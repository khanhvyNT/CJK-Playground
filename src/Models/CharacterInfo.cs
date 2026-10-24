using System;
using System.Text;

namespace CJKPlayground.Models
{
    public class CharacterInfo
    {
        public int CodePoint { get; set; }
        public string Character { get; set; }
        public string HexCode
        {
            get { return "U+" + CodePoint.ToString("X4"); }
        }
        public string BlockName { get; set; }
        public string HanViet { get; set; }
        public string Pinyin { get; set; }
        public string ChuNom { get; set; }
        public string Definition { get; set; }
        public int StrokeCount { get; set; }
        public string Radical { get; set; }
        public int GlyphIndex { get; set; }
        public int AdvanceWidth { get; set; }

        public string Utf8Hex
        {
            get
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Character);
                var sb = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (i > 0) sb.Append(" ");
                    sb.Append(bytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }

        public string Utf16Hex
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Character.Length; i++)
                {
                    if (i > 0) sb.Append(" ");
                    sb.Append(((int)Character[i]).ToString("X4"));
                }
                return sb.ToString();
            }
        }

        public CharacterInfo(int codePoint)
        {
            CodePoint = codePoint;
            Character = char.ConvertFromUtf32(codePoint);
            var block = UnicodeCatalog.FindBlock(codePoint);
            BlockName = block != null ? block.Name : GetBlockName(codePoint);
            GlyphIndex = -1;
            AdvanceWidth = -1;
        }

        public static string GetBlockName(int cp)
        {
            if (cp >= 0x4E00 && cp <= 0x9FFF) return "CJK Unified Ideographs (BMP)";
            if (cp >= 0x3400 && cp <= 0x4DBF) return "CJK Unified Ideographs Ext A";
            if (cp >= 0x20000 && cp <= 0x2A6DF) return "CJK Unified Ideographs Ext B (Chữ Nôm)";
            if (cp >= 0x2A700 && cp <= 0x2B73F) return "CJK Unified Ideographs Ext C";
            if (cp >= 0x2B740 && cp <= 0x2B81F) return "CJK Unified Ideographs Ext D";
            if (cp >= 0x2F00 && cp <= 0x2FDF) return "Kangxi Radicals (214 Bộ thủ)";
            if (cp >= 0x0020 && cp <= 0x007E) return "Basic Latin (ASCII)";
            if (cp >= 0x00A0 && cp <= 0x00FF) return "Latin-1 Supplement";
            return "Unicode Block (U+" + cp.ToString("X4") + ")";
        }
    }
}
