using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PSULib.FileClasses.General;

namespace PSULib.FileClasses.Models
{
    /// <summary>
    /// Read/write parser for XNR files tied to specific UI Files
    /// xnr's are weird...
    /// NXR-magic'd file whose body is a flat little-endian float32 array
    /// what these points actually do depends on the specific .xnr.
    /// Some of them adjust the size of elements or spacing of certain elements
    /// The parser looks if header is NXIF, then locates the "NXR\0" magic
    /// Skips 8 bytes past the magic float blob starts there and then walks the
    /// rest of the buffer 4 bytes at a time as float32 and groups consecutive floats into pairs and assigns them points
    /// Probably a stupid way of doing this but thats the best I came up with to make this easily editable
    /// It writes through to RawBytes immediately and sets dirty=true
    /// that way the PsuFile.ToRawFile() repacks correctly.. learned this the hard way
    /// </summary>
    public class XnrFile : PsuFile
    {
        // ---- Public data model ----
        public List<XnrPoint> Points { get; } = new List<XnrPoint>();
        public byte[] RawBytes { get; private set; }
        public string ParseError { get; private set; }
        public string ParseInfo { get; private set; }

        /// <summary>Byte position in RawBytes where the float blob starts
        /// (8 bytes after the "NXR\0" magic). -1 if the magic was not
        /// found and points were not parsed.</summary>
        public int FloatStart { get; private set; } = -1;

        private float? _trailingOddFloat;
        private int _ownerOffset;

        private int[] _originalPtrs;
        private int _originalFileOffset;

        // ---- Constructors ----
        public XnrFile(string filename, byte[] rawData, byte[] inHeader, int[] ptrs, int baseAddr)
        {
            this.filename = filename;
            this.header = inHeader;
            _originalPtrs = ptrs;
            _originalFileOffset = baseAddr;
            this.calculatedPointers = null;  // populated lazily in ToRaw()
            RawBytes = rawData;

            try
            {
                byte[] toParse = ReconstructFile(rawData, inHeader);
                if (toParse == null || toParse.Length < 12)
                {
                    ParseError = "Buffer too small or null";
                    return;
                }

                _ownerOffset = toParse.Length - (rawData?.Length ?? 0);
                if (_ownerOffset < 0) _ownerOffset = 0;

                int nxrPos = FindBytes(toParse, "NXR\0", 0);
                if (nxrPos < 0)
                {
                    ParseError = "NXR magic not found in buffer";
                    return;
                }

                int floatStart = nxrPos + 8;
                if (floatStart >= toParse.Length)
                {
                    ParseError = "Float blob start past end of buffer";
                    return;
                }

                FloatStart = floatStart - _ownerOffset;
                if (FloatStart < 0) FloatStart = floatStart;

                ParseFloats(toParse, floatStart);

                ParseInfo =
                    "toParse: " + toParse.Length + " bytes, " +
                    "NXR at 0x" + nxrPos.ToString("X") + ", " +
                    "float start at 0x" + floatStart.ToString("X") + ", " +
                    Points.Count + " points parsed";
            }
            catch (Exception ex)
            {
                ParseError = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }
        }

        public XnrFile() { }

        public List<string> EditLog { get; } = new List<string>();
        private const int MaxEditLog = 30;

        private void RecordEdit(int pos, byte[] before, byte[] after, string fieldKind)
        {
            string entry = string.Format("0x{0:X}: {1} -> {2} ({3})",
                pos, BitConverter.ToString(before), BitConverter.ToString(after), fieldKind);
            EditLog.Add(entry);
            if (EditLog.Count > MaxEditLog) EditLog.RemoveAt(0);
        }

        internal void WriteFloat(int pos, float value)
        {
            if (RawBytes == null || pos < 0 || pos + 4 > RawBytes.Length) return;
            byte[] before = new byte[4];
            Buffer.BlockCopy(RawBytes, pos, before, 0, 4);
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, RawBytes, pos, 4);
            RecordEdit(pos, before, bytes, "float=" + value);
            dirty = true;
        }

        public override byte[] ToRaw()
        {

            if (_originalPtrs == null || _originalPtrs.Length == 0
                || RawBytes == null)
            {
                this.calculatedPointers = new int[0];
                return RawBytes;
            }

            byte[] copy = (byte[])RawBytes.Clone();
            var rawRelativePositions = new List<int>(_originalPtrs.Length);
            foreach (int absPos in _originalPtrs)
            {
                int rel = absPos - _originalFileOffset;
                if (rel < 0 || rel + 4 > copy.Length) continue;

                int currentValue = BitConverter.ToInt32(copy, rel);
                int unrelocated = currentValue - _originalFileOffset;
                Buffer.BlockCopy(BitConverter.GetBytes(unrelocated), 0,
                                 copy, rel, 4);
                rawRelativePositions.Add(rel);
            }

            this.calculatedPointers = rawRelativePositions.ToArray();
            return copy;
        }

        // ---- Buffer reconstruction (copied from XncpFile) -------------------
        private static byte[] ReconstructFile(byte[] rawData, byte[] inHeader)
        {
            if (rawData == null) return null;
            if (inHeader == null || inHeader.Length == 0) return rawData;
            string headerSig = inHeader.Length >= 4
                ? Encoding.ASCII.GetString(inHeader, 0, 4) : "";
            if (headerSig == "NXIF" || headerSig == "NYIF")
            {
                byte[] combined = new byte[inHeader.Length + rawData.Length];
                Buffer.BlockCopy(inHeader, 0, combined, 0, inHeader.Length);
                Buffer.BlockCopy(rawData, 0, combined, inHeader.Length, rawData.Length);
                return combined;
            }
            return rawData;
        }

        private static int FindBytes(byte[] buf, string magic, int startAt)
        {
            byte[] needle = Encoding.ASCII.GetBytes(magic);
            for (int i = startAt; i <= buf.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (buf[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        // ---- Float blob walker ---------------------------------------------
        private void ParseFloats(byte[] toParse, int floatStartInToParse)
        {
            int blobLen = toParse.Length - floatStartInToParse;
            int floatCount = blobLen / 4;

            for (int i = 0; i + 1 < floatCount; i += 2)
            {
                int v1ToParsePos = floatStartInToParse + i * 4;
                int v2ToParsePos = floatStartInToParse + (i + 1) * 4;

                float v1 = BitConverter.ToSingle(toParse, v1ToParsePos);
                float v2 = BitConverter.ToSingle(toParse, v2ToParsePos);

                var pt = new XnrPoint
                {
                    PointNumber = (i / 2) + 1,    // 1-indexed user facing
                    FloatPairIndex = i / 2,       // 0-indexed internal
                };
                pt.SetValue1Raw(v1);
                pt.SetValue2Raw(v2);

                // Translate toParse positions to RawBytes positions.
                pt._owner = this;
                pt._value1Pos = v1ToParsePos - _ownerOffset;
                pt._value2Pos = v2ToParsePos - _ownerOffset;

                Points.Add(pt);
            }
            if (floatCount % 2 == 1)
            {
                int lastPos = floatStartInToParse + (floatCount - 1) * 4;
                _trailingOddFloat = BitConverter.ToSingle(toParse, lastPos);
            }
        }
    }

    /// <summary>
    /// A single (Value1, Value2) point in an XNR file. Edits to Value1 /
    /// Value2 write back through to the owning XnrFile's RawBytes buffer
    /// </summary>
    public class XnrPoint
    {
        internal XnrFile _owner;
        internal int _value1Pos = -1;
        internal int _value2Pos = -1;

        /// <summary>1-based point number for display (matches the Python
        /// prototype's "Point #N" labels).</summary>
        public int PointNumber { get; set; }

        /// 0-based pair index (Points[k].FloatPairIndex == k)
        public int FloatPairIndex { get; set; }

        private float _value1;
        public float Value1
        {
            get { return _value1; }
            set
            {
                _value1 = value;
                if (_owner != null && _value1Pos >= 0)
                    _owner.WriteFloat(_value1Pos, value);
            }
        }

        private float _value2;
        public float Value2
        {
            get { return _value2; }
            set
            {
                _value2 = value;
                if (_owner != null && _value2Pos >= 0)
                    _owner.WriteFloat(_value2Pos, value);
            }
        }

        public bool IsZeroPoint
        {
            get
            {
                return Math.Abs(_value1) < 1e-8f && Math.Abs(_value2) < 1e-8f;
            }
        }

        /// True when either value is NaN or infinite
        public bool HasNaNOrInf
        {
            get
            {
                return float.IsNaN(_value1) || float.IsInfinity(_value1)
                    || float.IsNaN(_value2) || float.IsInfinity(_value2);
            }
        }

        // Parser only setters that bypass byte write thru during loading
        internal void SetValue1Raw(float v) { _value1 = v; }
        internal void SetValue2Raw(float v) { _value2 = v; }

        public override string ToString()
        {
            return string.Format("Point #{0} | {1:F3} | {2:F3}",
                PointNumber, _value1, _value2);
        }
    }
}