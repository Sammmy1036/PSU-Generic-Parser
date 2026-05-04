using System;
using System.IO;

namespace psu_archive_explorer
{
    /// <summary>
    /// ADX-to-WAV decoder specialized for Phantasy Star Universe audio files.
    ///
    /// PSU ships standard CRI ADX type 0x03 streams with 18 byte blocks,
    /// 4 bit samples, and an optional type 8 encryption that uses a single
    /// hardcoded key.
    /// </summary>
    public static class AdxDecoder
    {
        // PSU's type 8 encryption key "3x5k62bg9ptbwy" derives to below three LCG parameters
        private const ushort PsuKeyStart = 0x5deb;
        private const ushort PsuKeyMult = 0x5f27;
        private const ushort PsuKeyAdd = 0x673f;

        // PSU always uses 18 byte blocks with 4 bit samples, giving 32 samples
        // per block. Hardcoding these allows the decode loop be a tight scalar
        private const int BlockSize = 18;
        private const int SamplesPerBlock = 32;

        /// <summary>
        /// Decode PSU ADX bytes into a complete 16 bit PCM WAV file's bytes.
        /// </summary>
        public static byte[] DecodeToWav(byte[] adx)
        {
            // ---- Header ----
            if (adx.Length < 20)
                throw new InvalidDataException("File too small to be ADX.");
            if (adx[0] != 0x80 || adx[1] != 0x00)
                throw new InvalidDataException("Not an ADX file: Bad magic!");

            int copyrightOffset = (adx[2] << 8) | adx[3];
            byte encodingType = adx[4];
            byte blockSize = adx[5];
            byte sampleBitdepth = adx[6];
            int channels = adx[7];
            int sampleRate = (adx[8] << 24) | (adx[9] << 16) | (adx[10] << 8) | adx[11];
            int totalSamples = (adx[12] << 24) | (adx[13] << 16) | (adx[14] << 8) | adx[15];
            int highpassFreq = (adx[16] << 8) | adx[17];
            byte flags = adx[19];

            if (encodingType != 0x03)
                throw new NotSupportedException(
                    $"ADX encoding 0x{encodingType:X2} not supported (PSU uses 0x03).");
            if (blockSize != BlockSize)
                throw new NotSupportedException(
                    $"ADX block size {blockSize} not supported (PSU uses {BlockSize}).");
            if (sampleBitdepth != 4)
                throw new NotSupportedException(
                    $"ADX bit depth {sampleBitdepth} not supported (PSU uses 4).");
            if (channels < 1 || channels > 2)
                throw new InvalidDataException($"Unexpected channel count: {channels}.");
            if (totalSamples <= 0)
                throw new InvalidDataException("Non-positive sample count.");
            if (flags != 0x00 && flags != 0x08)
                throw new NotSupportedException($"Unsupported flags byte 0x{flags:X2}.");

            int audioStart = copyrightOffset + 4;
            int frameSize = BlockSize * channels;
            int blockCount = (totalSamples + SamplesPerBlock - 1) / SamplesPerBlock;

            // ---- Scales ----
            // Read every block's 2 byte scale in file order (L, R, L, R for stereo)
            ushort[] scales = new ushort[blockCount * channels];
            for (int bi = 0; bi < blockCount; bi++)
            {
                int frameOff = audioStart + bi * frameSize;
                for (int ch = 0; ch < channels; ch++)
                {
                    int blockOff = frameOff + BlockSize * ch;
                    scales[bi * channels + ch] =
                        blockOff + 1 < adx.Length
                            ? (ushort)((adx[blockOff] << 8) | adx[blockOff + 1])
                            : (ushort)0;
                }
            }

            // ---- Decrypt (if encrypted) ----
            // Type 8 encryption XORs each scale with successive LCG outputs.
            // Silent blocks all zero sample bytes are left unencrypted by the
            // encoder but the LCG still advances through them, so we XOR every
            // slot unconditionally and then validate on non silent blocks only.
            if (flags == 0x08)
            {
                bool[] silent = new bool[scales.Length];
                for (int bi = 0; bi < blockCount; bi++)
                {
                    int frameOff = audioStart + bi * frameSize;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int blockOff = frameOff + BlockSize * ch;
                        bool allZero = true;
                        int end = Math.Min(blockOff + BlockSize, adx.Length);
                        for (int o = blockOff + 2; o < end; o++)
                        {
                            if (adx[o] != 0) { allZero = false; break; }
                        }
                        silent[bi * channels + ch] = allZero;
                    }
                }

                ushort xor = PsuKeyStart;
                for (int i = 0; i < scales.Length; i++)
                {
                    scales[i] ^= xor;
                    xor = (ushort)(((xor * PsuKeyMult) + PsuKeyAdd) & 0x7fff);
                }

                // Plain text scales are 13 bit (<= 0x1fff)
                for (int i = 0; i < scales.Length; i++)
                {
                    if (!silent[i] && scales[i] > 0x1fff)
                        throw new NotSupportedException(
                            "ADX decryption failed: PSU key did not produce valid scales. " +
                            "File may be from a different game or use a non-standard variant.");
                }
            }

            // ---- Coefficients (Q12 fixed point) ----
            double z = Math.Cos(2.0 * Math.PI * highpassFreq / sampleRate);
            double a = Math.Sqrt(2.0) - z;
            double b = Math.Sqrt(2.0) - 1.0;
            double c = (a - Math.Sqrt((a + b) * (a - b))) / b;
            int coef1 = (int)Math.Floor(c * 2.0 * 4096.0);
            int coef2 = (int)Math.Floor(-(c * c) * 4096.0);

            // ---- Decode samples ----
            short[] pcm = new short[checked(totalSamples * channels)];
            int hist1L = 0, hist2L = 0, hist1R = 0, hist2R = 0;
            int outIdx = 0;
            int sampleIndex = 0;

            for (int bi = 0; bi < blockCount && sampleIndex < totalSamples; bi++)
            {
                int frameOff = audioStart + bi * frameSize;

                int samplesThisBlock = SamplesPerBlock;
                if (sampleIndex + samplesThisBlock > totalSamples)
                    samplesThisBlock = totalSamples - sampleIndex;

                // Actual scale used in decode is (stored_scale & 0x1fff) + 1.
                int scaleL = (scales[bi * channels] & 0x1FFF) + 1;
                int scaleR = channels == 2 ? (scales[bi * channels + 1] & 0x1FFF) + 1 : 0;

                int leftBlockOff = frameOff;
                int rightBlockOff = frameOff + BlockSize;

                for (int s = 0; s < samplesThisBlock; s++)
                {
                    // Left / mono channel
                    int byteOff = leftBlockOff + 2 + (s >> 1);
                    int nibble = (s & 1) == 0 ? (adx[byteOff] >> 4) & 0x0F : adx[byteOff] & 0x0F;
                    int signed = (nibble & 0x08) != 0 ? nibble - 16 : nibble;
                    int prediction = (coef1 * hist1L + coef2 * hist2L) >> 12;
                    int sample = prediction + signed * scaleL;
                    if (sample > 32767) sample = 32767;
                    else if (sample < -32768) sample = -32768;
                    pcm[outIdx++] = (short)sample;
                    hist2L = hist1L;
                    hist1L = sample;

                    // Right channel
                    if (channels == 2)
                    {
                        byteOff = rightBlockOff + 2 + (s >> 1);
                        nibble = (s & 1) == 0 ? (adx[byteOff] >> 4) & 0x0F : adx[byteOff] & 0x0F;
                        signed = (nibble & 0x08) != 0 ? nibble - 16 : nibble;
                        prediction = (coef1 * hist1R + coef2 * hist2R) >> 12;
                        sample = prediction + signed * scaleR;
                        if (sample > 32767) sample = 32767;
                        else if (sample < -32768) sample = -32768;
                        pcm[outIdx++] = (short)sample;
                        hist2R = hist1R;
                        hist1R = sample;
                    }
                }

                sampleIndex += samplesThisBlock;
            }

            // ---- Build WAV (PCM16 LE) ----
            int dataBytes = pcm.Length * 2;
            int riffSize = 36 + dataBytes;
            byte[] wav = new byte[8 + riffSize];
            int p = 0;

            // RIFF
            wav[p++] = (byte)'R'; wav[p++] = (byte)'I'; wav[p++] = (byte)'F'; wav[p++] = (byte)'F';
            wav[p++] = (byte)(riffSize); wav[p++] = (byte)(riffSize >> 8);
            wav[p++] = (byte)(riffSize >> 16); wav[p++] = (byte)(riffSize >> 24);
            wav[p++] = (byte)'W'; wav[p++] = (byte)'A'; wav[p++] = (byte)'V'; wav[p++] = (byte)'E';

            // fmt
            wav[p++] = (byte)'f'; wav[p++] = (byte)'m'; wav[p++] = (byte)'t'; wav[p++] = (byte)' ';
            wav[p++] = 16; wav[p++] = 0; wav[p++] = 0; wav[p++] = 0;   // chunk size
            wav[p++] = 1; wav[p++] = 0;                                // PCM
            wav[p++] = (byte)channels; wav[p++] = 0;
            wav[p++] = (byte)(sampleRate); wav[p++] = (byte)(sampleRate >> 8);
            wav[p++] = (byte)(sampleRate >> 16); wav[p++] = (byte)(sampleRate >> 24);
            int byteRate = sampleRate * channels * 2;
            wav[p++] = (byte)(byteRate); wav[p++] = (byte)(byteRate >> 8);
            wav[p++] = (byte)(byteRate >> 16); wav[p++] = (byte)(byteRate >> 24);
            wav[p++] = (byte)(channels * 2); wav[p++] = 0;              // block align
            wav[p++] = 16; wav[p++] = 0;                                // bits/sample

            // data
            wav[p++] = (byte)'d'; wav[p++] = (byte)'a'; wav[p++] = (byte)'t'; wav[p++] = (byte)'a';
            wav[p++] = (byte)(dataBytes); wav[p++] = (byte)(dataBytes >> 8);
            wav[p++] = (byte)(dataBytes >> 16); wav[p++] = (byte)(dataBytes >> 24);
            for (int i = 0; i < pcm.Length; i++)
            {
                wav[p++] = (byte)(pcm[i] & 0xFF);
                wav[p++] = (byte)((pcm[i] >> 8) & 0xFF);
            }

            return wav;
        }
    }
}
