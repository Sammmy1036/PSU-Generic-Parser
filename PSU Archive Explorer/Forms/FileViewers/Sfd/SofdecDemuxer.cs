using System;
using System.Collections.Generic;

namespace psu_archive_explorer
{
    /// <summary>
    /// Sofdec SFD (MPEG-1 System Stream) demuxer.
    /// Parses a .sfd container and splits it into:
    ///   - VideoFrames: raw MPEG-1 video elementary stream (fed to Mpeg1Decoder)
    ///   - AdxFrames:   headerless ADX audio frames (wrapped with a header and
    ///                  decoded via AdxDecoder → WAV → NAudio for playback)
    /// </summary>
    internal class SofdecDemuxer
    {
        public List<byte[]> AdxFrames { get; } = new List<byte[]>();
        public List<byte[]> VideoFrames { get; } = new List<byte[]>();

        public int Channels { get; private set; } = 2;
        public int SampleRate { get; private set; } = 48000;
        private byte _audioSubstreamId = 0xFF;

        public byte[] GetAdxPayload() { return Concat(AdxFrames); }
        public byte[] GetVideoPayload() { return Concat(VideoFrames); }

        private static byte[] Concat(List<byte[]> chunks)
        {
            int total = 0;
            foreach (var c in chunks) total += c.Length;
            byte[] result = new byte[total];
            int p = 0;
            foreach (var c in chunks)
            {
                Buffer.BlockCopy(c, 0, result, p, c.Length);
                p += c.Length;
            }
            return result;
        }

        public void Parse(byte[] data)
        {
            int pos = 0;
            int len = data.Length;

            while (pos + 4 <= len)
            {
                if (!(data[pos] == 0x00 && data[pos + 1] == 0x00 && data[pos + 2] == 0x01))
                {
                    pos++;
                    continue;
                }

                byte startCode = data[pos + 3];
                pos += 4;

                switch (startCode)
                {
                    case 0xBA: pos = SkipPackHeader(data, pos); break;
                    case 0xBB:
                    case 0xBC: pos = SkipLengthPrefixed(data, pos); break;
                    case 0xB9: return;

                    case 0xBD:
                    case 0xC0:
                    case 0xE0:
                        pos = HandlePes(data, pos, startCode);
                        break;

                    default:
                        if (startCode >= 0xC0 && startCode <= 0xEF)
                            pos = HandlePes(data, pos, startCode);
                        else
                            pos = SkipLengthPrefixed(data, pos);
                        break;
                }
            }
        }

        private static int SkipPackHeader(byte[] data, int pos)
        {
            if (pos >= data.Length) return data.Length;
            if ((data[pos] & 0xF0) == 0x20) return Math.Min(pos + 8, data.Length);
            if (pos + 10 > data.Length) return data.Length;
            int stuffing = data[pos + 9] & 0x07;
            return Math.Min(pos + 10 + stuffing, data.Length);
        }

        private static int SkipLengthPrefixed(byte[] data, int pos)
        {
            if (pos + 2 > data.Length) return data.Length;
            int size = (data[pos] << 8) | data[pos + 1];
            return Math.Min(pos + 2 + size, data.Length);
        }

        private int HandlePes(byte[] data, int pos, byte streamId)
        {
            if (pos + 2 > data.Length) return data.Length;
            int pesLen = (data[pos] << 8) | data[pos + 1];
            pos += 2;
            int pesEnd = pesLen > 0 ? Math.Min(pos + pesLen, data.Length) : data.Length;
            int payloadStart = pos;

            while (payloadStart < pesEnd && data[payloadStart] == 0xFF)
                payloadStart++;

            if (payloadStart + 2 <= pesEnd && (data[payloadStart] & 0xC0) == 0x40)
                payloadStart += 2;

            if (payloadStart >= pesEnd) return pesEnd;

            byte flag = data[payloadStart];

            if ((flag & 0xC0) == 0x80)
            {
                if (payloadStart + 3 > pesEnd) return pesEnd;
                int hdrDataLen = data[payloadStart + 2];
                payloadStart += 3 + hdrDataLen;
            }
            else
            {
                if ((flag & 0xF0) == 0x20) payloadStart += 5;
                else if ((flag & 0xF0) == 0x30) payloadStart += 10;
                else if (flag == 0x0F) payloadStart += 1;
            }

            if (payloadStart >= pesEnd) return pesEnd;

            if (streamId == 0xBD)
            {
                byte sub = data[payloadStart];
                payloadStart++;
                bool isAdxSub = (sub >= 0x40 && sub <= 0x5F);
                if (!isAdxSub) return pesEnd;
                if (_audioSubstreamId == 0xFF) _audioSubstreamId = sub;
                if (sub != _audioSubstreamId) return pesEnd;
                if (payloadStart + 3 > pesEnd) return pesEnd;
                payloadStart += 3;
                AppendPayload(data, payloadStart, pesEnd - payloadStart, AdxFrames);
            }
            else if (streamId >= 0xC0 && streamId <= 0xDF)
            {
                AppendPayload(data, payloadStart, pesEnd - payloadStart, AdxFrames);
            }
            else if (streamId >= 0xE0 && streamId <= 0xEF)
            {
                AppendPayload(data, payloadStart, pesEnd - payloadStart, VideoFrames);
            }

            return pesEnd;
        }

        private static void AppendPayload(byte[] src, int start, int len, List<byte[]> dest)
        {
            if (len <= 0) return;
            byte[] chunk = new byte[len];
            Buffer.BlockCopy(src, start, chunk, 0, len);
            dest.Add(chunk);
        }
    }
}