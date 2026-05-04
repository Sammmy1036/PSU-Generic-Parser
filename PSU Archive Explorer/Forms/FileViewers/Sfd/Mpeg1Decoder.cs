using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace psu_archive_explorer
{
    /// <summary>
    /// MPEG-1 video decoder that uses pl_mpeg.dll (a ~200 KB native library)
    /// as the actual decoder. Input is a raw MPEG-1 video elementary stream
    /// </summary>
    internal class Mpeg1Decoder : IDisposable
    {
        // ---- P/Invoke into pl_mpeg.dll ----
        private const string Dll = "pl_mpeg";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr plmw_create(byte[] data, int length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void plmw_destroy(IntPtr w);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int plmw_get_width(IntPtr w);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int plmw_get_height(IntPtr w);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern double plmw_get_framerate(IntPtr w);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int plmw_decode_next(IntPtr w);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void plmw_copy_y(IntPtr w, byte[] dst);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void plmw_copy_cb(IntPtr w, byte[] dst);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void plmw_copy_cr(IntPtr w, byte[] dst);

        // ---- State ----
        private IntPtr _decoder;
        private byte[] _yBuf, _cbBuf, _crBuf;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public double Framerate { get; private set; }
        public string DebugLastInfo { get; private set; } = "";
        public int DebugTotalPicturesSeen { get; private set; }
        public int DebugIPicturesSeen { get; private set; }
        public int DebugPPicturesSeen { get; private set; }
        public int DebugBPicturesSeen { get; private set; }
        public int DebugOtherPicturesSeen { get; private set; }
        public int DebugEmittedAnchors { get; private set; }
        public int DebugEmittedBFrames { get; private set; }
        public int DebugEmittedFlushAnchor { get; private set; }
        public Stream DebugYuvDumpStream { get; set; }

        public Mpeg1Decoder(byte[] videoEs)
        {
            _decoder = plmw_create(videoEs, videoEs.Length);
            if (_decoder == IntPtr.Zero)
                throw new InvalidOperationException("plmw_create failed");

            Width = plmw_get_width(_decoder);
            Height = plmw_get_height(_decoder);
            Framerate = plmw_get_framerate(_decoder);

            _yBuf = new byte[Width * Height];
            _cbBuf = new byte[(Width / 2) * (Height / 2)];
            _crBuf = new byte[(Width / 2) * (Height / 2)];
        }

        public IEnumerable<Bitmap> DecodeFrames()
        {
            if (_decoder == IntPtr.Zero) yield break;

            while (plmw_decode_next(_decoder) != 0)
            {
                plmw_copy_y(_decoder, _yBuf);
                plmw_copy_cb(_decoder, _cbBuf);
                plmw_copy_cr(_decoder, _crBuf);

                DebugTotalPicturesSeen++;
                DebugEmittedBFrames++;

                if (DebugYuvDumpStream != null)
                {
                    DebugYuvDumpStream.Write(_yBuf, 0, _yBuf.Length);
                    DebugYuvDumpStream.Write(_cbBuf, 0, _cbBuf.Length);
                    DebugYuvDumpStream.Write(_crBuf, 0, _crBuf.Length);
                }

                DebugLastInfo = $"frame={DebugTotalPicturesSeen} {Width}x{Height}@{Framerate:F2}";

                yield return ProduceBitmap();
            }
        }

        private Bitmap ProduceBitmap()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, Width, Height);
            var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] row = new byte[stride];

            int cW = Width / 2;

            for (int y = 0; y < Height; y++)
            {
                int cY = y / 2;
                for (int x = 0; x < Width; x++)
                {
                    int cX = x / 2;
                    int Y = _yBuf[y * Width + x];
                    int Cb = _cbBuf[cY * cW + cX] - 128;
                    int Cr = _crBuf[cY * cW + cX] - 128;

                    // BT.601 YCbCr -> RGB
                    int R = Y + ((91881 * Cr) >> 16);
                    int G = Y - ((22554 * Cb + 46802 * Cr) >> 16);
                    int B = Y + ((116130 * Cb) >> 16);

                    if (R < 0) R = 0; else if (R > 255) R = 255;
                    if (G < 0) G = 0; else if (G > 255) G = 255;
                    if (B < 0) B = 0; else if (B > 255) B = 255;

                    int p = x * 4;
                    row[p + 0] = (byte)B;
                    row[p + 1] = (byte)G;
                    row[p + 2] = (byte)R;
                    row[p + 3] = 255;
                }
                Marshal.Copy(row, 0, bd.Scan0 + y * stride, stride);
            }

            bmp.UnlockBits(bd);
            return bmp;
        }

        public void Dispose()
        {
            if (_decoder != IntPtr.Zero)
            {
                plmw_destroy(_decoder);
                _decoder = IntPtr.Zero;
            }
        }

        ~Mpeg1Decoder() { Dispose(); }
    }
}