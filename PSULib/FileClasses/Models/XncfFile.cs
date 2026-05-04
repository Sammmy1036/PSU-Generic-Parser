using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PSULib.FileClasses.General;
using PSULib.Support;

namespace PSULib.FileClasses.Models
{
    /// <summary>
    /// Read/write parser for XNCF (Ninja CellSpriteDraw Font-list) files.
    ///
    /// XNCF is the companion file that ships next to many XNCPs in PSU
    /// (e.g. interface.xncf next to interface.xncp). It contains *only* a
    /// font list -- the same structure XNCP has embedded, just hoisted out
    /// into its own NCF-wrapped container with an nCFL chunk instead of nCPJ.
    ///
    /// Container layout (verified against PSU's interface.xncf):
    ///
    ///   +0x00  outer wrapper "NCF\0", 0x40 bytes total
    ///   +0x40  NXIF info chunk (8-byte header + 0x18 of fields)
    ///   +0x60  nCFL chunk:
    ///             header: 4-byte sig "nCFL", u32 size
    ///             body:
    ///               +0x00  field08            (= 0x10, base position)
    ///               +0x04  field0C            (= 0)
    ///               +0x08  font_count
    ///               +0x0C  table_off          -> array of (u32 char_count, u32 map_off)
    ///               +0x10  id_table_off       -> array of (u32 name_off,   u32 index)
    ///               +0x14..0x1C  internal redundant offsets (preserved as-is)
    ///         All offsets are TNNOffsets relative to the nCFL chunk start.
    ///
    ///   Each char map entry is 8 bytes: (u32 char_code, u32 sub_image_index).
    ///
    /// Strategy mirrors XncpFile: use string-anchored relocation detection so
    /// we don't trust the pointer table or baseAddr. Anchor on the inner-name
    /// string ("interface.xncf"-style) at wrapper +0x10, since unlike XNCP we
    /// can't anchor on "Root" -- there are no scenes here.
    /// </summary>
    public class XncfFile : PsuFile
    {
        // ---- Public data model ----
        public List<XncfFont> Fonts { get; } = new List<XncfFont>();
        public byte[] RawBytes { get; private set; }
        public string ParseError { get; private set; }
        public string ParseInfo { get; private set; }

        // How many bytes were prepended to RawBytes when forming the parse
        // buffer (toParse = inHeader + RawBytes when inHeader looks like
        // NXIF). Edit setters use this to translate parse-buffer positions
        // back to RawBytes positions when writing edits.
        private int _ownerOffset;

        // Preserved from the dispatcher for proper repack-with-relocation.
        // Same pattern as XncpFile -- see that class for the rationale.
        private int[] _originalPtrs;
        private int _originalFileOffset;

        // ---- Constructors ----
        public XncfFile(string filename, byte[] rawData, byte[] inHeader, int[] ptrs, int baseAddr)
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
                if (toParse == null || toParse.Length < 32)
                {
                    ParseError = "Buffer too small or null";
                    return;
                }

                _ownerOffset = toParse.Length - (rawData?.Length ?? 0);
                if (_ownerOffset < 0) _ownerOffset = 0;

                int ncflPos = FindBytes(toParse, "nCFL", 0);
                if (ncflPos < 0)
                {
                    ParseError = "nCFL chunk not found in buffer";
                    return;
                }

                // String-anchored relocation detection. The delta returned
                // tells us how to translate stored (relocated) offsets back
                // into buffer positions. See DetectDelta for the rationale.
                int delta = DetectDelta(toParse, ncflPos, out string detectInfo);

                ParseInfo =
                    "toParse: " + toParse.Length + " bytes, " +
                    "nCFL at 0x" + ncflPos.ToString("X") + ", " +
                    detectInfo;

                Parse(toParse, ncflPos, delta);
                if (!string.IsNullOrEmpty(_parseDiag))
                    ParseInfo += " | " + _parseDiag;
            }
            catch (Exception ex)
            {
                ParseError = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }
        }

        public XncfFile() { }

        /// <summary>
        /// Return bytes that PsuFile.ToRawFile() can correctly re-relocate.
        /// Same semantics as XncpFile.ToRaw() -- see that for the full
        /// explanation of the un-relocate-then-let-base-relocate pattern.
        /// </summary>
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

        // ---- In-place byte editors -------------------------------------------
        // Same shape as XncpFile's helpers. XncfFontGlyph uses these via its
        // owner reference to commit edits back into RawBytes.

        public List<string> EditLog { get; } = new List<string>();
        private const int MaxEditLog = 30;

        private void RecordEdit(int pos, byte[] before, byte[] after, string fieldKind)
        {
            string entry = $"0x{pos:X}: {BitConverter.ToString(before)} -> "
                         + $"{BitConverter.ToString(after)} ({fieldKind})";
            EditLog.Add(entry);
            if (EditLog.Count > MaxEditLog) EditLog.RemoveAt(0);
        }

        internal void WriteUInt32(int pos, uint value)
        {
            if (RawBytes == null || pos < 0 || pos + 4 > RawBytes.Length) return;
            byte[] before = new byte[4];
            Buffer.BlockCopy(RawBytes, pos, before, 0, 4);
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, RawBytes, pos, 4);
            RecordEdit(pos, before, bytes, "uint=" + value);
            dirty = true;
        }

        internal void WriteInt32(int pos, int value)
        {
            if (RawBytes == null || pos < 0 || pos + 4 > RawBytes.Length) return;
            byte[] before = new byte[4];
            Buffer.BlockCopy(RawBytes, pos, before, 0, 4);
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, RawBytes, pos, 4);
            RecordEdit(pos, before, bytes, "int=" + value);
            dirty = true;
        }

        // ---- Buffer reconstruction -------------------------------------------
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

        // ---- Scanning helpers ------------------------------------------------
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

        // Find a printable ASCII C-string of at least `minLen` chars at or
        // after `start`. Used as a fallback anchor for relocation detection.
        private static int FindAnyAsciiCString(byte[] buf, int start, int end, int minLen)
        {
            int run = 0;
            int runStart = -1;
            int limit = Math.Min(end, buf.Length);
            for (int i = start; i < limit; i++)
            {
                byte b = buf[i];
                if (b >= 0x20 && b < 0x7F)
                {
                    if (run == 0) runStart = i;
                    run++;
                }
                else
                {
                    if (b == 0 && run >= minLen) return runStart;
                    run = 0;
                    runStart = -1;
                }
            }
            return -1;
        }

        // ---- Relocation delta detection --------------------------------------
        //
        // Compute `delta` such that for any stored relative offset O found in
        // the file, the correct absolute position in `buf` is (ncflPos+O)-delta.
        //
        // Why this is needed: PSULib's loader adds `fileOffset` (the file's
        // archive position) to every pointer value in rawData during load.
        // So a stored offset that was originally 0x1C in the on-disk file
        // becomes 0x1C + fileOffset in the buffer we receive. We can't know
        // fileOffset directly from the parse, so we detect it indirectly.
        //
        // Strategy:
        //   (1) PROBE delta=0 (no relocation, common for top-level loads).
        //       If reading the table offset and following it lands on a
        //       (char_count, map_off) pair where char_count looks sane, we're
        //       done.
        //   (2) ANCHOR-SCAN: find any printable ASCII C-string >= 3 chars
        //       inside the chunk -- that's almost certainly a font name. Get
        //       its actual buffer position. Then look at the id_table value
        //       (which IS relocated). The first font's name_off there equals
        //       (originalNameOff + fileOffset). originalNameOff = nameAbs - ncflPos
        //       (in the un-relocated file). So fileOffset = storedNameOff - originalNameOff
        //       = storedNameOff - (nameAbs - ncflPos). That's our delta.
        //       But we can't read the id_table either without knowing delta,
        //       because the id_table_OFF field at body+0x10 has been relocated
        //       too. So we scan EVERY u32 in the chunk for one that, when
        //       interpreted as (storedOff - delta), points to nameAbs.
        //   (3) Final fallback: delta=0.
        //
        // Returns the chosen delta, and writes a one-line diagnostic into
        // `diagInfo` so the user can see what the parser inferred.
        private static int DetectDelta(byte[] buf, int ncflPos, out string diagInfo)
        {
            diagInfo = "delta=0 (default)";
            try
            {
                // ---- (1) Probe delta=0 ----
                int probed = ProbeDelta(buf, ncflPos, 0);
                if (probed >= 0)
                {
                    diagInfo = "delta=0 (probe ok, char_count=" + probed + ")";
                    return 0;
                }

                // ---- (2) Anchor-scan ----
                // Find a candidate name string. Skip the chunk header but
                // otherwise scan for any ASCII run -- font names are the
                // first printable strings in the chunk.
                int searchStart = ncflPos + 8;
                int nameAbs = FindAnyAsciiCString(buf, searchStart, buf.Length, 3);
                if (nameAbs < 0)
                {
                    diagInfo = "no anchor string found, delta=0";
                    return 0;
                }

                // The name's stored (relocated) offset would be:
                //   storedNameOff = (nameAbs - ncflPos) + delta
                // i.e. delta = storedNameOff - (nameAbs - ncflPos)
                //
                // We don't know storedNameOff directly, but the id_table's
                // first entry holds it. We don't know exactly where the
                // id_table is either (its `id_table_off` field is itself
                // relocated). So we scan every u32-aligned value between the
                // body and the start of the first string -- one of them IS
                // the relocated name_off in the id_table -- and validate
                // each candidate delta by probing the rest of the structure.
                int origNameRel = nameAbs - ncflPos;
                int bestDelta = 0;
                int bestProbe = -1;
                int scanEnd = nameAbs - 4;
                for (int p = ncflPos + 8; p < scanEnd; p += 4)
                {
                    if (p + 4 > buf.Length) break;
                    int v = BitConverter.ToInt32(buf, p);
                    int candidateDelta = v - origNameRel;
                    // Sanity-bound the candidate: archive offsets are typically
                    // positive and not absurdly large. Reject obviously wrong
                    // values to avoid false-positive matches.
                    if (candidateDelta < 0 || candidateDelta > 0x10000000) continue;

                    int charCount = ProbeDelta(buf, ncflPos, candidateDelta);
                    if (charCount > bestProbe)
                    {
                        bestProbe = charCount;
                        bestDelta = candidateDelta;
                    }
                }

                if (bestProbe > 0)
                {
                    diagInfo = "delta=0x" + bestDelta.ToString("X")
                             + " (anchored on string at 0x" + nameAbs.ToString("X")
                             + ", char_count=" + bestProbe + ")";
                    return bestDelta;
                }

                diagInfo = "anchor at 0x" + nameAbs.ToString("X")
                         + " but no candidate delta validated, delta=0";
                return 0;
            }
            catch (Exception ex)
            {
                diagInfo = "exception in DetectDelta: " + ex.Message + ", delta=0";
                return 0;
            }
        }

        // Try a candidate delta. Returns char_count of the first font if the
        // (table_off -> first font entry -> map data) chain all reads as
        // plausible bytes; returns -1 if anything looks wrong. This is how we
        // validate a guessed delta without trusting it blindly.
        private static int ProbeDelta(byte[] buf, int ncflPos, int delta)
        {
            int body = ncflPos + 8;
            if (body + 0x14 > buf.Length) return -1;

            int fontCount = BitConverter.ToInt32(buf, body + 0x08);
            int tblOff = BitConverter.ToInt32(buf, body + 0x0C);
            if (fontCount <= 0 || fontCount > 256) return -1;
            if (tblOff <= 0) return -1;

            int tblAbs = (ncflPos + tblOff) - delta;
            if (tblAbs < 0 || tblAbs + 8 > buf.Length) return -1;

            int charCount = BitConverter.ToInt32(buf, tblAbs + 0);
            int mapOff = BitConverter.ToInt32(buf, tblAbs + 4);
            if (charCount <= 0 || charCount > 4096) return -1;
            if (mapOff <= 0) return -1;

            int mapAbs = (ncflPos + mapOff) - delta;
            if (mapAbs < 0 || mapAbs + charCount * 8 > buf.Length) return -1;

            // Sanity check: at least the first entry's char code should be
            // plausibly small (a Unicode codepoint or single byte, not a
            // wild pointer-shaped value).
            uint firstChar = BitConverter.ToUInt32(buf, mapAbs);
            if (firstChar > 0x10FFFF) return -1;

            return charCount;
        }

        // ---- Parser core -----------------------------------------------------
        private byte[] data;
        private int gBase;
        private int delta;

        private string ResolveString(int storedOffset)
        {
            if (storedOffset == 0) return "";
            int abs = (gBase + storedOffset) - delta;
            if (abs < 0 || abs >= data.Length) return "";
            int end = abs;
            while (end < data.Length && data[end] != 0) end++;
            int len = end - abs;
            if (len <= 0 || len > 1024) return "";
            try { return Encoding.ASCII.GetString(data, abs, len); }
            catch { return Encoding.GetEncoding("shift-jis").GetString(data, abs, len); }
        }

        private int ResolveOffset(int storedOffset)
        {
            if (storedOffset == 0) return 0;
            int abs = (gBase + storedOffset) - delta;
            if (abs < 0 || abs >= data.Length) return -1;
            return abs;
        }

        private uint U32(int absPos)
        {
            if (absPos < 0 || absPos + 4 > data.Length) return 0;
            return BitConverter.ToUInt32(data, absPos);
        }

        // Parse-time diagnostics surfaced to the viewer's "Parse info" row
        // so failures can be diagnosed without attaching a debugger.
        private string _parseDiag = "";

        private void Parse(byte[] buf, int ncflPos, int relocationDelta)
        {
            data = buf;
            gBase = ncflPos;
            delta = relocationDelta;

            int body = ncflPos + 8;
            if (body + 0x14 > buf.Length)
            {
                _parseDiag = "buffer too small for body";
                return;
            }

            int fontCount = (int)U32(body + 0x08);
            int tblOff = (int)U32(body + 0x0C);
            int idTblOff = (int)U32(body + 0x10);

            _parseDiag = $"fontCount={fontCount} tblOff=0x{tblOff:X} idTblOff=0x{idTblOff:X}";

            if (fontCount < 0 || fontCount > 256)
            {
                ParseError = "Implausible font count " + fontCount;
                return;
            }

            // Read the id table first so we can label fonts by name as we
            // walk the data table.
            var names = new List<string>();
            var nameOffsets = new List<int>();
            var fontIndices = new List<int>();
            if (idTblOff != 0)
            {
                int tblAbs = ResolveOffset(idTblOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < fontCount; i++)
                    {
                        int e = tblAbs + i * 8;
                        if (e + 8 > data.Length) break;
                        int nmOff = (int)U32(e);
                        int idx = (int)U32(e + 4);
                        nameOffsets.Add(nmOff);
                        fontIndices.Add(idx);
                        names.Add(nmOff != 0 ? ResolveString(nmOff) : "");
                    }
                }
            }

            if (tblOff == 0) return;
            int dataTblAbs = ResolveOffset(tblOff);
            if (dataTblAbs <= 0) return;

            for (int i = 0; i < fontCount; i++)
            {
                int e = dataTblAbs + i * 8;
                if (e + 8 > data.Length) break;
                int charCount = (int)U32(e + 0);
                int mapOff = (int)U32(e + 4);

                var font = new XncfFont
                {
                    Name = i < names.Count ? names[i] : ("font_" + i),
                    Index = i < fontIndices.Count ? fontIndices[i] : i,
                    CharacterCount = charCount,
                };

                if (charCount > 0 && charCount <= 4096 && mapOff != 0)
                {
                    int mapAbs = ResolveOffset(mapOff);
                    if (mapAbs > 0)
                    {
                        for (int j = 0; j < charCount; j++)
                        {
                            int g = mapAbs + j * 8;
                            if (g + 8 > data.Length) break;
                            uint charCode = U32(g + 0);
                            uint subImg = U32(g + 4);

                            // Build glyph with raw setters (avoids triggering
                            // writes back into RawBytes during the parse).
                            var glyph = new XncfFontGlyph();
                            glyph.SetCharCodeRaw(charCode);
                            glyph.SetSubImageIndexRaw((int)subImg);

                            // Wire up edit support. _glyphPos is the toParse
                            // position of this 8-byte entry; the setters
                            // subtract _ownerOffset to translate that back to
                            // a RawBytes position.
                            glyph._owner = this;
                            glyph._glyphPos = g;
                            glyph._ownerOffset = _ownerOffset;
                            font.Glyphs.Add(glyph);
                        }
                    }
                }

                Fonts.Add(font);
            }
        }
    }

    // ===========================================================================
    // Data model classes
    // ===========================================================================

    public class XncfFont
    {
        public string Name { get; set; }
        public int Index { get; set; }
        public int CharacterCount { get; set; }
        public List<XncfFontGlyph> Glyphs { get; } = new List<XncfFontGlyph>();

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name)
                ? ("font_" + Index)
                : (Name + "  (" + Glyphs.Count + " glyphs)");
        }
    }

    public class XncfFontGlyph
    {
        // Edit-support wiring. _glyphPos lives in the parse buffer (toParse);
        // RawBytes positions are (_glyphPos - _ownerOffset).
        internal XncfFile _owner;
        internal int _glyphPos;
        internal int _ownerOffset;

        private uint _charCode;
        private int _subImageIndex;

        public uint CharCode
        {
            get { return _charCode; }
            set
            {
                _charCode = value;
                if (_owner != null && _glyphPos >= 0)
                    _owner.WriteUInt32(_glyphPos + 0 - _ownerOffset, value);
            }
        }

        public int SubImageIndex
        {
            get { return _subImageIndex; }
            set
            {
                _subImageIndex = value;
                if (_owner != null && _glyphPos >= 0)
                    _owner.WriteInt32(_glyphPos + 4 - _ownerOffset, value);
            }
        }

        // Convenience: present the char code as a printable character if it
        // falls in the basic ASCII range, otherwise show \xNN.
        public string CharDisplay
        {
            get
            {
                if (_charCode >= 0x20 && _charCode < 0x7F)
                    return ((char)_charCode).ToString();
                return "\\x" + _charCode.ToString("X2");
            }
        }

        // Internal raw setters used by the parser (avoid writing to RawBytes
        // since we're reading the values FROM the bytes).
        internal void SetCharCodeRaw(uint v) { _charCode = v; }
        internal void SetSubImageIndexRaw(int v) { _subImageIndex = v; }

        public override string ToString()
        {
            return $"'{CharDisplay}'  (0x{_charCode:X2})  -> subimage {_subImageIndex}";
        }
    }
}