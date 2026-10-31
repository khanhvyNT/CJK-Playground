using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class GlyphInjectionEntry
    {
        public int CodePoint { get; set; }
        public RawGlyphOutline Outline { get; set; }
        public ushort AdvanceWidth { get; set; }
        public short LeftSideBearing { get; set; }

        public GlyphInjectionEntry() { }

        public GlyphInjectionEntry(int codePoint, RawGlyphOutline outline, ushort advanceWidth, short lsb)
        {
            CodePoint = codePoint;
            Outline = outline;
            AdvanceWidth = advanceWidth;
            LeftSideBearing = lsb;
        }
    }

    public static class TrueTypeFontWriter
    {
        public static bool IsTrueTypeFont(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 12) return false;
                    byte[] header = new byte[4];
                    fs.Read(header, 0, 4);
                    uint magic = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
                    return magic == 0x00010000 || magic == 0x74727565; // 0x00010000 or 'true'
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsTrueTypeFont(byte[] fontData)
        {
            if (fontData == null || fontData.Length < 12) return false;
            uint magic = (uint)((fontData[0] << 24) | (fontData[1] << 16) | (fontData[2] << 8) | fontData[3]);
            return magic == 0x00010000 || magic == 0x74727565;
        }

        #region Big-Endian Stream Helpers
        private static void WriteUInt16(Stream s, ushort val)
        {
            s.WriteByte((byte)((val >> 8) & 0xFF));
            s.WriteByte((byte)(val & 0xFF));
        }

        private static void WriteInt16(Stream s, short val)
        {
            WriteUInt16(s, (ushort)val);
        }

        private static void WriteUInt32(Stream s, uint val)
        {
            s.WriteByte((byte)((val >> 24) & 0xFF));
            s.WriteByte((byte)((val >> 16) & 0xFF));
            s.WriteByte((byte)((val >> 8) & 0xFF));
            s.WriteByte((byte)(val & 0xFF));
        }

        private static void WriteInt32(Stream s, int val)
        {
            WriteUInt32(s, (uint)val);
        }
        #endregion

        #region Simple Glyph Serializer
        /// <summary>
        /// Chuyển đổi một RawGlyphOutline thành chuỗi byte của Simple Glyph theo chuẩn OpenType/TrueType.
        /// </summary>
        public static byte[] SerializeSimpleGlyph(RawGlyphOutline outline)
        {
            if (outline == null || outline.Contours == null || outline.Contours.Count == 0)
            {
                return new byte[0]; // Empty glyph (như dấu cách)
            }

            // Thu thập các điểm và chỉ số kết thúc của từng đường bao (endPtsOfContours)
            var allPoints = new List<OutlinePoint>();
            var endPts = new List<ushort>();
            int runningCount = 0;

            for (int i = 0; i < outline.Contours.Count; i++)
            {
                var c = outline.Contours[i];
                if (c == null || c.Count == 0) continue;
                allPoints.AddRange(c);
                runningCount += c.Count;
                endPts.Add((ushort)(runningCount - 1));
            }

            int numContours = endPts.Count;
            int totalPoints = allPoints.Count;

            if (numContours == 0 || totalPoints == 0)
            {
                return new byte[0];
            }

            // Tính Bounding Box thực tế
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < totalPoints; i++)
            {
                int px = allPoints[i].X;
                int py = allPoints[i].Y;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }

            using (var ms = new MemoryStream())
            {
                // 1. Header (10 bytes)
                WriteInt16(ms, (short)numContours);
                WriteInt16(ms, (short)minX);
                WriteInt16(ms, (short)minY);
                WriteInt16(ms, (short)maxX);
                WriteInt16(ms, (short)maxY);

                // 2. endPtsOfContours (numContours * 2 bytes)
                for (int i = 0; i < numContours; i++)
                {
                    WriteUInt16(ms, endPts[i]);
                }

                // 3. instructionLength = 0 (2 bytes)
                WriteUInt16(ms, 0);

                // 4. Flags, X coordinates, Y coordinates
                byte[] flags = new byte[totalPoints];
                for (int i = 0; i < totalPoints; i++)
                {
                    int prevX = (i == 0) ? 0 : allPoints[i - 1].X;
                    int prevY = (i == 0) ? 0 : allPoints[i - 1].Y;
                    int dx = allPoints[i].X - prevX;
                    int dy = allPoints[i].Y - prevY;

                    byte f = (byte)(allPoints[i].OnCurve ? 0x01 : 0x00);

                    // X coordinate flags
                    if (dx == 0)
                    {
                        f |= 0x10; // X_IS_SAME_OR_POSITIVE_X_SHORT_VECTOR (no bytes stored)
                    }
                    else if (dx > 0 && dx <= 255)
                    {
                        f |= 0x02; // X_SHORT_VECTOR
                        f |= 0x10; // positive
                    }
                    else if (dx < 0 && dx >= -255)
                    {
                        f |= 0x02; // X_SHORT_VECTOR (negative: bit 4 is 0)
                    }

                    // Y coordinate flags
                    if (dy == 0)
                    {
                        f |= 0x20; // Y_IS_SAME_OR_POSITIVE_Y_SHORT_VECTOR (no bytes stored)
                    }
                    else if (dy > 0 && dy <= 255)
                    {
                        f |= 0x04; // Y_SHORT_VECTOR
                        f |= 0x20; // positive
                    }
                    else if (dy < 0 && dy >= -255)
                    {
                        f |= 0x04; // Y_SHORT_VECTOR (negative: bit 5 is 0)
                    }

                    flags[i] = f;
                }

                // Ghi mảng flags
                for (int i = 0; i < totalPoints; i++)
                {
                    ms.WriteByte(flags[i]);
                }

                // Ghi mảng tọa độ X
                for (int i = 0; i < totalPoints; i++)
                {
                    int prevX = (i == 0) ? 0 : allPoints[i - 1].X;
                    int dx = allPoints[i].X - prevX;
                    if (dx == 0)
                    {
                        // Không cần byte nào
                    }
                    else if (dx > 0 && dx <= 255)
                    {
                        ms.WriteByte((byte)dx);
                    }
                    else if (dx < 0 && dx >= -255)
                    {
                        ms.WriteByte((byte)(-dx));
                    }
                    else
                    {
                        WriteInt16(ms, (short)dx);
                    }
                }

                // Ghi mảng tọa độ Y
                for (int i = 0; i < totalPoints; i++)
                {
                    int prevY = (i == 0) ? 0 : allPoints[i - 1].Y;
                    int dy = allPoints[i].Y - prevY;
                    if (dy == 0)
                    {
                        // Không cần byte nào
                    }
                    else if (dy > 0 && dy <= 255)
                    {
                        ms.WriteByte((byte)dy);
                    }
                    else if (dy < 0 && dy >= -255)
                    {
                        ms.WriteByte((byte)(-dy));
                    }
                    else
                    {
                        WriteInt16(ms, (short)dy);
                    }
                }

                return ms.ToArray();
            }
        }
        #endregion

        #region Cmap Subtable Builders
        private class Cmap4Segment
        {
            public ushort StartCode;
            public ushort EndCode;
            public short IdDelta;
        }

        public static byte[] BuildFormat4Subtable(Dictionary<int, ushort> cmapMappings)
        {
            // Lọc ra các ký tự thuộc BMP (<= 0xFFFF)
            var bmpList = new List<KeyValuePair<int, ushort>>();
            foreach (var kvp in cmapMappings)
            {
                if (kvp.Key >= 0 && kvp.Key <= 0xFFFF && kvp.Value > 0)
                {
                    bmpList.Add(kvp);
                }
            }

            bmpList.Sort(new Comparison<KeyValuePair<int, ushort>>(delegate(KeyValuePair<int, ushort> a, KeyValuePair<int, ushort> b)
            {
                return a.Key.CompareTo(b.Key);
            }));

            // Phân mảnh các đoạn liên tục có cùng idDelta
            var segments = new List<Cmap4Segment>();
            Cmap4Segment currentSeg = null;

            for (int i = 0; i < bmpList.Count; i++)
            {
                int code = bmpList[i].Key;
                ushort gid = bmpList[i].Value;
                if (code >= 0xFFFF) continue; // Dành riêng cho phân đoạn kết thúc 0xFFFF

                short delta = (short)((gid - code) & 0xFFFF);

                if (currentSeg != null &&
                    code == currentSeg.EndCode + 1 &&
                    delta == currentSeg.IdDelta)
                {
                    currentSeg.EndCode = (ushort)code;
                }
                else
                {
                    currentSeg = new Cmap4Segment
                    {
                        StartCode = (ushort)code,
                        EndCode = (ushort)code,
                        IdDelta = delta
                    };
                    segments.Add(currentSeg);
                }
            }

            // Luôn thêm phân đoạn kết thúc 0xFFFF..0xFFFF
            ushort lastGid = 0;
            cmapMappings.TryGetValue(0xFFFF, out lastGid);
            short lastDelta = lastGid > 0 ? (short)((lastGid - 0xFFFF) & 0xFFFF) : (short)1;

            segments.Add(new Cmap4Segment
            {
                StartCode = 0xFFFF,
                EndCode = 0xFFFF,
                IdDelta = lastDelta
            });

            int segCount = segments.Count;
            ushort segCountX2 = (ushort)(segCount * 2);

            int powerOf2 = 1;
            int entrySelector = 0;
            while ((powerOf2 * 2) <= segCount)
            {
                powerOf2 *= 2;
                entrySelector++;
            }
            ushort searchRange = (ushort)(2 * powerOf2);
            ushort rangeShift = (ushort)(segCountX2 - searchRange);

            ushort length = (ushort)(16 + (segCount * 8));

            using (var ms = new MemoryStream(length))
            {
                WriteUInt16(ms, 4); // format = 4
                WriteUInt16(ms, length);
                WriteUInt16(ms, 0); // language = 0
                WriteUInt16(ms, segCountX2);
                WriteUInt16(ms, searchRange);
                WriteUInt16(ms, (ushort)entrySelector);
                WriteUInt16(ms, rangeShift);

                // endCode[segCount]
                for (int i = 0; i < segCount; i++)
                {
                    WriteUInt16(ms, segments[i].EndCode);
                }

                WriteUInt16(ms, 0); // reservedPad

                // startCode[segCount]
                for (int i = 0; i < segCount; i++)
                {
                    WriteUInt16(ms, segments[i].StartCode);
                }

                // idDelta[segCount]
                for (int i = 0; i < segCount; i++)
                {
                    WriteInt16(ms, segments[i].IdDelta);
                }

                // idRangeOffset[segCount] - toàn bộ bằng 0 vì dùng idDelta thuần túy
                for (int i = 0; i < segCount; i++)
                {
                    WriteUInt16(ms, 0);
                }

                return ms.ToArray();
            }
        }

        private class Cmap12Group
        {
            public uint StartCharCode;
            public uint EndCharCode;
            public uint StartGlyphID;
        }

        public static byte[] BuildFormat12Subtable(Dictionary<int, ushort> cmapMappings)
        {
            var list = new List<KeyValuePair<int, ushort>>();
            foreach (var kvp in cmapMappings)
            {
                if (kvp.Key >= 0 && kvp.Key <= 0x10FFFF && kvp.Value > 0)
                {
                    list.Add(kvp);
                }
            }

            list.Sort(new Comparison<KeyValuePair<int, ushort>>(delegate(KeyValuePair<int, ushort> a, KeyValuePair<int, ushort> b)
            {
                return a.Key.CompareTo(b.Key);
            }));

            var groups = new List<Cmap12Group>();
            Cmap12Group currentGroup = null;

            for (int i = 0; i < list.Count; i++)
            {
                uint c = (uint)list[i].Key;
                uint gid = list[i].Value;

                if (currentGroup != null &&
                    c == currentGroup.EndCharCode + 1 &&
                    gid == currentGroup.StartGlyphID + (c - currentGroup.StartCharCode))
                {
                    currentGroup.EndCharCode = c;
                }
                else
                {
                    currentGroup = new Cmap12Group
                    {
                        StartCharCode = c,
                        EndCharCode = c,
                        StartGlyphID = gid
                    };
                    groups.Add(currentGroup);
                }
            }

            uint numGroups = (uint)groups.Count;
            uint length = 16 + (numGroups * 12);

            using (var ms = new MemoryStream((int)length))
            {
                WriteUInt16(ms, 12); // format
                WriteUInt16(ms, 0);  // reserved
                WriteUInt32(ms, length);
                WriteUInt32(ms, 0);  // language
                WriteUInt32(ms, numGroups);

                for (int i = 0; i < groups.Count; i++)
                {
                    WriteUInt32(ms, groups[i].StartCharCode);
                    WriteUInt32(ms, groups[i].EndCharCode);
                    WriteUInt32(ms, groups[i].StartGlyphID);
                }

                return ms.ToArray();
            }
        }

        public static byte[] BuildCmapTable(Dictionary<int, ushort> cmapMappings)
        {
            byte[] subtable4 = BuildFormat4Subtable(cmapMappings);
            byte[] subtable12 = BuildFormat12Subtable(cmapMappings);

            // Bố cục bảng cmap:
            // Header: version (2), numTables (2) = 4 bytes
            // 4 Encoding records (mỗi record 8 bytes: platformID(2), encodingID(2), subtableOffset(4)) = 32 bytes
            // Tổng header = 36 bytes.
            uint offsetFmt4 = 36;
            uint fmt4LenPadded = (uint)((subtable4.Length + 3) & ~3);
            uint offsetFmt12 = offsetFmt4 + fmt4LenPadded;

            uint totalLen = offsetFmt12 + (uint)subtable12.Length;

            using (var ms = new MemoryStream((int)totalLen))
            {
                WriteUInt16(ms, 0); // version
                WriteUInt16(ms, 4); // 4 encoding tables

                // 1. Unicode BMP (0, 3) -> Format 4
                WriteUInt16(ms, 0);
                WriteUInt16(ms, 3);
                WriteUInt32(ms, offsetFmt4);

                // 2. Unicode Full (0, 4) -> Format 12
                WriteUInt16(ms, 0);
                WriteUInt16(ms, 4);
                WriteUInt32(ms, offsetFmt12);

                // 3. Windows BMP (3, 1) -> Format 4
                WriteUInt16(ms, 3);
                WriteUInt16(ms, 1);
                WriteUInt32(ms, offsetFmt4);

                // 4. Windows Full (3, 10) -> Format 12
                WriteUInt16(ms, 3);
                WriteUInt16(ms, 10);
                WriteUInt32(ms, offsetFmt12);

                // Ghi Subtable 4
                ms.Write(subtable4, 0, subtable4.Length);
                while (ms.Position % 4 != 0)
                {
                    ms.WriteByte(0);
                }

                // Ghi Subtable 12
                ms.Write(subtable12, 0, subtable12.Length);

                return ms.ToArray();
            }
        }
        #endregion

        #region Checksum Calculation
        public static uint CalculateTableChecksum(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            uint sum = 0;
            int len = data.Length;
            int nLongs = (len + 3) / 4;
            int pos = 0;

            for (int i = 0; i < nLongs; i++)
            {
                uint b0 = (pos < len) ? data[pos++] : 0u;
                uint b1 = (pos < len) ? data[pos++] : 0u;
                uint b2 = (pos < len) ? data[pos++] : 0u;
                uint b3 = (pos < len) ? data[pos++] : 0u;
                sum += (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
            }

            return sum;
        }
        #endregion

        #region Font Injection Engine
        /// <summary>
        /// Nạp danh sách các glyph mới/chỉnh sửa vào tệp phông TrueType nhị phân.
        /// </summary>
        public static byte[] InjectGlyphs(byte[] originalFontData, IEnumerable<GlyphInjectionEntry> entries)
        {
            if (originalFontData == null || originalFontData.Length < 12)
                throw new ArgumentException("Dữ liệu font không hợp lệ hoặc quá ngắn.");

            if (!IsTrueTypeFont(originalFontData))
                throw new InvalidOperationException("Chỉ hỗ trợ nạp ký tự vào font TrueType (.ttf) chuẩn đơn tệp.");

            var entryList = new List<GlyphInjectionEntry>();
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e != null) entryList.Add(e);
                }
            }

            if (entryList.Count == 0)
            {
                return (byte[])originalFontData.Clone();
            }

            using (var reader = new OpenTypeBinaryReader(originalFontData))
            {
                var tables = reader.GetTables();
                if (!tables.ContainsKey("glyf") || !tables.ContainsKey("head") ||
                    !tables.ContainsKey("hhea") || !tables.ContainsKey("maxp") ||
                    !tables.ContainsKey("loca") || !tables.ContainsKey("hmtx"))
                {
                    throw new InvalidOperationException("Phông chữ thiếu các bảng TrueType cốt lõi (glyf, loca, hmtx, hhea, head, maxp).");
                }

                ushort oldNumGlyphs = reader.Header.NumGlyphs;
                var cmapMappings = reader.GetAllCmapMappings();

                // 1. Thu thập toàn bộ byte của các glyph hiện có
                var allGlyphs = new List<byte[]>(oldNumGlyphs + entryList.Count);
                for (ushort g = 0; g < oldNumGlyphs; g++)
                {
                    byte[] gb = reader.ReadGlyphBytes(g);
                    allGlyphs.Add(gb ?? new byte[0]);
                }

                // 2. Thu thập toàn bộ metrics ngang hiện có
                var advanceWidths = new List<ushort>(oldNumGlyphs + entryList.Count);
                var lsbs = new List<short>(oldNumGlyphs + entryList.Count);
                for (ushort g = 0; g < oldNumGlyphs; g++)
                {
                    ushort adv;
                    short lsb;
                    reader.GetHorizontalMetrics(g, out adv, out lsb);
                    advanceWidths.Add(adv);
                    lsbs.Add(lsb);
                }

                // 3. Tiến hành bơm các glyph mới
                short newFontXMin = reader.Header.XMin;
                short newFontYMin = reader.Header.YMin;
                short newFontXMax = reader.Header.XMax;
                short newFontYMax = reader.Header.YMax;
                ushort maxPoints = 0;
                ushort maxContours = 0;

                for (int ei = 0; ei < entryList.Count; ei++)
                {
                    var entry = entryList[ei];
                    byte[] newGlyphBytes = SerializeSimpleGlyph(entry.Outline);

                    if (entry.Outline != null && entry.Outline.Contours != null && entry.Outline.Contours.Count > 0)
                    {
                        if (entry.Outline.XMin < newFontXMin) newFontXMin = entry.Outline.XMin;
                        if (entry.Outline.YMin < newFontYMin) newFontYMin = entry.Outline.YMin;
                        if (entry.Outline.XMax > newFontXMax) newFontXMax = entry.Outline.XMax;
                        if (entry.Outline.YMax > newFontYMax) newFontYMax = entry.Outline.YMax;

                        ushort pts = 0;
                        for (int ci = 0; ci < entry.Outline.Contours.Count; ci++)
                        {
                            pts += (ushort)entry.Outline.Contours[ci].Count;
                        }
                        if (pts > maxPoints) maxPoints = pts;
                        if (entry.Outline.Contours.Count > maxContours) maxContours = (ushort)entry.Outline.Contours.Count;
                    }

                    ushort existingGid;
                    if (cmapMappings.TryGetValue(entry.CodePoint, out existingGid) &&
                        existingGid > 0 && existingGid < allGlyphs.Count)
                    {
                        // Ghi đè glyph hiện có tại GID đó
                        allGlyphs[existingGid] = newGlyphBytes;
                        advanceWidths[existingGid] = entry.AdvanceWidth;
                        lsbs[existingGid] = entry.LeftSideBearing;
                    }
                    else
                    {
                        // Thêm glyph mới vào cuối danh sách
                        ushort newGid = (ushort)allGlyphs.Count;
                        allGlyphs.Add(newGlyphBytes);
                        advanceWidths.Add(entry.AdvanceWidth);
                        lsbs.Add(entry.LeftSideBearing);
                        cmapMappings[entry.CodePoint] = newGid;
                    }
                }

                int totalGlyphs = allGlyphs.Count;

                // 4. Xây dựng lại bảng glyf và loca (chuẩn Long 32-bit offset)
                var newLocaOffsets = new uint[totalGlyphs + 1];
                byte[] newGlyfTable;
                using (var glyfMs = new MemoryStream())
                {
                    for (int i = 0; i < totalGlyphs; i++)
                    {
                        newLocaOffsets[i] = (uint)glyfMs.Position;
                        byte[] gb = allGlyphs[i];
                        if (gb != null && gb.Length > 0)
                        {
                            glyfMs.Write(gb, 0, gb.Length);
                            // Căn chỉnh 2-byte cho từng glyph theo chuẩn TrueType
                            if (glyfMs.Position % 2 != 0)
                            {
                                glyfMs.WriteByte(0);
                            }
                        }
                    }
                    newLocaOffsets[totalGlyphs] = (uint)glyfMs.Position;
                    newGlyfTable = glyfMs.ToArray();
                }

                byte[] newLocaTable;
                using (var locaMs = new MemoryStream((totalGlyphs + 1) * 4))
                {
                    for (int i = 0; i <= totalGlyphs; i++)
                    {
                        WriteUInt32(locaMs, newLocaOffsets[i]);
                    }
                    newLocaTable = locaMs.ToArray();
                }

                // 5. Xây dựng bảng cmap Format 4 + 12
                byte[] newCmapTable = BuildCmapTable(cmapMappings);

                // 6. Xây dựng bảng hmtx
                byte[] newHmtxTable;
                using (var hmtxMs = new MemoryStream(totalGlyphs * 4))
                {
                    for (int i = 0; i < totalGlyphs; i++)
                    {
                        WriteUInt16(hmtxMs, advanceWidths[i]);
                        WriteInt16(hmtxMs, lsbs[i]);
                    }
                    newHmtxTable = hmtxMs.ToArray();
                }

                // 7. Cập nhật bảng hhea
                byte[] newHheaTable = reader.ReadTableBytes("hhea");
                if (newHheaTable == null || newHheaTable.Length < 36)
                {
                    newHheaTable = new byte[36];
                    newHheaTable[1] = 1;
                }
                newHheaTable[34] = (byte)((totalGlyphs >> 8) & 0xFF);
                newHheaTable[35] = (byte)(totalGlyphs & 0xFF);

                // 8. Cập nhật bảng maxp
                byte[] newMaxpTable = reader.ReadTableBytes("maxp");
                if (newMaxpTable == null || newMaxpTable.Length < 6)
                {
                    newMaxpTable = new byte[32];
                    newMaxpTable[1] = 1;
                }
                newMaxpTable[4] = (byte)((totalGlyphs >> 8) & 0xFF);
                newMaxpTable[5] = (byte)(totalGlyphs & 0xFF);
                if (newMaxpTable.Length >= 10)
                {
                    ushort oldMaxPts = (ushort)((newMaxpTable[6] << 8) | newMaxpTable[7]);
                    if (maxPoints > oldMaxPts)
                    {
                        newMaxpTable[6] = (byte)((maxPoints >> 8) & 0xFF);
                        newMaxpTable[7] = (byte)(maxPoints & 0xFF);
                    }
                    ushort oldMaxCtrs = (ushort)((newMaxpTable[8] << 8) | newMaxpTable[9]);
                    if (maxContours > oldMaxCtrs)
                    {
                        newMaxpTable[8] = (byte)((maxContours >> 8) & 0xFF);
                        newMaxpTable[9] = (byte)(maxContours & 0xFF);
                    }
                }

                // 9. Cập nhật bảng head
                byte[] newHeadTable = reader.ReadTableBytes("head");
                if (newHeadTable == null || newHeadTable.Length < 54)
                {
                    throw new InvalidOperationException("Bảng head không hợp lệ.");
                }
                // Xóa checkSumAdjustment về 0 trước khi tính tổng
                newHeadTable[8] = 0;
                newHeadTable[9] = 0;
                newHeadTable[10] = 0;
                newHeadTable[11] = 0;
                // indexToLocFormat = 1 (Long format)
                newHeadTable[50] = 0;
                newHeadTable[51] = 1;
                // Cập nhật modified time (64-bit seconds since 1904)
                long otSeconds = (long)(DateTime.UtcNow - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                for (int b = 7; b >= 0; b--)
                {
                    newHeadTable[28 + (7 - b)] = (byte)((otSeconds >> (b * 8)) & 0xFF);
                }
                // Cập nhật bounding box
                newHeadTable[36] = (byte)((newFontXMin >> 8) & 0xFF);
                newHeadTable[37] = (byte)(newFontXMin & 0xFF);
                newHeadTable[38] = (byte)((newFontYMin >> 8) & 0xFF);
                newHeadTable[39] = (byte)(newFontYMin & 0xFF);
                newHeadTable[40] = (byte)((newFontXMax >> 8) & 0xFF);
                newHeadTable[41] = (byte)(newFontXMax & 0xFF);
                newHeadTable[42] = (byte)((newFontYMax >> 8) & 0xFF);
                newHeadTable[43] = (byte)(newFontYMax & 0xFF);

                // 10. Cập nhật bảng OS/2 (nếu có)
                byte[] newOs2Table = reader.ReadTableBytes("OS/2");
                if (newOs2Table != null && newOs2Table.Length >= 68)
                {
                    ushort firstChar = (ushort)((newOs2Table[64] << 8) | newOs2Table[65]);
                    ushort lastChar = (ushort)((newOs2Table[66] << 8) | newOs2Table[67]);
                    for (int i = 0; i < entryList.Count; i++)
                    {
                        int cp = entryList[i].CodePoint;
                        if (cp >= 0 && cp <= 0xFFFF)
                        {
                            if (cp < firstChar) firstChar = (ushort)cp;
                            if (cp > lastChar) lastChar = (ushort)cp;
                        }
                    }
                    newOs2Table[64] = (byte)(firstChar >> 8);
                    newOs2Table[65] = (byte)(firstChar & 0xFF);
                    newOs2Table[66] = (byte)(lastChar >> 8);
                    newOs2Table[67] = (byte)(lastChar & 0xFF);
                }

                // 11. Tập hợp các bảng hoàn chỉnh
                var finalTables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var tag in tables.Keys)
                {
                    if (tag == "glyf" || tag == "loca" || tag == "cmap" || tag == "hmtx" ||
                        tag == "hhea" || tag == "maxp" || tag == "head" || tag == "OS/2")
                    {
                        continue;
                    }
                    byte[] raw = reader.ReadTableBytes(tag);
                    if (raw != null) finalTables[tag] = raw;
                }

                finalTables["glyf"] = newGlyfTable;
                finalTables["loca"] = newLocaTable;
                finalTables["cmap"] = newCmapTable;
                finalTables["hmtx"] = newHmtxTable;
                finalTables["hhea"] = newHheaTable;
                finalTables["maxp"] = newMaxpTable;
                finalTables["head"] = newHeadTable;
                if (newOs2Table != null) finalTables["OS/2"] = newOs2Table;

                // 12. Sắp xếp danh mục bảng theo thứ tự mã ASCII tăng dần
                var sortedTags = new List<string>(finalTables.Keys);
                sortedTags.Sort(StringComparer.Ordinal);

                ushort numTables = (ushort)sortedTags.Count;
                int p2 = 1;
                int entrySel = 0;
                while ((p2 * 2) <= numTables)
                {
                    p2 *= 2;
                    entrySel++;
                }
                ushort sRange = (ushort)(p2 * 16);
                ushort rShift = (ushort)(numTables * 16 - sRange);

                // Tính toán vị trí các bảng
                uint tableDirOffset = 12;
                uint currentTableOffset = tableDirOffset + (uint)(numTables * 16);

                var tableOffsets = new Dictionary<string, uint>();
                var tableLengths = new Dictionary<string, uint>();
                var tableChecksums = new Dictionary<string, uint>();

                for (int i = 0; i < sortedTags.Count; i++)
                {
                    string tag = sortedTags[i];
                    byte[] data = finalTables[tag];
                    tableOffsets[tag] = currentTableOffset;
                    tableLengths[tag] = (uint)data.Length;
                    tableChecksums[tag] = CalculateTableChecksum(data);

                    currentTableOffset += (uint)((data.Length + 3) & ~3); // Căn lề 4-byte
                }

                // 13. Lắp ráp toàn bộ tệp font
                byte[] finalFontBytes;
                uint headTablePos = tableOffsets["head"];

                using (var outMs = new MemoryStream((int)currentTableOffset))
                {
                    // Offset Table (12 bytes)
                    WriteUInt32(outMs, 0x00010000); // sfntVersion
                    WriteUInt16(outMs, numTables);
                    WriteUInt16(outMs, sRange);
                    WriteUInt16(outMs, (ushort)entrySel);
                    WriteUInt16(outMs, rShift);

                    // Table Directory
                    for (int i = 0; i < sortedTags.Count; i++)
                    {
                        string tag = sortedTags[i];
                        byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
                        outMs.Write(tagBytes, 0, 4);
                        WriteUInt32(outMs, tableChecksums[tag]);
                        WriteUInt32(outMs, tableOffsets[tag]);
                        WriteUInt32(outMs, tableLengths[tag]);
                    }

                    // Table Data
                    for (int i = 0; i < sortedTags.Count; i++)
                    {
                        string tag = sortedTags[i];
                        byte[] data = finalTables[tag];
                        outMs.Write(data, 0, data.Length);
                        while (outMs.Position % 4 != 0)
                        {
                            outMs.WriteByte(0);
                        }
                    }

                    finalFontBytes = outMs.ToArray();
                }

                // 14. Tính toán Checksum toàn tệp và cập nhật head.checkSumAdjustment
                uint totalFileSum = CalculateTableChecksum(finalFontBytes);
                uint checkSumAdjustment = 0xB1B0AFBA - totalFileSum;

                finalFontBytes[headTablePos + 8] = (byte)((checkSumAdjustment >> 24) & 0xFF);
                finalFontBytes[headTablePos + 9] = (byte)((checkSumAdjustment >> 16) & 0xFF);
                finalFontBytes[headTablePos + 10] = (byte)((checkSumAdjustment >> 8) & 0xFF);
                finalFontBytes[headTablePos + 11] = (byte)(checkSumAdjustment & 0xFF);

                return finalFontBytes;
            }
        }

        /// <summary>
        /// Tạo đường dẫn tệp font đích với hậu tố '_modified', tự động loại bỏ các hậu tố
        /// '_modified' hoặc '_batch_pua' bị lặp lại khi nạp nhiều lần liên tiếp.
        /// </summary>
        public static string GetCleanModifiedFontPath(string fontPath)
        {
            if (string.IsNullOrEmpty(fontPath)) return "";
            string dir = Path.GetDirectoryName(fontPath);
            string fname = Path.GetFileNameWithoutExtension(fontPath);
            string ext = Path.GetExtension(fontPath);

            // Xóa triệt để các hậu tố lặp lại như _modified, _batch_pua
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                if (fname.EndsWith("_modified", StringComparison.OrdinalIgnoreCase))
                {
                    fname = fname.Substring(0, fname.Length - "_modified".Length);
                    stripped = true;
                }
                else if (fname.EndsWith("_batch_pua", StringComparison.OrdinalIgnoreCase))
                {
                    fname = fname.Substring(0, fname.Length - "_batch_pua".Length);
                    stripped = true;
                }
            }

            return Path.Combine(dir, fname + "_modified" + ext);
        }

        /// <summary>
        /// Nạp glyph vào file TTF nguồn và lưu ra tệp đích với cơ chế sao lưu tự động an toàn.
        /// </summary>
        public static void InjectGlyphsToFile(
            string sourceTtfPath,
            string destinationTtfPath,
            IEnumerable<GlyphInjectionEntry> entries,
            bool createBackup = true)
        {
            if (string.IsNullOrEmpty(sourceTtfPath) || !File.Exists(sourceTtfPath))
                throw new FileNotFoundException("Không tìm thấy tệp phông chữ nguồn: " + sourceTtfPath);

            if (string.IsNullOrEmpty(destinationTtfPath))
                throw new ArgumentException("Đường dẫn tệp đích không được để trống.");

            byte[] originalBytes = File.ReadAllBytes(sourceTtfPath);
            byte[] modifiedBytes = InjectGlyphs(originalBytes, entries);

            // Kiểm tra bảo vệ chống mất dữ liệu: Sao lưu .bak nếu ghi đè tệp gốc
            bool isOverwritingSameFile = string.Equals(
                Path.GetFullPath(sourceTtfPath),
                Path.GetFullPath(destinationTtfPath),
                StringComparison.OrdinalIgnoreCase);

            if (isOverwritingSameFile && createBackup)
            {
                string backupPath = sourceTtfPath + ".bak";
                int counter = 1;
                while (File.Exists(backupPath))
                {
                    backupPath = sourceTtfPath + ".bak" + counter;
                    counter++;
                }
                File.Copy(sourceTtfPath, backupPath, true);
            }

            // Ghi ra tệp tạm trước, sau đó hoán đổi hoặc ghi đè an toàn với cơ chế Retry
            string tempPath = destinationTtfPath + ".tmp_" + Guid.NewGuid().ToString("N");
            try
            {
                string dir = Path.GetDirectoryName(destinationTtfPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(tempPath, modifiedBytes);

                bool written = false;
                Exception lastEx = null;

                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        // Thử 1: Ghi đè trực tiếp vào FileStream với FileShare.ReadWrite
                        using (var fs = new FileStream(destinationTtfPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        {
                            fs.Write(modifiedBytes, 0, modifiedBytes.Length);
                            fs.Flush();
                        }
                        written = true;
                        break;
                    }
                    catch (Exception ex1)
                    {
                        lastEx = ex1;
                        try
                        {
                            // Thử 2: Xóa file cũ và di chuyển tệp tạm vào
                            if (File.Exists(destinationTtfPath))
                            {
                                File.Delete(destinationTtfPath);
                            }
                            File.Move(tempPath, destinationTtfPath);
                            written = true;
                            break;
                        }
                        catch (Exception ex2)
                        {
                            lastEx = ex2;
                            try
                            {
                                // Thử 3: Sao chép đè bản tạm
                                File.Copy(tempPath, destinationTtfPath, true);
                                written = true;
                                break;
                            }
                            catch (Exception ex3)
                            {
                                lastEx = ex3;
                            }
                        }
                    }

                    if (!written && attempt < 5)
                    {
                        System.Threading.Thread.Sleep(50 * attempt);
                    }
                }

                if (!written && lastEx != null)
                {
                    throw lastEx;
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
        #endregion
    }
}
