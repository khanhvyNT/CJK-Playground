using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CJKPlayground.Services
{
    public class TableRecord
    {
        public string Tag { get; set; }
        public uint Checksum { get; set; }
        public uint Offset { get; set; }
        public uint Length { get; set; }
    }

    public class FontHeaderInfo
    {
        public ushort UnitsPerEm { get; set; }
        public short IndexToLocFormat { get; set; }
        public short XMin { get; set; }
        public short YMin { get; set; }
        public short XMax { get; set; }
        public short YMax { get; set; }

        public short Ascender { get; set; }
        public short Descender { get; set; }
        public short LineGap { get; set; }
        public ushort AdvanceWidthMax { get; set; }
        public ushort NumOfLongHorMetrics { get; set; }
        public ushort NumGlyphs { get; set; }

        public string FamilyName { get; set; }
        public string SubfamilyName { get; set; }
        public string FullName { get; set; }
        public string Version { get; set; }
        public string Copyright { get; set; }
    }

    public class RawGlyphOutline
    {
        public int GlyphIndex { get; set; }
        public short NumberOfContours { get; set; }
        public short XMin { get; set; }
        public short YMin { get; set; }
        public short XMax { get; set; }
        public short YMax { get; set; }
        public ushort AdvanceWidth { get; set; }
        public short LeftSideBearing { get; set; }
        public List<List<OutlinePoint>> Contours { get; set; }

        public RawGlyphOutline()
        {
            Contours = new List<List<OutlinePoint>>();
        }
    }

    public struct OutlinePoint
    {
        public int X;
        public int Y;
        public bool OnCurve;

        public OutlinePoint(int x, int y, bool onCurve)
        {
            X = x;
            Y = y;
            OnCurve = onCurve;
        }
    }

    public class OpenTypeBinaryReader : IDisposable
    {
        private Stream _stream;
        private readonly bool _ownsStream;
        private readonly Dictionary<string, TableRecord> _tables = new Dictionary<string, TableRecord>(StringComparer.Ordinal);
        private readonly Dictionary<int, ushort> _cmap = new Dictionary<int, ushort>();
        private readonly HashSet<int> _supportedChars = new HashSet<int>();
        private uint[] _locaOffsets;
        private uint _glyfTableOffset;
        private uint _hmtxTableOffset;

        public FontHeaderInfo Header { get; private set; }

        public int SupportedCharacterCount
        {
            get { return _supportedChars.Count; }
        }

        public OpenTypeBinaryReader(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Không tìm thấy tệp font: " + filePath);

            // Đọc toàn bộ byte vào MemoryStream để giải phóng file handle trên đĩa ngay lập tức,
            // tránh triệt để lỗi 'The process cannot access the file because it is being used by another process'.
            byte[] fontData = File.ReadAllBytes(filePath);
            _stream = new MemoryStream(fontData, false);
            _ownsStream = true;
            Header = new FontHeaderInfo();
            Parse();
        }

        public OpenTypeBinaryReader(byte[] fontData)
        {
            if (fontData == null || fontData.Length < 12)
                throw new ArgumentException("Dữ liệu font không hợp lệ hoặc quá ngắn.");

            _stream = new MemoryStream(fontData, false);
            _ownsStream = true;
            Header = new FontHeaderInfo();
            Parse();
        }

        public bool IsCharacterSupported(int codePoint)
        {
            return _supportedChars.Contains(codePoint);
        }

        public ushort GetGlyphIndex(int codePoint)
        {
            ushort gid;
            if (_cmap.TryGetValue(codePoint, out gid))
                return gid;
            return 0;
        }

        public HashSet<int> GetSupportedCodePoints()
        {
            return new HashSet<int>(_supportedChars);
        }

        #region Big-Endian Stream Helpers
        private ushort ReadUInt16BE()
        {
            int b1 = _stream.ReadByte();
            int b2 = _stream.ReadByte();
            if (b1 < 0 || b2 < 0) throw new EndOfStreamException();
            return (ushort)((b1 << 8) | b2);
        }

        private short ReadInt16BE()
        {
            return (short)ReadUInt16BE();
        }

        private uint ReadUInt32BE()
        {
            int b1 = _stream.ReadByte();
            int b2 = _stream.ReadByte();
            int b3 = _stream.ReadByte();
            int b4 = _stream.ReadByte();
            if (b1 < 0 || b2 < 0 || b3 < 0 || b4 < 0) throw new EndOfStreamException();
            return (uint)((b1 << 24) | (b2 << 16) | (b3 << 8) | b4);
        }

        private int ReadInt32BE()
        {
            return (int)ReadUInt32BE();
        }

        private string ReadTag()
        {
            byte[] bytes = new byte[4];
            int read = _stream.Read(bytes, 0, 4);
            if (read < 4) throw new EndOfStreamException();
            return Encoding.ASCII.GetString(bytes);
        }
        #endregion

        private void Parse()
        {
            _stream.Position = 0;
            uint magic = ReadUInt32BE();

            uint offsetToTableDir = 0;
            // Check if TrueType Collection (ttcf)
            if (magic == 0x74746366) // "ttcf"
            {
                uint version = ReadUInt32BE();
                uint numFonts = ReadUInt32BE();
                if (numFonts > 0)
                {
                    // Read first font face offset
                    offsetToTableDir = ReadUInt32BE();
                }
            }

            _stream.Position = offsetToTableDir;
            uint sfntVersion = ReadUInt32BE();
            ushort numTables = ReadUInt16BE();
            ushort searchRange = ReadUInt16BE();
            ushort entrySelector = ReadUInt16BE();
            ushort rangeShift = ReadUInt16BE();

            // Read table directory
            for (int i = 0; i < numTables; i++)
            {
                var rec = new TableRecord
                {
                    Tag = ReadTag(),
                    Checksum = ReadUInt32BE(),
                    Offset = ReadUInt32BE(),
                    Length = ReadUInt32BE()
                };
                _tables[rec.Tag] = rec;
            }

            // Parse head
            ParseHeadTable();

            // Parse hhea & maxp
            ParseHheaTable();
            ParseMaxpTable();

            // Parse name
            ParseNameTable();

            // Parse cmap (Format 4 & 12)
            ParseCmapTable();

            // Store glyf & hmtx offsets
            if (_tables.ContainsKey("glyf"))
                _glyfTableOffset = _tables["glyf"].Offset;
            if (_tables.ContainsKey("hmtx"))
                _hmtxTableOffset = _tables["hmtx"].Offset;

            // Parse loca
            ParseLocaTable();
        }

        private void ParseHeadTable()
        {
            TableRecord rec;
            if (!_tables.TryGetValue("head", out rec)) return;

            _stream.Position = rec.Offset;
            _stream.Seek(12, SeekOrigin.Current); // skip major(2), minor(2), fontRevision(4), checkSumAdj(4)
            uint magicNumber = ReadUInt32BE();
            if (magicNumber != 0x5F0F3CF5) return;

            ushort flags = ReadUInt16BE();
            Header.UnitsPerEm = ReadUInt16BE();
            _stream.Seek(16, SeekOrigin.Current); // skip created(8), modified(8)

            Header.XMin = ReadInt16BE();
            Header.YMin = ReadInt16BE();
            Header.XMax = ReadInt16BE();
            Header.YMax = ReadInt16BE();

            _stream.Seek(6, SeekOrigin.Current); // skip macStyle(2), lowestRecPPEM(2), fontDirectionHint(2)
            Header.IndexToLocFormat = ReadInt16BE();
        }

        private void ParseHheaTable()
        {
            TableRecord rec;
            if (!_tables.TryGetValue("hhea", out rec)) return;

            _stream.Position = rec.Offset + 4; // skip version
            Header.Ascender = ReadInt16BE();
            Header.Descender = ReadInt16BE();
            Header.LineGap = ReadInt16BE();
            Header.AdvanceWidthMax = ReadUInt16BE();
            _stream.Seek(22, SeekOrigin.Current);
            Header.NumOfLongHorMetrics = ReadUInt16BE();
        }

        private void ParseMaxpTable()
        {
            TableRecord rec;
            if (!_tables.TryGetValue("maxp", out rec)) return;

            _stream.Position = rec.Offset + 4; // skip version
            Header.NumGlyphs = ReadUInt16BE();
        }

        private void ParseNameTable()
        {
            TableRecord rec;
            if (!_tables.TryGetValue("name", out rec)) return;

            _stream.Position = rec.Offset;
            ushort format = ReadUInt16BE();
            ushort count = ReadUInt16BE();
            ushort stringOffset = ReadUInt16BE();
            long storageOffset = rec.Offset + stringOffset;

            for (int i = 0; i < count; i++)
            {
                ushort platformID = ReadUInt16BE();
                ushort encodingID = ReadUInt16BE();
                ushort languageID = ReadUInt16BE();
                ushort nameID = ReadUInt16BE();
                ushort length = ReadUInt16BE();
                ushort offset = ReadUInt16BE();

                long curPos = _stream.Position;
                _stream.Position = storageOffset + offset;
                byte[] raw = new byte[length];
                _stream.Read(raw, 0, length);
                _stream.Position = curPos;

                string str = "";
                if (platformID == 3 || platformID == 0) // Windows Unicode UTF-16BE
                {
                    str = Encoding.BigEndianUnicode.GetString(raw).Trim('\0');
                }
                else
                {
                    str = Encoding.ASCII.GetString(raw).Trim('\0');
                }

                if (string.IsNullOrEmpty(str)) continue;

                switch (nameID)
                {
                    case 0: if (string.IsNullOrEmpty(Header.Copyright)) Header.Copyright = str; break;
                    case 1: if (string.IsNullOrEmpty(Header.FamilyName)) Header.FamilyName = str; break;
                    case 2: if (string.IsNullOrEmpty(Header.SubfamilyName)) Header.SubfamilyName = str; break;
                    case 4: if (string.IsNullOrEmpty(Header.FullName)) Header.FullName = str; break;
                    case 5: if (string.IsNullOrEmpty(Header.Version)) Header.Version = str; break;
                }
            }
        }

        private void ParseCmapTable()
        {
            TableRecord rec;
            if (!_tables.TryGetValue("cmap", out rec)) return;

            long cmapStart = rec.Offset;
            _stream.Position = cmapStart;
            ushort version = ReadUInt16BE();
            ushort numTables = ReadUInt16BE();

            var subtables = new List<Tuple<ushort, ushort, uint>>();
            for (int i = 0; i < numTables; i++)
            {
                ushort platformID = ReadUInt16BE();
                ushort encodingID = ReadUInt16BE();
                uint subtableOffset = ReadUInt32BE();
                subtables.Add(new Tuple<ushort, ushort, uint>(platformID, encodingID, subtableOffset));
            }

            // Prefer Format 12 (covers 32-bit Chữ Nôm Ext B+), then Format 4
            Tuple<ushort, ushort, uint> fmt12 = null;
            Tuple<ushort, ushort, uint> fmt4 = null;

            foreach (var st in subtables)
            {
                _stream.Position = cmapStart + st.Item3;
                ushort format = ReadUInt16BE();
                if (format == 12 && fmt12 == null) fmt12 = st;
                if (format == 4 && fmt4 == null) fmt4 = st;
            }

            // Always parse Format 4 first (for BMP)
            if (fmt4 != null)
            {
                ParseCmapFormat4(cmapStart + fmt4.Item3);
            }

            // Then parse Format 12 (to override/expand for Plane 2 Chữ Nôm)
            if (fmt12 != null)
            {
                ParseCmapFormat12(cmapStart + fmt12.Item3);
            }
        }

        private void ParseCmapFormat4(long subtableStart)
        {
            _stream.Position = subtableStart;
            ushort format = ReadUInt16BE();
            ushort length = ReadUInt16BE();
            ushort language = ReadUInt16BE();
            ushort segCountX2 = ReadUInt16BE();
            int segCount = segCountX2 / 2;
            _stream.Seek(6, SeekOrigin.Current); // skip searchRange, entrySelector, rangeShift

            ushort[] endCodes = new ushort[segCount];
            for (int i = 0; i < segCount; i++) endCodes[i] = ReadUInt16BE();

            ushort reservedPad = ReadUInt16BE();

            ushort[] startCodes = new ushort[segCount];
            for (int i = 0; i < segCount; i++) startCodes[i] = ReadUInt16BE();

            short[] idDeltas = new short[segCount];
            for (int i = 0; i < segCount; i++) idDeltas[i] = ReadInt16BE();

            long idRangeOffsetsPos = _stream.Position;
            ushort[] idRangeOffsets = new ushort[segCount];
            for (int i = 0; i < segCount; i++) idRangeOffsets[i] = ReadUInt16BE();

            for (int i = 0; i < segCount; i++)
            {
                ushort start = startCodes[i];
                ushort end = endCodes[i];
                short delta = idDeltas[i];
                ushort rangeOffset = idRangeOffsets[i];

                if (start == 0xFFFF && end == 0xFFFF) break;

                for (int c = start; c <= end; c++)
                {
                    ushort gid = 0;
                    if (rangeOffset == 0)
                    {
                        gid = (ushort)((c + delta) & 0xFFFF);
                    }
                    else
                    {
                        long glyphOffset = idRangeOffsetsPos + (i * 2) + rangeOffset + ((c - start) * 2);
                        _stream.Position = glyphOffset;
                        gid = ReadUInt16BE();
                        if (gid != 0)
                        {
                            gid = (ushort)((gid + delta) & 0xFFFF);
                        }
                    }

                    if (gid > 0)
                    {
                        _cmap[c] = gid;
                        _supportedChars.Add(c);
                    }
                }
            }
        }

        private void ParseCmapFormat12(long subtableStart)
        {
            _stream.Position = subtableStart;
            ushort format = ReadUInt16BE();
            ushort reserved = ReadUInt16BE();
            uint length = ReadUInt32BE();
            uint language = ReadUInt32BE();
            uint numGroups = ReadUInt32BE();

            for (uint i = 0; i < numGroups; i++)
            {
                uint startCharCode = ReadUInt32BE();
                uint endCharCode = ReadUInt32BE();
                uint startGlyphID = ReadUInt32BE();

                for (uint c = startCharCode; c <= endCharCode; c++)
                {
                    uint gid = startGlyphID + (c - startCharCode);
                    if (gid > 0 && gid <= 0xFFFF)
                    {
                        int cp = (int)c;
                        _cmap[cp] = (ushort)gid;
                        _supportedChars.Add(cp);
                    }
                }
            }
        }

        private void ParseLocaTable()
        {
            TableRecord rec;
            if (!_tables.TryGetValue("loca", out rec) || Header.NumGlyphs == 0) return;

            _stream.Position = rec.Offset;
            int count = Header.NumGlyphs + 1;
            _locaOffsets = new uint[count];

            if (Header.IndexToLocFormat == 0) // Short 16-bit offsets (divided by 2)
            {
                for (int i = 0; i < count; i++)
                {
                    _locaOffsets[i] = (uint)(ReadUInt16BE() * 2);
                }
            }
            else // Long 32-bit offsets
            {
                for (int i = 0; i < count; i++)
                {
                    _locaOffsets[i] = ReadUInt32BE();
                }
            }
        }

        public RawGlyphOutline GetGlyphOutline(int codePoint)
        {
            ushort gid = GetGlyphIndex(codePoint);
            if (gid == 0 || _locaOffsets == null || gid >= _locaOffsets.Length - 1)
                return null;

            uint startOffset = _locaOffsets[gid];
            uint endOffset = _locaOffsets[gid + 1];

            var glyph = new RawGlyphOutline { GlyphIndex = gid };
            ushort advance;
            short lsb;
            GetHorizontalMetrics(gid, out advance, out lsb);
            glyph.AdvanceWidth = advance;
            glyph.LeftSideBearing = lsb;

            // Empty glyph (like space)
            if (startOffset == endOffset)
                return glyph;

            _stream.Position = _glyfTableOffset + startOffset;
            glyph.NumberOfContours = ReadInt16BE();
            glyph.XMin = ReadInt16BE();
            glyph.YMin = ReadInt16BE();
            glyph.XMax = ReadInt16BE();
            glyph.YMax = ReadInt16BE();

            // Simple glyph
            if (glyph.NumberOfContours > 0)
            {
                ushort[] endPtsOfContours = new ushort[glyph.NumberOfContours];
                for (int i = 0; i < glyph.NumberOfContours; i++)
                    endPtsOfContours[i] = ReadUInt16BE();

                ushort instructionLength = ReadUInt16BE();
                _stream.Seek(instructionLength, SeekOrigin.Current); // skip instructions

                int totalPoints = endPtsOfContours[glyph.NumberOfContours - 1] + 1;
                byte[] flags = new byte[totalPoints];

                // Read flags
                int pIndex = 0;
                while (pIndex < totalPoints)
                {
                    byte f = (byte)_stream.ReadByte();
                    flags[pIndex++] = f;
                    if ((f & 0x08) != 0) // Repeat flag
                    {
                        int repeatCount = _stream.ReadByte();
                        for (int r = 0; r < repeatCount && pIndex < totalPoints; r++)
                            flags[pIndex++] = f;
                    }
                }

                // Read X coordinates
                int[] xCoordinates = new int[totalPoints];
                int currentX = 0;
                for (int i = 0; i < totalPoints; i++)
                {
                    byte f = flags[i];
                    bool xShort = (f & 0x02) != 0;
                    bool xSameOrPos = (f & 0x10) != 0;

                    if (xShort)
                    {
                        int dx = _stream.ReadByte();
                        currentX += xSameOrPos ? dx : -dx;
                    }
                    else if (!xSameOrPos)
                    {
                        currentX += ReadInt16BE();
                    }
                    xCoordinates[i] = currentX;
                }

                // Read Y coordinates
                int[] yCoordinates = new int[totalPoints];
                int currentY = 0;
                for (int i = 0; i < totalPoints; i++)
                {
                    byte f = flags[i];
                    bool yShort = (f & 0x04) != 0;
                    bool ySameOrPos = (f & 0x20) != 0;

                    if (yShort)
                    {
                        int dy = _stream.ReadByte();
                        currentY += ySameOrPos ? dy : -dy;
                    }
                    else if (!ySameOrPos)
                    {
                        currentY += ReadInt16BE();
                    }
                    yCoordinates[i] = currentY;
                }

                // Build contours
                int startPt = 0;
                for (int c = 0; c < glyph.NumberOfContours; c++)
                {
                    int endPt = endPtsOfContours[c];
                    var contour = new List<OutlinePoint>();
                    for (int pt = startPt; pt <= endPt; pt++)
                    {
                        bool onCurve = (flags[pt] & 0x01) != 0;
                        contour.Add(new OutlinePoint(xCoordinates[pt], yCoordinates[pt], onCurve));
                    }
                    glyph.Contours.Add(contour);
                    startPt = endPt + 1;
                }
            }

            return glyph;
        }

        public void GetHorizontalMetrics(ushort gid, out ushort advanceWidth, out short leftSideBearing)
        {
            advanceWidth = Header.AdvanceWidthMax;
            leftSideBearing = 0;

            if (_hmtxTableOffset == 0 || Header.NumOfLongHorMetrics == 0) return;

            long pos = _stream.Position;
            if (gid < Header.NumOfLongHorMetrics)
            {
                _stream.Position = _hmtxTableOffset + (gid * 4);
                advanceWidth = ReadUInt16BE();
                leftSideBearing = ReadInt16BE();
            }
            else
            {
                // Advance width repeats from last long metric
                _stream.Position = _hmtxTableOffset + ((Header.NumOfLongHorMetrics - 1) * 4);
                advanceWidth = ReadUInt16BE();
                _stream.Position = _hmtxTableOffset + (Header.NumOfLongHorMetrics * 4) + ((gid - Header.NumOfLongHorMetrics) * 2);
                leftSideBearing = ReadInt16BE();
            }
            _stream.Position = pos;
        }

        public Dictionary<string, TableRecord> GetTables()
        {
            return new Dictionary<string, TableRecord>(_tables);
        }

        public uint[] GetLocaOffsets()
        {
            return _locaOffsets != null ? (uint[])_locaOffsets.Clone() : new uint[0];
        }

        public Dictionary<int, ushort> GetAllCmapMappings()
        {
            return new Dictionary<int, ushort>(_cmap);
        }

        public byte[] ReadTableBytes(string tag)
        {
            TableRecord rec;
            if (!_tables.TryGetValue(tag, out rec)) return null;
            byte[] data = new byte[rec.Length];
            long curPos = _stream.Position;
            _stream.Position = rec.Offset;
            int read = _stream.Read(data, 0, (int)rec.Length);
            _stream.Position = curPos;
            if (read < rec.Length) return null;
            return data;
        }

        public byte[] ReadGlyphBytes(ushort gid)
        {
            if (_locaOffsets == null || gid >= _locaOffsets.Length - 1 || _glyfTableOffset == 0) return null;
            uint start = _locaOffsets[gid];
            uint end = _locaOffsets[gid + 1];
            if (end <= start) return new byte[0];
            uint len = end - start;
            byte[] data = new byte[len];
            long curPos = _stream.Position;
            _stream.Position = _glyfTableOffset + start;
            int read = _stream.Read(data, 0, (int)len);
            _stream.Position = curPos;
            if (read < len) return null;
            return data;
        }

        public void Dispose()
        {
            if (_ownsStream && _stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }
    }
}
