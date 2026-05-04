using System;
using System.IO;
using System.Linq;
using System.Text;

namespace psu_archive_explorer
{
    internal static class DatConverter
    {
        // Valid magic signatures for .DAT sound files.
        private static readonly byte[] SIG_DDNS = Encoding.ASCII.GetBytes("xobxDDNS");
        private static readonly byte[] SIG_KPTD = Encoding.ASCII.GetBytes("xobxKPTD");
        private static readonly byte[][] VALID_SIGNATURES = { SIG_DDNS, SIG_KPTD };

        private static (int Position, string Name) FindLastSignature(byte[] rawData, int searchLimit = 8192)
        {
            int limit = Math.Min(rawData.Length, searchLimit);
            int bestPos = -1;
            string bestName = null;

            foreach (var sig in VALID_SIGNATURES)
            {
                int pos = LastIndexOf(rawData, sig, limit);
                if (pos > bestPos)
                {
                    bestPos = pos;
                    bestName = Encoding.ASCII.GetString(sig);
                }
            }

            return (bestPos, bestName);
        }

        private static int LastIndexOf(byte[] data, byte[] pattern, int searchLimit)
        {
            if (pattern.Length == 0 || pattern.Length > searchLimit) return -1;

            for (int i = searchLimit - pattern.Length; i >= 0; i--)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        // ====================== Public API for MainForm batch exports ======================

        /// <summary>
        /// Checks that the buffer contains a valid PSU sound DAT signature
        /// (xobxDDNS or xobxKPTD) within the first 8 KB and used by batch exporter to
        /// decide whether to route a .dat through DecodeToWav or extract it raw.
        /// </summary>
        public static bool IsSoundDat(byte[] rawData)
        {
            if (rawData == null || rawData.Length < 16)
                return false;

            var (sigPos, _) = FindLastSignature(rawData);
            return sigPos >= 0;
        }

        /// <summary>
        /// Path overload of IsSoundDat. Reads only the leading bytes needed for the
        /// signature scan so it stays cheap on large files.
        /// </summary>
        public static bool IsSoundDat(string path)
        {
            try
            {
                const int scanLen = 8192;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int toRead = (int)Math.Min(fs.Length, scanLen);
                    if (toRead < 16) return false;
                    byte[] buf = new byte[toRead];
                    int read = fs.Read(buf, 0, toRead);
                    if (read < 16) return false;
                    if (read < toRead) Array.Resize(ref buf, read);
                    return IsSoundDat(buf);
                }
            }
            catch
            {
                return false;
            }
        }
        /// Convert raw .dat bytes directly to .wav bytes.
        public static byte[] DecodeToWav(
            byte[] rawData,
            int sampleRate = 44100,
            int channels = 1,
            int sampWidth = 2)
        {
            if (rawData == null || rawData.Length < 16)
                throw new InvalidDataException("DAT buffer is empty or too small.");

            var (sigPos, _) = FindLastSignature(rawData);
            if (sigPos < 0)
                throw new InvalidDataException("No valid xobxDDNS / xobxKPTD signature found.");

            if (rawData.Length < sigPos + 16)
                throw new InvalidDataException("DAT buffer too short to read metadata header.");

            int bytesPerFrame = sampWidth * channels;

            // Read declared audio size from metadata
            uint declaredSize = BitConverter.ToUInt32(rawData, sigPos + 8 + 4);
            if (!BitConverter.IsLittleEndian)
                declaredSize = ReverseBytes(declaredSize);

            int audioStart;
            byte[] audioData;
            int remainingAfterSig = rawData.Length - (sigPos + 8);

            if (declaredSize > 0 && declaredSize <= (uint)remainingAfterSig)
            {
                audioStart = rawData.Length - (int)declaredSize;
                audioData = new byte[declaredSize];
                Buffer.BlockCopy(rawData, audioStart, audioData, 0, (int)declaredSize);
            }
            else
            {
                audioStart = sigPos + 8;
                audioData = new byte[rawData.Length - audioStart];
                Buffer.BlockCopy(rawData, audioStart, audioData, 0, audioData.Length);
            }

            int trim = audioData.Length % bytesPerFrame;
            if (trim != 0)
                Array.Resize(ref audioData, audioData.Length - trim);

            if (audioData.Length < 100)
                throw new InvalidDataException("Audio payload too small after stripping header.");

            int byteRate = sampleRate * channels * sampWidth;
            short blockAlign = (short)(channels * sampWidth);
            short bitsPerSample = (short)(sampWidth * 8);

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + audioData.Length);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)channels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(audioData.Length);
                bw.Write(audioData);
                return ms.ToArray();
            }
        }

        // ====================== Original CLI implementation (unchanged) ======================

        /// <summary>
        ///   1. Verifies the file contains a known signature
        ///   2. Reads the audio size field from the metadata block after the signature
        ///   3. Skips exactly the file size - audio size bytes from the start, so the WAV contains only the real PCM audio and none of the header
        /// </summary>
        private static void ConvertDatToWav(
            string datPath,
            int sampleRate,
            int channels,
            int sampWidth,
            int headerOffset = 0,
            bool diagnose = false)
        {
            string wavPath = Path.ChangeExtension(datPath, ".wav");
            string datName = Path.GetFileName(datPath);

            byte[] rawData = File.ReadAllBytes(datPath);

            var (sigPos, sigName) = FindLastSignature(rawData);
            if (sigPos < 0)
            {
                Console.WriteLine($"Skipping (no valid signature found): {datName}");
                return;
            }

            int bytesPerFrame = sampWidth * channels;

            // The metadata block after the signature contains a 32 bit little endian
            // value at offset +4 that equals the size of the PCM audio payload
            // Use that to compute the exact start of the audio data
            int audioStart;
            byte[] audioData;
            string detectionMethod;

            if (headerOffset != 0)
            {
                audioStart = sigPos + 8 + headerOffset;
                if (audioStart < 0 || audioStart > rawData.Length)
                {
                    Console.WriteLine($"!! Manual header-offset puts audio start out of range: {datName}");
                    return;
                }
                audioData = new byte[rawData.Length - audioStart];
                Buffer.BlockCopy(rawData, audioStart, audioData, 0, audioData.Length);
                detectionMethod = $"manual header-offset={headerOffset}";
            }
            else
            {
                // Read declared audio size from metadata
                // Field layout after signature
                //   +0 : uint32  unknown / flags
                //   +4 : uint32  audio data size in bytes
                if (rawData.Length < sigPos + 16)
                {
                    Console.WriteLine($"!! File too short to read header: {datName}");
                    return;
                }

                uint declaredSize = BitConverter.ToUInt32(rawData, sigPos + 8 + 4);
                if (!BitConverter.IsLittleEndian)
                {
                    declaredSize = ReverseBytes(declaredSize);
                }

                int remainingAfterSig = rawData.Length - (sigPos + 8);
                if (declaredSize > 0 && declaredSize <= (uint)remainingAfterSig)
                {
                    audioStart = rawData.Length - (int)declaredSize;
                    audioData = new byte[declaredSize];
                    Buffer.BlockCopy(rawData, audioStart, audioData, 0, (int)declaredSize);
                    detectionMethod = $"auto (declared size={declaredSize})";
                }
                else
                {
                    // everything after signature
                    audioStart = sigPos + 8;
                    audioData = new byte[rawData.Length - audioStart];
                    Buffer.BlockCopy(rawData, audioStart, audioData, 0, audioData.Length);
                    detectionMethod = $"fallback (declared size {declaredSize} invalid)";
                }
            }

            int trim = audioData.Length % bytesPerFrame;
            if (trim != 0)
            {
                Array.Resize(ref audioData, audioData.Length - trim);
            }

            if (diagnose)
            {
                Console.WriteLine(new string('=', 70));
                Console.WriteLine($"File: {datName}");
                Console.WriteLine($"  File size:            {rawData.Length} bytes");
                Console.WriteLine($"  Last signature:       '{sigName}' at offset {sigPos}");
                Console.WriteLine($"  Detection method:     {detectionMethod}");
                Console.WriteLine($"  Audio start offset:   {audioStart}");
                Console.WriteLine($"  Header size:          {audioStart} bytes total " +
                                  $"({audioStart - sigPos - 8} after signature)");
                Console.WriteLine($"  Audio data length:    {audioData.Length} bytes");

                double sec = audioData.Length / (double)(sampleRate * bytesPerFrame);
                Console.WriteLine($"  Playback duration:    {sec:F3} sec at {sampleRate} Hz, " +
                                  $"{channels}ch, {sampWidth * 8}-bit");

                int previewLen = Math.Min(64, Math.Max(0, rawData.Length - (sigPos + 8)));
                var preview = new byte[previewLen];
                Buffer.BlockCopy(rawData, sigPos + 8, preview, 0, previewLen);
                string hexStr = string.Join(" ", preview.Select(b => b.ToString("X2")));
                Console.WriteLine("  First 64 bytes after signature:");
                Console.WriteLine($"    {hexStr}");

                int audioPreviewLen = Math.Min(16, audioData.Length);
                var audioPreview = new byte[audioPreviewLen];
                Buffer.BlockCopy(audioData, 0, audioPreview, 0, audioPreviewLen);
                string audioHex = string.Join(" ", audioPreview.Select(b => b.ToString("X2")));
                Console.WriteLine("  First 16 bytes of audio data:");
                Console.WriteLine($"    {audioHex}");
                Console.WriteLine(new string('=', 70));
            }

            if (audioData.Length < 100)
            {
                Console.WriteLine($"!! Audio data too small after header: {datName}");
                return;
            }

            WriteWav(wavPath, audioData, sampleRate, channels, sampWidth);

            double seconds = audioData.Length / (double)(sampleRate * bytesPerFrame);
            Console.WriteLine($"[{sigName}] {datName} -> {Path.GetFileName(wavPath)}  ({seconds:F2}s)");
        }

        /// <summary>
        /// Write a standard PCM WAV file (RIFF / fmt / data chunks).
        /// </summary>
        private static void WriteWav(string path, byte[] pcm, int sampleRate, int channels, int sampWidth)
        {
            int byteRate = sampleRate * channels * sampWidth;
            short blockAlign = (short)(channels * sampWidth);
            short bitsPerSample = (short)(sampWidth * 8);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // RIFF header
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + pcm.Length);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                // fmt sub-chunk
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)channels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);

                // data sub-chunk
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(pcm.Length);
                bw.Write(pcm);
            }
        }

        private static uint ReverseBytes(uint value)
        {
            return ((value & 0x000000FFu) << 24) |
                   ((value & 0x0000FF00u) << 8) |
                   ((value & 0x00FF0000u) >> 8) |
                   ((value & 0xFF000000u) >> 24);
        }

        // ----------------- CLI -----------------

        private static int Run(string[] args)
        {
            string input = null;
            int sampleRate = 44100;
            int channels = 1;
            int sampWidth = 2;
            int headerOffset = 0;
            bool diagnose = false;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    switch (a)
                    {
                        case "--sample-rate":
                            sampleRate = int.Parse(args[++i]);
                            break;
                        case "--channels":
                            channels = int.Parse(args[++i]);
                            break;
                        case "--sampwidth":
                            sampWidth = int.Parse(args[++i]);
                            if (sampWidth != 1 && sampWidth != 2)
                                throw new ArgumentException("--sampwidth must be 1 or 2");
                            break;
                        case "--header-offset":
                            headerOffset = int.Parse(args[++i]);
                            break;
                        case "--diagnose":
                            diagnose = true;
                            break;
                        case "-h":
                        case "--help":
                            PrintUsage();
                            return 0;
                        default:
                            if (a.StartsWith("-"))
                                throw new ArgumentException($"Unknown option: {a}");
                            if (input != null)
                                throw new ArgumentException("Only one input path allowed.");
                            input = a;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Argument error: {ex.Message}");
                PrintUsage();
                return 2;
            }

            if (string.IsNullOrEmpty(input))
            {
                PrintUsage();
                return 2;
            }

            string fullPath = Path.GetFullPath(input);

            if (File.Exists(fullPath))
            {
                if (string.Equals(Path.GetExtension(fullPath), ".dat", StringComparison.OrdinalIgnoreCase))
                {
                    ConvertDatToWav(fullPath, sampleRate, channels, sampWidth, headerOffset, diagnose);
                }
                else
                {
                    Console.WriteLine($"Error: Not a .dat file: {fullPath}");
                }
            }
            else if (Directory.Exists(fullPath))
            {
                var datFiles = Directory.GetFiles(fullPath, "*.dat", SearchOption.TopDirectoryOnly)
                                        .OrderBy(p => p, StringComparer.Ordinal)
                                        .ToArray();
                if (datFiles.Length == 0)
                {
                    Console.WriteLine($"No .dat files found in folder: {fullPath}");
                    return 0;
                }
                foreach (var f in datFiles)
                {
                    ConvertDatToWav(f, sampleRate, channels, sampWidth, headerOffset, diagnose);
                }
            }
            else
            {
                Console.WriteLine($"Error: Path not found: {fullPath}");
                return 1;
            }

            return 0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Convert .DAT (xobxDDNS / xobxKPTD container) to .WAV");
            Console.WriteLine();
            Console.WriteLine("Usage: Dat2Wav <input> [options]");
            Console.WriteLine("  <input>                 Single .dat file or folder");
            Console.WriteLine("  --sample-rate <int>     Default 44100");
            Console.WriteLine("  --channels <int>        Default 1");
            Console.WriteLine("  --sampwidth <1|2>       Bytes per sample, default 2");
            Console.WriteLine("  --header-offset <int>   Extra bytes to skip after the signature.");
            Console.WriteLine("  --diagnose              Print header details and hex preview.");
        }
    }
}