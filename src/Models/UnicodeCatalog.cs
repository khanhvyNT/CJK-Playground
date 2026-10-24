using System;
using System.Collections.Generic;

namespace CJKPlayground.Models
{
    public class UnicodeBlock
    {
        public string Name { get; set; }
        public int StartCp { get; set; }
        public int EndCp { get; set; }
        public int Plane { get; set; }
        public string PlaneName { get; set; }
        public string Category { get; set; } // "cjk", "latin", "symbols", "scripts", "special"

        public int TotalCharacters
        {
            get { return EndCp - StartCp + 1; }
        }

        public string RangeString
        {
            get { return string.Format("U+{0:X4}..U+{1:X4}", StartCp, EndCp); }
        }

        public UnicodeBlock(string name, int start, int end, int plane, string planeName, string category = "cjk")
        {
            Name = name;
            StartCp = start;
            EndCp = end;
            Plane = plane;
            PlaneName = planeName;
            Category = category;
        }

        public override string ToString()
        {
            return string.Format("{0} [{1}]", Name, RangeString);
        }
    }

    public static class UnicodeCatalog
    {
        public static readonly List<UnicodeBlock> Blocks = new List<UnicodeBlock>
        {
            // === PLANE 0: BMP ===
            new UnicodeBlock("CJK Unified Ideographs (BMP)", 0x4E00, 0x9FFF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("CJK Unified Ideographs Ext A", 0x3400, 0x4DBF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Kangxi Radicals (214 Bộ thủ)", 0x2F00, 0x2FD5, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("CJK Radicals Supplement", 0x2E80, 0x2EF3, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Ideographic Description Characters (IDC)", 0x2FF0, 0x2FFF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("CJK Symbols and Punctuation", 0x3000, 0x303F, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Hiragana", 0x3040, 0x309F, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Katakana", 0x30A0, 0x30FF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Bopomofo (Chú âm phù hiệu)", 0x3100, 0x312F, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Hangul Compatibility Jamo", 0x3130, 0x318F, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("CJK Strokes (Các nét chữ Hán)", 0x31C0, 0x31EF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Enclosed CJK Letters and Months", 0x3200, 0x32FF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("CJK Compatibility", 0x3300, 0x33FF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Hangul Syllables (Âm tiết Hàn)", 0xAC00, 0xD7AF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("CJK Compatibility Ideographs", 0xF900, 0xFAFF, 0, "Plane 0: BMP", "cjk"),
            new UnicodeBlock("Basic Latin (ASCII)", 0x0020, 0x007E, 0, "Plane 0: BMP", "latin"),
            new UnicodeBlock("Latin-1 Supplement", 0x00A0, 0x00FF, 0, "Plane 0: BMP", "latin"),
            new UnicodeBlock("Latin Extended Additional (Tiếng Việt)", 0x1E00, 0x1EFF, 0, "Plane 0: BMP", "latin"),
            new UnicodeBlock("Private Use Area (PUA)", 0xE000, 0xF8FF, 0, "Plane 0: BMP", "special"),

            // === PLANE 2: SIP (CJK mở rộng / Chữ Nôm) ===
            new UnicodeBlock("CJK Unified Ideographs Ext B", 0x20000, 0x2A6DF, 2, "Plane 2: SIP", "cjk"),
            new UnicodeBlock("CJK Unified Ideographs Ext C", 0x2A700, 0x2B73F, 2, "Plane 2: SIP", "cjk"),
            new UnicodeBlock("CJK Unified Ideographs Ext D", 0x2B740, 0x2B81F, 2, "Plane 2: SIP", "cjk"),
            new UnicodeBlock("CJK Unified Ideographs Ext E", 0x2B820, 0x2CEAF, 2, "Plane 2: SIP", "cjk"),
            new UnicodeBlock("CJK Unified Ideographs Ext F", 0x2CEB0, 0x2EBEF, 2, "Plane 2: SIP", "cjk"),
            new UnicodeBlock("CJK Unified Ideographs Ext I", 0x2EBF0, 0x2EE5F, 2, "Plane 2: SIP", "cjk"),
            new UnicodeBlock("CJK Compatibility Ideographs Supplement", 0x2F800, 0x2FA1F, 2, "Plane 2: SIP", "cjk"),

            // === PLANE 3: TIP ===
            new UnicodeBlock("CJK Unified Ideographs Ext G", 0x30000, 0x3134F, 3, "Plane 3: TIP", "cjk"),
            new UnicodeBlock("CJK Unified Ideographs Ext H", 0x31350, 0x323AF, 3, "Plane 3: TIP", "cjk")
        };

        public static UnicodeBlock FindBlock(int codePoint)
        {
            foreach (var b in Blocks)
            {
                if (codePoint >= b.StartCp && codePoint <= b.EndCp)
                    return b;
            }
            return null;
        }
    }
}
