using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    /// <summary>
    /// 16 lớp hình thái học đóng (MECE Morphological Shape Classes) cho Beautiful IDS v2.0.
    /// </summary>
    public enum ShapeClass16 : byte
    {
        SquareBlock = 0,     // Khối vuông đặc (日, 田, 口, 国)
        NarrowPillar = 1,    // Trụ dọc hẹp (亻, 氵, 忄, 刂, 阝, 彳)
        WideRoof = 2,        // Mái che rộng đỉnh (宀, 冖, 雨, ⻗, 穴, ⺮, 艹, 爫)
        WideBase = 3,        // Đế đỡ rộng đáy (皿, 灬, 土, 心, 止, 旦, 立)
        TopCavity = 4,       // Khối lõm đỉnh (凵, 山, 臼)
        BotCavity = 5,       // Khối lõm đáy / choãi chân (人, 八, 大, 天, 父, 木, 禾)
        LeftBracket = 6,     // Ngoặc bao trái (匚, 匸)
        TopBracket = 7,      // Ngoặc bao trên (冂, 同, 門, 门, 鬥)
        DiagTlDrape = 8,     // Khung rủ trên-trái (广, 厂, 尸, 户, 疒, 虍, 麻)
        DiagTrHook = 9,      // Khung móc trên-phải (勹, 气, 戈, 弋, 几, 風)
        DiagBlTray = 10,     // Khay đỡ dưới-trái (辶, 廴, 走, 鬼, 麦, 支)
        FullRing = 11,       // Khung bao kín (囗)
        SymmetricCross = 12, // Chữ thập / đối xứng tâm (十, 米, 乂, 爻, 巫, 東, 束)
        TriangleUp = 13,     // Tam giác nhọn đỉnh (厶, 幺, 云, 会, 合)
        FlatBar = 14,        // Thanh ngang dẹt (一, 二, 三, ⺀)
        ThinPost = 15        // Thanh dọc mảnh (丨, 亅, 丿)
    }

    /// <summary>
    /// Bản ghi chữ mẫu rút gọn trong chỉ mục 98.000 chữ All HAN IDS.TXT.
    /// </summary>
    public struct CorpusExemplarRecord
    {
        public int CodePoint;
        public byte OpId;
        public int C0Cp;
        public int C1Cp;
        public int C2Cp;
        public byte C0SubOp;
        public byte C1SubOp;
        public byte C0Strokes;
        public byte C1Strokes;
        public byte C2Strokes;
        public ShapeClass16 C0Shape;
        public ShapeClass16 C1Shape;
        public UnicodeTier Tier;
    }

    /// <summary>
    /// Kết quả truy vấn 4 tầng (Tier 1..4) từ chỉ mục All HAN IDS.TXT.
    /// </summary>
    public class CorpusMatchCandidate
    {
        public CorpusExemplarRecord Record;
        public int MatchedTier;        // 1 = Exact Pos + Sibling Struct, 2 = Exact Pos, 3 = Shape+Stroke Band
        public float TierWeight;       // Trọng số tầng (1.0, 0.70, 0.40)
        public float StrokeSimilarity; // Trọng số Gaussian tương đồng số nét anh em [0..1]
        public float CombinedWeight;   // TierWeight * StrokeSimilarity
    }

    /// <summary>
    /// STAGE 0 & STAGE 3A: Bộ máy Chỉ mục 98.000 chữ Hán-Nôm từ All HAN IDS.TXT.
    /// - Giải nén DAG Macro {1}..{122} với kiểm tra chu trình 3 màu (UNVISITED/VISITING/VISITED).
    /// - Chọn hình thể ưu tiên theo vùng: V > T > H > J > K > G.
    /// - Xây dựng bảng tra ngược Sub-IDS -> CodePoint và Positional Index 4 tầng.
    /// </summary>
    public static class HanIdsCorpusIndex
    {
        private static readonly object InitLock = new object();
        private static bool _isInitialized = false;
        private static bool _ablationMode = false;

        // Bảng macro {1}..{122} đã mở rộng
        private static readonly Dictionary<int, string> RawMacros = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> ExpandedMacros = new Dictionary<int, string>();
        private static bool _hasMacroCycleError = false;

        // Bảng dữ liệu chính từ All HAN IDS.TXT
        private static readonly Dictionary<int, string> CharToExpandedIds = new Dictionary<int, string>(100000);
        private static readonly Dictionary<string, List<int>> IdsToCodePoints = new Dictionary<string, List<int>>(100000, StringComparer.Ordinal);
        private static readonly Dictionary<ulong, int> SubIdsHashToCodepoint = new Dictionary<ulong, int>(100000);
        private static readonly Dictionary<int, byte> StrokeCountByCp = new Dictionary<int, byte>(100000);
        private static readonly Dictionary<int, ShapeClass16> ShapeClassByCp = new Dictionary<int, ShapeClass16>(100000);

        // Chỉ mục vị trí Tier 1 & Tier 2: Key = PackPositionalKey(opId, slot, canonicalCompCp)
        private static readonly Dictionary<ulong, List<CorpusExemplarRecord>> PositionalBuckets =
            new Dictionary<ulong, List<CorpusExemplarRecord>>(65536);

        // Chỉ mục hình thái Tier 3: Key = PackMorphKey(opId, slot, shapeClass, strokeBand)
        private static readonly Dictionary<uint, List<CorpusExemplarRecord>> MorphologicalBuckets =
            new Dictionary<uint, List<CorpusExemplarRecord>>(2048);

        // Tập hợp các cặp (opId, c0Cp, c1Cp) xuất hiện nguyên bản trong kho (phục vụ Lexical Exception)
        private static readonly HashSet<ulong> ExactBinaryPairsInCorpus = new HashSet<ulong>();

        public static int TotalIndexedCharacters
        {
            get
            {
                EnsureInitialized();
                return CharToExpandedIds.Count;
            }
        }

        public static bool IsAblationMode
        {
            get { return _ablationMode; }
            set { _ablationMode = value; }
        }

        public static bool HasMacroCycleError
        {
            get
            {
                EnsureInitialized();
                return _hasMacroCycleError;
            }
        }

        public static void EnsureInitialized()
        {
            if (_isInitialized) return;
            lock (InitLock)
            {
                if (_isInitialized) return;
                _isInitialized = true;
                InitializeCorpus();
            }
        }

        // =========================================================================
        // 1. MÃ HÓA TOÁN TỬ & KHÓA BIT-PACKING 64-BIT
        // =========================================================================
        public static byte GetOperatorId(string op)
        {
            if (string.IsNullOrEmpty(op)) return 0;
            char c = op[0];
            if (c >= '\u2FF0' && c <= '\u2FFB') return (byte)(c - '\u2FF0' + 1); // 1..12
            if (c == '\u2FFC') return 13;
            if (c == '\u2FFD') return 14;
            if (c == '\u2FFE') return 15;
            if (c == '\u2FFF') return 16;
            if (c == '\u31EF') return 17;
            if (c == '\u303E') return 18;
            return 0;
        }

        public static ulong PackPositionalKey(byte opId, int slot, int canonicalCompCp)
        {
            return ((ulong)opId << 28) | (((ulong)(slot & 0x3)) << 24) | ((ulong)(uint)(canonicalCompCp & 0xFFFFFF));
        }

        public static uint PackMorphKey(byte opId, int slot, ShapeClass16 shape, int strokeBand)
        {
            return ((uint)opId << 16) | (((uint)(slot & 0x3)) << 12) | (((uint)shape & 0xF) << 4) | ((uint)(strokeBand & 0xF));
        }

        public static ulong PackExactBinaryPair(byte opId, int c0Cp, int c1Cp)
        {
            return ((ulong)opId << 48) | (((ulong)(uint)(c0Cp & 0xFFFFFF)) << 24) | ((ulong)(uint)(c1Cp & 0xFFFFFF));
        }

        public static ulong ComputeSubIdsFnv1a(byte opId, ulong h0, ulong h1, ulong h2 = 0)
        {
            ulong hash = 14695981039346656037UL;
            hash = (hash ^ opId) * 1099511628211UL;
            hash = (hash ^ h0) * 1099511628211UL;
            hash = (hash ^ h1) * 1099511628211UL;
            if (h2 != 0)
            {
                hash = (hash ^ h2) * 1099511628211UL;
            }
            return hash;
        }

        public static ulong ComputeLeafFnv1a(int canonicalCp)
        {
            ulong hash = 14695981039346656037UL;
            hash = (hash ^ 0xFFUL) * 1099511628211UL;
            hash = (hash ^ (ulong)(uint)canonicalCp) * 1099511628211UL;
            return hash;
        }

        // =========================================================================
        // 2. CHUẨN HÓA BỘ THỦ ĐỒNG NHẤT GIỮA KHO ALL HAN IDS.TXT VÀ TRUY VẤN
        // =========================================================================
        public static int NormalizeCorpusComponent(int cp, byte opId, int slot)
        {
            cp = FoldKangxiRadicalCodePoint(cp);

            // Quy ước của All HAN IDS.TXT (lines 200-214):
            // Quy đồng các biến thể vị trí về một mã chuẩn duy nhất cho mỗi vị trí
            // để cả chữ trong All HAN IDS.TXT lẫn công thức người dùng đều khớp 100%.
            if ((opId == 1 || opId == 3) && slot == 0) // ⿰[0] hoặc ⿲[0] (Bên trái)
            {
                if (cp == 0x6C34) return 0x6C35; // 水 -> 氵
                if (cp == 0x4EBA) return 0x4EBB; // 人 -> 亻
                if (cp == 0x624B) return 0x624C; // 手 -> 扌
                if (cp == 0x5FC3) return 0x5FC4; // 心 -> 忄
                if (cp == 0x72AC) return 0x72AD; // 犬 -> 犭
                if (cp == 0x793A || cp == 0x793B) return 0x793B; // 示/礻 -> 礻
                if (cp == 0x8863) return 0x8864; // 衣 -> 衤
                if (cp == 0x961C || cp == 0x2ECF) return 0x961D; // 阜 -> 阝
                if (cp == 0x7389 || cp == 0x248E9) return 0x738B; // 玉 / 𤣩 -> 王
                if (cp == 0x8A01 || cp == 0x8EC0) return 0x8A00; // 訁 / 讠 -> 言
                if (cp == 0x91D2 || cp == 0x9485) return 0x91D1; // 釒 / 钅 -> 金
                if (cp == 0x7CF9 || cp == 0x7E9F) return 0x7CF8; // 糹 / 纟 -> 糸
                if (cp == 0x98E0 || cp == 0x9963) return 0x98DF; // 飠 / 饣 -> 食
                if (cp == 0x725B || cp == 0x725C) return 0x725B; // 牛 / 牜 -> 牛
                if (cp == 0x8DB3 || cp == 0x2ECA) return 0x8DB3; // 足 / ⻊ -> 足
                if (cp == 0x8089 || cp == 0x2EBC) return 0x6708; // 肉 / ⺼ -> 月
            }
            else if ((opId == 1 && slot == 1) || (opId == 3 && slot == 2)) // ⿰[1] hoặc ⿲[2] (Bên phải)
            {
                if (cp == 0x5200) return 0x5202; // 刀 -> 刂
                if (cp == 0x9091 || cp == 0x2ED6) return 0x961D; // 邑 -> 阝
            }
            else if ((opId == 2 || opId == 4) && slot == 0) // ⿱[0] hoặc ⿳[0] (Phía trên)
            {
                if (cp == 0x8278 || cp == 0x8349 || cp == 0x2EBE || cp == 0x2EBF) return 0x8279; // 艸/草 -> 艹
                if (cp == 0x7AF9 || cp == 0x25AD7 || cp == 0x2EAE) return 0x7AF9; // 竹 / 𥫗 / ⺮ -> 竹
                if (cp == 0x722A || cp == 0x2EA4) return 0x722B; // 爪 -> 爫
                if (cp == 0x96E8 || cp == 0x2ED7) return 0x96E8; // 雨 / ⻗ -> 雨
                if (cp == 0x7F51 || cp == 0x2EB2 || cp == 0x7F52) return 0x7F52; // 网 / ⺲ -> 罒
            }
            else if ((opId == 2 && slot == 1) || (opId == 4 && slot == 2)) // ⿱[1] hoặc ⿳[2] (Phía dưới)
            {
                if (cp == 0x2E97) return 0x5FC3; // 㣺 -> 心
            }
            else if (opId == 11 && slot == 0) // ⿺[0] (Bao dưới-trái)
            {
                if (cp == 0x8FB5 || cp == 0x2ECC) return 0x8FB6; // 辵 -> 辶
                if (cp == 0x2ECE) return 0x8D70; // ⻞ -> 走
                if (cp == 0x2ECC) return 0x5EF4; // ⻌ -> 廴
            }

            // Đồng nhất toàn cục cho các biến thể bộ thủ CJK
            if (cp == 0x248E9) return 0x738B; // 𤣩 -> 王
            if (cp == 0x25AD7 || cp == 0x2EAE) return 0x7AF9; // 𥫗/⺮ -> 竹
            if (cp == 0x8A01) return 0x8A00;  // 訁 -> 言
            if (cp == 0x91D2) return 0x91D1;  // 釒 -> 金
            if (cp == 0x7CF9) return 0x7CF8;  // 糹 -> 糸
            if (cp == 0x98E0) return 0x98DF;  // 飠 -> 食
            if (cp == 0x725C) return 0x725B;  // 牜 -> 牛
            if (cp == 0x2EBC) return 0x6708;  // ⺼ -> 月

            return cp;
        }

        // Bảng ánh xạ chuẩn 214 Bộ thủ Khang Hi (U+2F00..U+2FD5) sang CJK Unified Ideographs
        private static readonly int[] KangxiToUnifiedTable = new int[214]
        {
            0x4E00, 0x4E28, 0x4E36, 0x4E3F, 0x4E59, 0x4E85, 0x4E8C, 0x4EA0, 0x4EBA, 0x513F,
            0x5165, 0x516B, 0x5182, 0x5196, 0x51AB, 0x51E0, 0x51F5, 0x5200, 0x529B, 0x52F9,
            0x5315, 0x531A, 0x5338, 0x5341, 0x535C, 0x5369, 0x5382, 0x53B6, 0x53C8, 0x53E3,
            0x56D7, 0x571F, 0x58EB, 0x5902, 0x590A, 0x5915, 0x5927, 0x5973, 0x5B50, 0x5B80,
            0x5BF8, 0x5C0F, 0x5C22, 0x5C38, 0x5C6E, 0x5C71, 0x5DDB, 0x5DE5, 0x5DF1, 0x5DFE,
            0x5E72, 0x5E7A, 0x5E7F, 0x5EF4, 0x5EFE, 0x5F0B, 0x5F13, 0x5F50, 0x5F61, 0x5F73,
            0x5FC3, 0x6208, 0x6236, 0x624B, 0x652F, 0x6534, 0x6587, 0x6597, 0x65A4, 0x65B9,
            0x65E0, 0x65E5, 0x66F0, 0x6708, 0x6728, 0x6B20, 0x6B62, 0x6B79, 0x6BB3, 0x6BCB,
            0x6BD4, 0x6BDB, 0x6C0F, 0x6C14, 0x6C34, 0x706B, 0x722A, 0x7236, 0x723B, 0x723F,
            0x7247, 0x7259, 0x725B, 0x72AC, 0x7384, 0x7389, 0x74DC, 0x74E6, 0x7518, 0x751F,
            0x7528, 0x7530, 0x758B, 0x7592, 0x7676, 0x767D, 0x76AE, 0x76BF, 0x76EE, 0x77DB,
            0x77E2, 0x77F3, 0x793A, 0x79B8, 0x79BE, 0x7A74, 0x7ACB, 0x7AF9, 0x7C73, 0x7CF8,
            0x7F36, 0x7F51, 0x7F8A, 0x7FBD, 0x8001, 0x800C, 0x8012, 0x8033, 0x807F, 0x8089,
            0x81E3, 0x81EA, 0x81F3, 0x81FC, 0x820C, 0x821B, 0x821F, 0x826E, 0x8272, 0x8278,
            0x864D, 0x866B, 0x8840, 0x884C, 0x8863, 0x897E, 0x898B, 0x89D2, 0x8A00, 0x8C37,
            0x8C46, 0x8C55, 0x8C78, 0x8C9D, 0x8D64, 0x8D70, 0x8DB3, 0x8EAB, 0x8ECA, 0x8F9B,
            0x8FB0, 0x8FB5, 0x9091, 0x9149, 0x91C6, 0x91CC, 0x91D1, 0x9577, 0x9580, 0x961C,
            0x96B6, 0x96B9, 0x96E8, 0x9751, 0x975E, 0x9762, 0x9769, 0x97CB, 0x97ED, 0x97F3,
            0x9801, 0x98A8, 0x98DB, 0x98DF, 0x9996, 0x9999, 0x99AC, 0x9AA8, 0x9AD8, 0x9ADF,
            0x9B25, 0x9B2F, 0x9B32, 0x9B3C, 0x9B5A, 0x9CE5, 0x9E75, 0x9E7F, 0x9EA5, 0x9EBB,
            0x9EC3, 0x9ECD, 0x9ED1, 0x9EF9, 0x9EFD, 0x9F0E, 0x9F13, 0x9F20, 0x9F3B, 0x9F4A,
            0x9F52, 0x9F8D, 0x9F9C, 0x9FA0
        };

        public static int FoldKangxiRadicalCodePoint(int cp)
        {
            if (cp >= 0x2F00 && cp <= 0x2FD5)
            {
                return KangxiToUnifiedTable[cp - 0x2F00];
            }
            return cp;
        }

        // =========================================================================
        // 3. KHỞI TẠO & PHÂN TÍCH ALL HAN IDS.TXT
        // =========================================================================
        private static void InitializeCorpus()
        {
            SeedBaseStrokeAndShapeTables();
            SeedDefaultMacros();

            using (var reader = OpenCorpusStreamReader())
            {
                if (reader == null) return;

                string line;
                bool macrosResolved = false;

                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;

                    // Loại bỏ BOM ở dòng đầu tiên nếu có
                    if (line[0] == '\uFEFF') line = line.Substring(1);
                    if (line.Length == 0) continue;

                    if (line[0] == '#')
                    {
                        // Kiểm tra dòng định nghĩa Legend: #\t{k}\t(xxxx)\t<ids>\t...
                        ParseLegendCommentLine(line);
                        continue;
                    }

                    if (!macrosResolved)
                    {
                        ResolveAllMacrosWithCycleCheck();
                        macrosResolved = true;
                    }

                    ParseCharacterEntryLine(line);
                }

                if (!macrosResolved)
                {
                    ResolveAllMacrosWithCycleCheck();
                }
            }

            // Tính toán số nét và lớp hình thái lan truyền cho toàn bộ 98.000 chữ
            PropagateCorpusStrokesAndShapes();

            // Xây dựng PositionalBuckets (Tier 1 & 2) và MorphologicalBuckets (Tier 3)
            BuildPositionalAndMorphologicalIndices();
        }

        private static StreamReader OpenCorpusStreamReader()
        {
            // 1. Thử đọc từ Embedded Resource trong CJKPlayground.exe
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var resName in asm.GetManifestResourceNames())
                {
                    if (resName.EndsWith("All_HAN_IDS.TXT", StringComparison.OrdinalIgnoreCase) ||
                        resName.EndsWith("All HAN IDS.TXT", StringComparison.OrdinalIgnoreCase))
                    {
                        var stream = asm.GetManifestResourceStream(resName);
                        if (stream != null) return new StreamReader(stream, Encoding.UTF8);
                    }
                }
            }
            catch { }

            // 2. Thử đọc từ các đường dẫn file trên đĩa
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, "data", "All HAN IDS.TXT"),
                Path.Combine(baseDir, "All HAN IDS.TXT"),
                @"data\All HAN IDS.TXT",
                @"..\data\All HAN IDS.TXT",
                @"C:\Users\Admin\Desktop\CJKPlayground\data\All HAN IDS.TXT",
                @"c:\Users\Admin\Downloads\All HAN IDS.TXT"
            };

            foreach (var p in candidates)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        return new StreamReader(p, Encoding.UTF8);
                    }
                    catch { }
                }
            }

            return null;
        }

        private static void ParseLegendCommentLine(string line)
        {
            // Định dạng: #\t{1}\t(F2A0 ...)\t⿹⿺㇉一灬\t...
            int openBrace = line.IndexOf('{');
            if (openBrace < 0 || openBrace > 6) return;
            int closeBrace = line.IndexOf('}', openBrace + 1);
            if (closeBrace < 0) return;

            int macroId;
            if (!int.TryParse(line.Substring(openBrace + 1, closeBrace - openBrace - 1), out macroId))
                return;

            int tabAfterBrace = line.IndexOf('\t', closeBrace);
            if (tabAfterBrace < 0) return;
            int tabAfterPua = line.IndexOf('\t', tabAfterBrace + 1);
            if (tabAfterPua < 0) return;
            int tabAfterIds = line.IndexOf('\t', tabAfterPua + 1);

            string idsExpr = (tabAfterIds > tabAfterPua)
                ? line.Substring(tabAfterPua + 1, tabAfterIds - tabAfterPua - 1).Trim()
                : line.Substring(tabAfterPua + 1).Trim();

            if (!string.IsNullOrEmpty(idsExpr))
            {
                RawMacros[macroId] = idsExpr;
            }
        }

        /// <summary>
        /// Giải nén DAG cho {1}..{122} bằng thuật toán DFS 3 màu (0=Unvisited, 1=Visiting, 2=Visited).
        /// </summary>
        public static bool TryExpandMacroString(string input, Dictionary<int, string> macroTable, out string expanded, out string errorCode)
        {
            expanded = input;
            errorCode = null;
            if (string.IsNullOrEmpty(input) || input.IndexOf('{') < 0) return true;

            var color = new Dictionary<int, int>();
            var memo = new Dictionary<int, string>();
            bool cycleDetected = false;

            Func<int, string> dfs = null;
            dfs = (id) =>
            {
                int c;
                if (color.TryGetValue(id, out c))
                {
                    if (c == 1)
                    {
                        cycleDetected = true;
                        return "";
                    }
                    if (c == 2) return memo[id];
                }

                color[id] = 1; // VISITING
                string raw;
                if (!macroTable.TryGetValue(id, out raw) || string.IsNullOrEmpty(raw) || raw == "？")
                {
                    // Ký hiệu cấu kiện nguyên tử không phân giải được -> gán mã PUA Plane 15 riêng biệt
                    string atomicPua = char.ConvertFromUtf32(0xF0000 + id);
                    memo[id] = atomicPua;
                    color[id] = 2;
                    return atomicPua;
                }

                var sb = new StringBuilder();
                int idx = 0;
                while (idx < raw.Length)
                {
                    int ob = raw.IndexOf('{', idx);
                    if (ob < 0)
                    {
                        sb.Append(raw, idx, raw.Length - idx);
                        break;
                    }
                    if (ob > idx)
                    {
                        sb.Append(raw, idx, ob - idx);
                    }
                    int cb = raw.IndexOf('}', ob + 1);
                    if (cb < 0)
                    {
                        sb.Append(raw, ob, raw.Length - ob);
                        break;
                    }
                    int childId;
                    if (int.TryParse(raw.Substring(ob + 1, cb - ob - 1), out childId))
                    {
                        string childExp = dfs(childId);
                        if (cycleDetected) return "";
                        sb.Append(childExp);
                    }
                    else
                    {
                        sb.Append(raw, ob, cb - ob + 1);
                    }
                    idx = cb + 1;
                }

                string res = sb.ToString();
                memo[id] = res;
                color[id] = 2; // VISITED
                return res;
            };

            var outSb = new StringBuilder();
            int pos = 0;
            while (pos < input.Length)
            {
                int ob = input.IndexOf('{', pos);
                if (ob < 0)
                {
                    outSb.Append(input, pos, input.Length - pos);
                    break;
                }
                if (ob > pos) outSb.Append(input, pos, ob - pos);
                int cb = input.IndexOf('}', ob + 1);
                if (cb < 0)
                {
                    outSb.Append(input, ob, input.Length - ob);
                    break;
                }
                int mid;
                if (int.TryParse(input.Substring(ob + 1, cb - ob - 1), out mid))
                {
                    string rep = dfs(mid);
                    if (cycleDetected)
                    {
                        errorCode = "ERR_MACRO_CYCLE";
                        expanded = null;
                        return false;
                    }
                    outSb.Append(rep);
                }
                else
                {
                    outSb.Append(input, ob, cb - ob + 1);
                }
                pos = cb + 1;
            }

            expanded = outSb.ToString();
            return true;
        }

        private static void ResolveAllMacrosWithCycleCheck()
        {
            ExpandedMacros.Clear();
            _hasMacroCycleError = false;

            foreach (var kvp in RawMacros)
            {
                string exp, err;
                if (TryExpandMacroString("{" + kvp.Key + "}", RawMacros, out exp, out err))
                {
                    ExpandedMacros[kvp.Key] = exp;
                }
                else
                {
                    _hasMacroCycleError = true;
                }
            }
        }

        private static void ParseCharacterEntryLine(string line)
        {
            // Định dạng: U+4E00\t一\t^一$(GHTJKPV)\t...
            if (!line.StartsWith("U+", StringComparison.Ordinal)) return;

            int tab1 = line.IndexOf('\t');
            if (tab1 <= 2) return;

            int tab2 = line.IndexOf('\t', tab1 + 1);
            if (tab2 < 0) return;

            // Parse mã U+XXXX
            string hexPart = line.Substring(2, tab1 - 2);
            int cp;
            if (!int.TryParse(hexPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp))
                return;

            // Duyệt qua các cột ^IDS$(Regions) để chọn hình thể ưu tiên V > T > H > J > K > G
            string bestIds = null;
            int bestScore = -1;

            int colStart = tab2 + 1;
            while (colStart < line.Length)
            {
                int nextTab = line.IndexOf('\t', colStart);
                string col = (nextTab >= 0)
                    ? line.Substring(colStart, nextTab - colStart)
                    : line.Substring(colStart);

                colStart = (nextTab >= 0) ? (nextTab + 1) : line.Length;

                if (col.Length == 0 || col[0] == '*') break; // Ghi chú * bắt đầu ở cuối dòng
                if (col[0] != '^') continue;

                int dollarIdx = col.IndexOf('$');
                if (dollarIdx <= 1) continue;

                string rawIds = col.Substring(1, dollarIdx - 1);
                if (rawIds == "？") continue;

                string regionPart = (dollarIdx + 1 < col.Length) ? col.Substring(dollarIdx + 1) : "";
                int score = ScoreRegionPriority(regionPart);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIds = rawIds;
                }
            }

            if (string.IsNullOrEmpty(bestIds)) return;

            // Mở rộng macro {1}..{122} nếu có trong bestIds
            if (bestIds.IndexOf('{') >= 0)
            {
                var sb = new StringBuilder(bestIds.Length + 8);
                int idx = 0;
                while (idx < bestIds.Length)
                {
                    int ob = bestIds.IndexOf('{', idx);
                    if (ob < 0)
                    {
                        sb.Append(bestIds, idx, bestIds.Length - idx);
                        break;
                    }
                    if (ob > idx) sb.Append(bestIds, idx, ob - idx);
                    int cb = bestIds.IndexOf('}', ob + 1);
                    if (cb < 0)
                    {
                        sb.Append(bestIds, ob, bestIds.Length - ob);
                        break;
                    }
                    int mid;
                    string rep;
                    if (int.TryParse(bestIds.Substring(ob + 1, cb - ob - 1), out mid) &&
                        ExpandedMacros.TryGetValue(mid, out rep))
                    {
                        sb.Append(rep);
                    }
                    else
                    {
                        sb.Append(char.ConvertFromUtf32(0xF0000 + (mid > 0 ? mid : 999)));
                    }
                    idx = cb + 1;
                }
                bestIds = sb.ToString();
            }

            CharToExpandedIds[cp] = bestIds;
            if (!string.IsNullOrEmpty(bestIds))
            {
                List<int> cps;
                if (!IdsToCodePoints.TryGetValue(bestIds, out cps))
                {
                    cps = new List<int>(1);
                    IdsToCodePoints[bestIds] = cps;
                }
                cps.Add(cp);
            }
        }

        private static int ScoreRegionPriority(string regionPart)
        {
            if (string.IsNullOrEmpty(regionPart)) return 65; // Universal
            // Thứ tự ưu tiên chuẩn: V > T > H > J > K > G > khác
            if (regionPart.IndexOf('V') >= 0) return regionPart.Contains("[V]") ? 95 : 100;
            if (regionPart.IndexOf('T') >= 0) return regionPart.Contains("[T]") ? 85 : 90;
            if (regionPart.IndexOf('H') >= 0) return regionPart.Contains("[H]") ? 75 : 80;
            if (regionPart.IndexOf('J') >= 0) return regionPart.Contains("[J]") ? 65 : 70;
            if (regionPart.IndexOf('K') >= 0) return regionPart.Contains("[K]") ? 55 : 60;
            if (regionPart.IndexOf('G') >= 0) return regionPart.Contains("[G]") ? 45 : 50;
            return 20;
        }

        // =========================================================================
        // 4. XÂY DỰNG CHỈ MỤC VỊ TRÍ, SỐ NÉT & TRA NGƯỢC SUB-IDS
        // =========================================================================
        private static List<int> DecodeCodePointsFast(string s)
        {
            var list = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    list.Add(char.ConvertToUtf32(c, s[i + 1]));
                    i++;
                }
                else
                {
                    list.Add(c);
                }
            }
            return list;
        }

        private static byte GetOperatorIdFromCp(int cp)
        {
            if (cp >= 0x2FF0 && cp <= 0x2FFB) return (byte)(cp - 0x2FF0 + 1);
            if (cp == 0x2FFC) return 13;
            if (cp == 0x2FFD) return 14;
            if (cp == 0x2FFE) return 15;
            if (cp == 0x2FFF) return 16;
            if (cp == 0x31EF) return 17;
            if (cp == 0x303E) return 18;
            return 0;
        }

        private static int GetOperatorArityById(byte opId)
        {
            if (opId == 3 || opId == 4) return 3; // ⿲, ⿳
            if (opId == 15 || opId == 16 || opId == 18) return 1; // ⿾, ⿿, 〾
            if (opId >= 1 && opId <= 17) return 2;
            return 0;
        }

        private static void PropagateCorpusStrokesAndShapes()
        {
            // Lặp 3 vòng để lan truyền số nét từ các chữ cơ sở lên các chữ ghép phức hợp
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var kvp in CharToExpandedIds)
                {
                    int cp = kvp.Key;
                    if (StrokeCountByCp.ContainsKey(cp)) continue;

                    string ids = kvp.Value;
                    int sumStrokes = 0;
                    bool allKnown = true;
                    for (int i = 0; i < ids.Length; i++)
                    {
                        int tokenCp;
                        if (char.IsHighSurrogate(ids[i]) && i + 1 < ids.Length && char.IsLowSurrogate(ids[i + 1]))
                        {
                            tokenCp = char.ConvertToUtf32(ids[i], ids[i + 1]);
                            i++;
                        }
                        else
                        {
                            tokenCp = ids[i];
                        }

                        if (GetOperatorIdFromCp(tokenCp) != 0) continue;

                        byte st;
                        int normCp = NormalizeCorpusComponent(tokenCp, 0, 0);
                        if (StrokeCountByCp.TryGetValue(tokenCp, out st) || StrokeCountByCp.TryGetValue(normCp, out st))
                        {
                            sumStrokes += st;
                        }
                        else
                        {
                            allKnown = false;
                            sumStrokes += 5;
                        }
                    }

                    if (allKnown || pass == 2)
                    {
                        StrokeCountByCp[cp] = (byte)Math.Max(1, Math.Min(48, sumStrokes));
                    }
                }
            }
        }

        private static void BuildPositionalAndMorphologicalIndices()
        {
            PositionalBuckets.Clear();
            MorphologicalBuckets.Clear();
            SubIdsHashToCodepoint.Clear();
            ExactBinaryPairsInCorpus.Clear();

            // PASS 1: Thu thập toàn bộ khóa băm SubIdsHashToCodepoint trên 98.000 chữ trước
            // (kể cả bí danh tương đương hình học ⿻ <-> ⿱ cho các cấu trúc như 未/末 ⿻一木 <-> ⿱一木, 本 ⿻木一 <-> ⿱木一)
            foreach (var kvp in CharToExpandedIds)
            {
                int exemplarCp = kvp.Key;
                string ids = kvp.Value;
                if (string.IsNullOrEmpty(ids) || ids.Length < 3) continue;

                var tokens = DecodeCodePointsFast(ids);
                if (tokens.Count < 3) continue;

                byte rootOp = GetOperatorIdFromCp(tokens[0]);
                if (rootOp == 0) continue;

                int arity = GetOperatorArityById(rootOp);
                if (arity < 2) continue;

                int cursor = 1;
                int c0Cp = 0, c1Cp = 0, c2Cp = 0;
                byte c0SubOp = 0, c1SubOp = 0;
                ulong h0 = 0, h1 = 0, h2 = 0;

                if (!TryReadSubtreeInfo(tokens, ref cursor, rootOp, 0, out c0Cp, out c0SubOp, out h0)) continue;
                if (!TryReadSubtreeInfo(tokens, ref cursor, rootOp, 1, out c1Cp, out c1SubOp, out h1)) continue;
                if (arity == 3)
                {
                    byte dummySubOp;
                    if (!TryReadSubtreeInfo(tokens, ref cursor, rootOp, 2, out c2Cp, out dummySubOp, out h2)) continue;
                }

                if (cursor != tokens.Count) continue;

                ulong treeHash = ComputeSubIdsFnv1a(rootOp, h0, h1, h2);
                int existingCp;
                if (!SubIdsHashToCodepoint.TryGetValue(treeHash, out existingCp) || GetTierPriority(exemplarCp) < GetTierPriority(existingCp))
                {
                    SubIdsHashToCodepoint[treeHash] = exemplarCp;
                }

                // Bí danh cấu trúc: Trong All HAN IDS.TXT, các chữ như 未/末 (⿻一木), 本 (⿻木一) dùng toán tử ⿻ (opId=12),
                // nhưng khi người dùng viết công thức tùy chỉnh thường dùng ⿱ (opId=2: ⿱一木, ⿱木一).
                // Đăng ký thêm khóa băm tương đương ⿱ (opId=2) nếu chưa có chữ tường minh chiếm giữ.
                if (rootOp == 12 && arity == 2)
                {
                    ulong aliasVerticalHash = ComputeSubIdsFnv1a(2, h0, h1, 0);
                    int existingAliasCp;
                    if (!SubIdsHashToCodepoint.TryGetValue(aliasVerticalHash, out existingAliasCp) ||
                        GetTierPriority(exemplarCp) < GetTierPriority(existingAliasCp))
                    {
                        SubIdsHashToCodepoint[aliasVerticalHash] = exemplarCp;
                    }
                }
            }

            // PASS 2: Xây dựng PositionalBuckets (Tier 1 & 2) và MorphologicalBuckets (Tier 3)
            // khi SubIdsHashToCodepoint đã hoàn chỉnh 100% để giải được mọi nhánh con lồng nhau.
            foreach (var kvp in CharToExpandedIds)
            {
                int exemplarCp = kvp.Key;
                string ids = kvp.Value;
                if (string.IsNullOrEmpty(ids) || ids.Length < 3) continue;

                var tokens = DecodeCodePointsFast(ids);
                if (tokens.Count < 3) continue;

                byte rootOp = GetOperatorIdFromCp(tokens[0]);
                if (rootOp == 0) continue;

                int arity = GetOperatorArityById(rootOp);
                if (arity < 2) continue;

                // Phân tách các nhánh con trực tiếp của toán tử gốc
                int cursor = 1;
                int c0Cp = 0, c1Cp = 0, c2Cp = 0;
                byte c0SubOp = 0, c1SubOp = 0;
                ulong h0 = 0, h1 = 0, h2 = 0;

                if (!TryReadSubtreeInfo(tokens, ref cursor, rootOp, 0, out c0Cp, out c0SubOp, out h0)) continue;
                if (!TryReadSubtreeInfo(tokens, ref cursor, rootOp, 1, out c1Cp, out c1SubOp, out h1)) continue;
                if (arity == 3)
                {
                    byte dummySubOp;
                    if (!TryReadSubtreeInfo(tokens, ref cursor, rootOp, 2, out c2Cp, out dummySubOp, out h2)) continue;
                }

                if (cursor != tokens.Count) continue; // Bỏ qua nếu công thức con không khớp hoàn toàn

                // Cập nhật lại khóa tra ngược Sub-IDS -> CodePoint với h0, h1 đã phân giải sâu
                ulong treeHash = ComputeSubIdsFnv1a(rootOp, h0, h1, h2);
                int existingCp;
                if (!SubIdsHashToCodepoint.TryGetValue(treeHash, out existingCp) || GetTierPriority(exemplarCp) < GetTierPriority(existingCp))
                {
                    SubIdsHashToCodepoint[treeHash] = exemplarCp;
                }

                // Lưu cặp nhị phân nguyên bản để kiểm tra ngoại lệ từ vựng (như 災 ⿱巛火, 焚 ⿱林火)
                if (arity == 2 && c0Cp > 0 && c1Cp > 0)
                {
                    ExactBinaryPairsInCorpus.Add(PackExactBinaryPair(rootOp, tokens[1], tokens[2]));
                }

                // Khôi phục toán tử nội tại (Intrinsic Sub-Operator) cho các chữ con đơn lẻ nhưng bản chất là chữ ghép (như 圭=⿱土土 trong 娃=⿰女圭, 呆=⿱口木 trong 保=⿰亻呆)
                byte effC0SubOp = c0SubOp;
                byte effC1SubOp = c1SubOp;
                if (effC0SubOp == 0 && c0Cp > 0 && GetStrokeCount(c0Cp) >= 5)
                {
                    string sub0;
                    if (CharToExpandedIds.TryGetValue(c0Cp, out sub0) && !string.IsNullOrEmpty(sub0))
                    {
                        effC0SubOp = GetOperatorIdFromCp(GetFirstCodePoint(sub0));
                    }
                }
                if (effC1SubOp == 0 && c1Cp > 0 && GetStrokeCount(c1Cp) >= 5)
                {
                    string sub1;
                    if (CharToExpandedIds.TryGetValue(c1Cp, out sub1) && !string.IsNullOrEmpty(sub1))
                    {
                        effC1SubOp = GetOperatorIdFromCp(GetFirstCodePoint(sub1));
                    }
                }

                var rec = new CorpusExemplarRecord
                {
                    CodePoint = exemplarCp,
                    OpId = rootOp,
                    C0Cp = c0Cp,
                    C1Cp = c1Cp,
                    C2Cp = c2Cp,
                    C0SubOp = effC0SubOp,
                    C1SubOp = effC1SubOp,
                    C0Strokes = GetStrokeCount(c0Cp),
                    C1Strokes = GetStrokeCount(c1Cp),
                    C2Strokes = arity == 3 ? GetStrokeCount(c2Cp) : (byte)0,
                    C0Shape = GetShapeClass(c0Cp, effC0SubOp),
                    C1Shape = GetShapeClass(c1Cp, effC1SubOp),
                    Tier = ClassifyUnicodeTier(exemplarCp)
                };

                // Đưa vào PositionalBuckets cho từng slot
                if (c0Cp > 0) AddToPositionalBucket(PackPositionalKey(rootOp, 0, c0Cp), rec);
                if (c1Cp > 0) AddToPositionalBucket(PackPositionalKey(rootOp, 1, c1Cp), rec);
                if (arity == 3 && c2Cp > 0) AddToPositionalBucket(PackPositionalKey(rootOp, 2, c2Cp), rec);

                // Giải nén liên kết 1 cấp (Associative Structural Unfolding):
                // Trong All HAN IDS.TXT, hầu hết chữ 3 cột/3 tầng chuẩn BMP như 樹 (U+6A39 ⿰木尌, với 尌 = ⿰壴寸),
                // 树 (U+6811 ⿰木对), 湖 (U+6E56 ⿰氵胡), 藍 (U+85CD ⿱艹監) được mã hóa nhị phân lồng nhau,
                // trong khi người dùng và Preset thường nhập dạng phẳng ⿲ABC (opId=3) hoặc ⿳ABC (opId=4).
                if (arity == 2 && (rootOp == 1 || rootOp == 2))
                {
                    byte ternaryOp = (rootOp == 1) ? (byte)3 : (byte)4; // 1(⿰)->3(⿲), 2(⿱)->4(⿳)
                    int subLeft, subRight;
                    if (c0Cp > 0 && c1Cp > 0 && TryGetBinaryDecompositionOfChar(c1Cp, rootOp, out subLeft, out subRight))
                    {
                        var ternRec = new CorpusExemplarRecord
                        {
                            CodePoint = exemplarCp,
                            OpId = ternaryOp,
                            C0Cp = NormalizeCorpusComponent(c0Cp, ternaryOp, 0),
                            C1Cp = NormalizeCorpusComponent(subLeft, ternaryOp, 1),
                            C2Cp = NormalizeCorpusComponent(subRight, ternaryOp, 2),
                            C0Strokes = GetStrokeCount(c0Cp),
                            C1Strokes = GetStrokeCount(subLeft),
                            C2Strokes = GetStrokeCount(subRight),
                            C0Shape = GetShapeClass(c0Cp, 0),
                            C1Shape = GetShapeClass(subLeft, 0),
                            Tier = rec.Tier
                        };
                        AddToPositionalBucket(PackPositionalKey(ternaryOp, 0, ternRec.C0Cp), ternRec);
                        AddToPositionalBucket(PackPositionalKey(ternaryOp, 1, ternRec.C1Cp), ternRec);
                        AddToPositionalBucket(PackPositionalKey(ternaryOp, 2, ternRec.C2Cp), ternRec);

                        ulong ternHash = ComputeSubIdsFnv1a(
                            ternaryOp,
                            ComputeLeafFnv1a(ternRec.C0Cp),
                            ComputeLeafFnv1a(ternRec.C1Cp),
                            ComputeLeafFnv1a(ternRec.C2Cp));
                        int existTernCp;
                        if (!SubIdsHashToCodepoint.TryGetValue(ternHash, out existTernCp) || GetTierPriority(exemplarCp) < GetTierPriority(existTernCp))
                        {
                            SubIdsHashToCodepoint[ternHash] = exemplarCp;
                        }
                    }
                    else if (c0Cp > 0 && c1Cp > 0 && TryGetBinaryDecompositionOfChar(c0Cp, rootOp, out subLeft, out subRight))
                    {
                        var ternRec = new CorpusExemplarRecord
                        {
                            CodePoint = exemplarCp,
                            OpId = ternaryOp,
                            C0Cp = NormalizeCorpusComponent(subLeft, ternaryOp, 0),
                            C1Cp = NormalizeCorpusComponent(subRight, ternaryOp, 1),
                            C2Cp = NormalizeCorpusComponent(c1Cp, ternaryOp, 2),
                            C0Strokes = GetStrokeCount(subLeft),
                            C1Strokes = GetStrokeCount(subRight),
                            C2Strokes = GetStrokeCount(c1Cp),
                            C0Shape = GetShapeClass(subLeft, 0),
                            C1Shape = GetShapeClass(subRight, 0),
                            Tier = rec.Tier
                        };
                        AddToPositionalBucket(PackPositionalKey(ternaryOp, 0, ternRec.C0Cp), ternRec);
                        AddToPositionalBucket(PackPositionalKey(ternaryOp, 1, ternRec.C1Cp), ternRec);
                        AddToPositionalBucket(PackPositionalKey(ternaryOp, 2, ternRec.C2Cp), ternRec);
                    }
                }

                // Đưa vào MorphologicalBuckets (Tier 3) cho các chữ BMP và Ext-A/B tiêu biểu
                if (rec.Tier == UnicodeTier.Bmp || rec.Tier == UnicodeTier.ExtA)
                {
                    AddToMorphBucket(PackMorphKey(rootOp, 0, rec.C0Shape, GetStrokeBand(rec.C0Strokes)), rec);
                    AddToMorphBucket(PackMorphKey(rootOp, 1, rec.C1Shape, GetStrokeBand(rec.C1Strokes)), rec);
                }
            }
        }

        private static bool TryGetBinaryDecompositionOfChar(int cp, byte expectedOpId, out int child0Cp, out int child1Cp)
        {
            child0Cp = 0;
            child1Cp = 0;
            string subIds;
            if (!CharToExpandedIds.TryGetValue(cp, out subIds) || string.IsNullOrEmpty(subIds))
                return false;

            var toks = DecodeCodePointsFast(subIds);
            if (toks.Count != 3) return false;
            if (GetOperatorIdFromCp(toks[0]) != expectedOpId) return false;
            if (GetOperatorIdFromCp(toks[1]) != 0 || GetOperatorIdFromCp(toks[2]) != 0) return false;

            child0Cp = toks[1];
            child1Cp = toks[2];
            return true;
        }

        private static bool TryReadSubtreeInfo(
            List<int> tokens,
            ref int cursor,
            byte parentOp,
            int slot,
            out int canonicalCp,
            out byte subOp,
            out ulong subtreeHash)
        {
            canonicalCp = 0;
            subOp = 0;
            subtreeHash = 0;
            if (cursor >= tokens.Count) return false;

            int tok = tokens[cursor++];
            byte op = GetOperatorIdFromCp(tok);
            if (op == 0)
            {
                canonicalCp = NormalizeCorpusComponent(tok, parentOp, slot);
                subtreeHash = ComputeLeafFnv1a(canonicalCp);
                return true;
            }

            subOp = op;
            int arity = GetOperatorArityById(op);
            int subC0 = 0, subC1 = 0, subC2 = 0;
            byte subOp0 = 0, subOp1 = 0, subOp2 = 0;
            ulong h0 = 0, h1 = 0, h2 = 0;

            if (arity >= 1 && !TryReadSubtreeInfo(tokens, ref cursor, op, 0, out subC0, out subOp0, out h0)) return false;
            if (arity >= 2 && !TryReadSubtreeInfo(tokens, ref cursor, op, 1, out subC1, out subOp1, out h1)) return false;
            if (arity >= 3 && !TryReadSubtreeInfo(tokens, ref cursor, op, 2, out subC2, out subOp2, out h2)) return false;

            subtreeHash = ComputeSubIdsFnv1a(op, h0, h1, h2);
            int resolvedCp;
            if (SubIdsHashToCodepoint.TryGetValue(subtreeHash, out resolvedCp))
            {
                canonicalCp = NormalizeCorpusComponent(resolvedCp, parentOp, slot);
            }
            return true;
        }

        private static void AddToPositionalBucket(ulong key, CorpusExemplarRecord rec)
        {
            List<CorpusExemplarRecord> list;
            if (!PositionalBuckets.TryGetValue(key, out list))
            {
                list = new List<CorpusExemplarRecord>(16);
                PositionalBuckets[key] = list;
            }

            // Giữ tối đa 192 mẫu đa dạng mỗi bucket (ưu tiên BMP trước, đảm bảo không rớt các chữ thông dụng ở giữa bảng mã)
            if (list.Count < 192)
            {
                list.Add(rec);
            }
            else if (rec.Tier == UnicodeTier.Bmp)
            {
                // Thay thế một mẫu ExtB hoặc ExtA ở cuối nếu mẫu mới thuộc BMP
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Tier != UnicodeTier.Bmp)
                    {
                        list[i] = rec;
                        break;
                    }
                }
            }
        }

        private static void AddToMorphBucket(uint key, CorpusExemplarRecord rec)
        {
            List<CorpusExemplarRecord> list;
            if (!MorphologicalBuckets.TryGetValue(key, out list))
            {
                list = new List<CorpusExemplarRecord>(32);
                MorphologicalBuckets[key] = list;
            }

            if (list.Count >= 96) return;

            // Đảm bảo tính đa dạng bộ thủ: không để một bộ thủ đầu bảng Unicode (như 亻 U+4EBB)
            // độc chiếm toàn bộ MorphologicalBucket! Mỗi bộ thủ C0Cp chỉ xuất hiện tối đa 2 lần trong bucket.
            int sameRadicalCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].C0Cp == rec.C0Cp && rec.C0Cp > 0)
                {
                    sameRadicalCount++;
                    if (sameRadicalCount >= 2) return;
                }
            }

            list.Add(rec);
        }

        private static int GetTierPriority(int cp)
        {
            if (cp >= 0x4E00 && cp <= 0x9FFF) return 0; // BMP Unified
            if (cp >= 0x3400 && cp <= 0x4DBF) return 1; // Ext-A
            return 2;                                   // Ext-B..I
        }

        public static UnicodeTier ClassifyUnicodeTier(int cp)
        {
            if (cp >= 0x20000) return UnicodeTier.ExtB;
            if (cp >= 0x3400 && cp <= 0x4DBF) return UnicodeTier.ExtA;
            return UnicodeTier.Bmp;
        }

        public static int GetStrokeBand(int strokes)
        {
            if (strokes <= 3) return 0;
            if (strokes <= 7) return 1;
            if (strokes <= 12) return 2;
            return 3;
        }

        // =========================================================================
        // 5. TRUY VẤN TRA NGƯỢC SUB-IDS & KIỂM TRA NGOẠI LỆ TỪ VỰNG
        // =========================================================================
        public static int ResolveSubIdsToKnownCodePoint(ulong canonicalSubtreeHash)
        {
            EnsureInitialized();
            if (_ablationMode) return 0;
            int cp;
            if (SubIdsHashToCodepoint.TryGetValue(canonicalSubtreeHash, out cp))
                return cp;
            return 0;
        }

        public static bool HasExactBinaryPairInCorpus(string op, int rawC0Cp, int rawC1Cp)
        {
            EnsureInitialized();
            byte opId = GetOperatorId(op);
            if (opId == 0) return false;
            return ExactBinaryPairsInCorpus.Contains(PackExactBinaryPair(opId, rawC0Cp, rawC1Cp));
        }

        public static string GetExpandedIdsForCodePoint(int cp)
        {
            EnsureInitialized();
            string ids;
            if (CharToExpandedIds.TryGetValue(cp, out ids)) return ids;
            return null;
        }

        public static List<int> SearchCodePointsByIds(string query, int limit = 150)
        {
            EnsureInitialized();
            var results = new List<int>();
            if (string.IsNullOrEmpty(query)) return results;

            string q = query.Trim();
            if (q.StartsWith("ids:", StringComparison.OrdinalIgnoreCase))
                q = q.Substring(4).Trim();

            if (string.IsNullOrEmpty(q)) return results;

            // 1. Exact match in IdsToCodePoints
            List<int> exactList;
            if (IdsToCodePoints.TryGetValue(q, out exactList))
            {
                foreach (int cp in exactList)
                {
                    results.Add(cp);
                    if (results.Count >= limit) return results;
                }
            }

            // 2. Substring / partial match in CharToExpandedIds
            foreach (var kvp in CharToExpandedIds)
            {
                if (results.Contains(kvp.Key)) continue;
                if (kvp.Value.IndexOf(q, StringComparison.Ordinal) >= 0)
                {
                    results.Add(kvp.Key);
                    if (results.Count >= limit) break;
                }
            }
            return results;
        }

        // =========================================================================
        // 6. TRUY VẤN ĐỐI CHIẾU 4 TẦNG (STAGE 3A: 4-TIER CONTEXTUAL MATCHING)
        // =========================================================================
        public static List<CorpusMatchCandidate> QueryContextualExemplars(
            string op,
            int slotIndex,
            int componentCp,
            ShapeClass16 componentShape,
            int componentStrokes,
            byte siblingSubOp,
            ShapeClass16 siblingShape,
            int siblingStrokes,
            IGlyphSource glyphSource,
            out UnicodeTier maxTierReached)
        {
            return QueryContextualExemplars(
                op,
                slotIndex,
                componentCp,
                0,
                componentShape,
                componentStrokes,
                0,
                siblingSubOp,
                siblingShape,
                siblingStrokes,
                glyphSource,
                out maxTierReached);
        }

        public static List<CorpusMatchCandidate> QueryContextualExemplars(
            string op,
            int slotIndex,
            int componentCp,
            byte componentSubOp,
            ShapeClass16 componentShape,
            int componentStrokes,
            int siblingCp,
            byte siblingSubOp,
            ShapeClass16 siblingShape,
            int siblingStrokes,
            IGlyphSource glyphSource,
            out UnicodeTier maxTierReached)
        {
            EnsureInitialized();
            maxTierReached = UnicodeTier.Bmp;
            var results = new List<CorpusMatchCandidate>();

            // Nếu đang ở chế độ Ablation (triệt tiêu hạt giống để kiểm thử Tier 4), trả về danh sách rỗng
            if (_ablationMode || ExemplarSeeds.AllSeeds.Count == 0)
            {
                return results;
            }

            byte opId = GetOperatorId(op);
            if (opId == 0) return results;

            string excludedChar = ExemplarSeeds.CurrentExcludedCharacter;
            int excludedCp = (!string.IsNullOrEmpty(excludedChar)) ? GetFirstCodePoint(excludedChar) : 0;

            int normCompCp = (componentCp > 0) ? NormalizeCorpusComponent(componentCp, opId, slotIndex) : 0;
            int sibSlot = (slotIndex == 0) ? 1 : 0;
            int normSibCp = (siblingCp > 0) ? NormalizeCorpusComponent(siblingCp, opId, sibSlot) : 0;

            float sigmaS = 3.0f;
            var seenCodepoints = new HashSet<int>();

            // TẦNG 1 & TẦNG 2: Tìm trong PositionalBuckets theo đúng (opId, slotIndex, normCompCp)
            if (normCompCp > 0)
            {
                ulong posKey = PackPositionalKey(opId, slotIndex, normCompCp);
                List<CorpusExemplarRecord> bucket;
                if (PositionalBuckets.TryGetValue(posKey, out bucket))
                {
                    foreach (var rec in bucket)
                    {
                        if (rec.CodePoint == excludedCp) continue;
                        if (seenCodepoints.Contains(rec.CodePoint)) continue;

                        int recSibCp = (slotIndex == 0) ? rec.C1Cp : rec.C0Cp;
                        int sibStrokes = (slotIndex == 0) ? rec.C1Strokes : rec.C0Strokes;
                        ShapeClass16 sibShp = (slotIndex == 0) ? rec.C1Shape : rec.C0Shape;
                        byte sibOp = (slotIndex == 0) ? rec.C1SubOp : rec.C0SubOp;

                        float dS = sibStrokes - siblingStrokes;
                        float wStroke = (float)Math.Exp(-(dS * dS) / (2.0f * sigmaS * sigmaS));

                        bool isExactSibling = (normSibCp > 0 && recSibCp == normSibCp);
                        bool isTier1 = isExactSibling || (siblingSubOp != 0 && sibOp == siblingSubOp) || (sibShp == siblingShape);
                        int matchedTier = isTier1 ? 1 : 2;
                        float tierWeight = isExactSibling ? 1.45f : (isTier1 ? 1.0f : 0.70f);

                        // Ưu tiên tuyệt đối chữ thuộc BMP trước, chỉ vượt rào sang ExtA -> ExtB khi BMP không đủ mẫu
                        float tierBonus = (rec.Tier == UnicodeTier.Bmp) ? 1.0f : (rec.Tier == UnicodeTier.ExtA ? 0.52f : 0.32f);

                        seenCodepoints.Add(rec.CodePoint);

                        results.Add(new CorpusMatchCandidate
                        {
                            Record = rec,
                            MatchedTier = matchedTier,
                            TierWeight = tierWeight,
                            StrokeSimilarity = wStroke,
                            CombinedWeight = tierWeight * wStroke * tierBonus
                        });
                    }
                }
            }

            // Bổ sung các hạt giống chuẩn từ ExemplarSeeds (được hưởng trọng số mẫu chuẩn 1.12x)
            var seedList = ExemplarSeeds.GetSeeds(op);
            foreach (var seed in seedList)
            {
                if (seed.CodePoint == excludedCp) continue;
                if (seed.Components != null && slotIndex >= 0 && slotIndex < seed.Components.Length)
                {
                    int seedPartCp = NormalizeCorpusComponent(GetFirstCodePoint(seed.Components[slotIndex]), opId, slotIndex);
                    if (normCompCp > 0 && seedPartCp == normCompCp)
                    {
                        int sIdx = (slotIndex == 0 && seed.Components.Length > 1) ? 1 : 0;
                        int sibCp = GetFirstCodePoint(seed.Components[sIdx]);
                        int sibSt = GetStrokeCount(sibCp);
                        float dS = sibSt - siblingStrokes;
                        float wStroke = (float)Math.Exp(-(dS * dS) / (2.0f * sigmaS * sigmaS));
                        bool exactSib = (normSibCp > 0 && NormalizeCorpusComponent(sibCp, opId, sIdx) == normSibCp);

                        if (seenCodepoints.Contains(seed.CodePoint))
                        {
                            // Nâng trọng số cho mẫu chuẩn nếu đã có trong danh sách
                            for (int k = 0; k < results.Count; k++)
                            {
                                if (results[k].Record.CodePoint == seed.CodePoint)
                                {
                                    results[k].CombinedWeight *= (exactSib ? 1.35f : 1.15f);
                                    break;
                                }
                            }
                            continue;
                        }

                        var rec = new CorpusExemplarRecord
                        {
                            CodePoint = seed.CodePoint,
                            OpId = opId,
                            C0Cp = GetFirstCodePoint(seed.Components[0]),
                            C1Cp = seed.Components.Length > 1 ? GetFirstCodePoint(seed.Components[1]) : 0,
                            C0Strokes = GetStrokeCount(GetFirstCodePoint(seed.Components[0])),
                            C1Strokes = seed.Components.Length > 1 ? GetStrokeCount(GetFirstCodePoint(seed.Components[1])) : (byte)5,
                            Tier = seed.Tier
                        };

                        seenCodepoints.Add(seed.CodePoint);

                        results.Add(new CorpusMatchCandidate
                        {
                            Record = rec,
                            MatchedTier = 1,
                            TierWeight = exactSib ? 1.45f : 1.12f,
                            StrokeSimilarity = wStroke,
                            CombinedWeight = (exactSib ? 1.45f : 1.12f) * wStroke
                        });
                    }
                }
            }

            // TẦNG 1.5 (Sibling-Anchored Contextual Search):
            // Khi nhánh hiện tại là một cụm phức hợp tự chế (như ⿱天土 trong ⿰女⿱天土) chưa từng xuất hiện ở BMP,
            // thay vì rơi thẳng xuống MorphologicalBuckets (lấy nhầm chữ bộ 亻), ta tra các chữ BMP trong kho 98k
            // có CÙNG BỘ THỦ ANH EM (ví dụ cùng bộ 女 ở slot 0) và có nhánh hiện tại cùng cấu trúc con (ví dụ cùng là ⿱ ~7 nét, như 娃 ⿰女圭, 姪 ⿰女至, 姥 ⿰女老)!
            int bmpCountSoFar = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Record.Tier == UnicodeTier.Bmp) bmpCountSoFar++;
            }

            if (bmpCountSoFar < 4 && normSibCp > 0 && (opId == 1 || opId == 2 || opId >= 5))
            {
                ulong sibPosKey = PackPositionalKey(opId, sibSlot, normSibCp);
                List<CorpusExemplarRecord> sibBucket;
                if (PositionalBuckets.TryGetValue(sibPosKey, out sibBucket))
                {
                    foreach (var rec in sibBucket)
                    {
                        if (rec.CodePoint == excludedCp || seenCodepoints.Contains(rec.CodePoint)) continue;
                        if (rec.Tier != UnicodeTier.Bmp) continue;

                        int targetStrokes = (slotIndex == 0) ? rec.C0Strokes : rec.C1Strokes;
                        ShapeClass16 targetShp = (slotIndex == 0) ? rec.C0Shape : rec.C1Shape;
                        byte targetSubOp = (slotIndex == 0) ? rec.C0SubOp : rec.C1SubOp;

                        bool structMatch = (componentSubOp != 0 && targetSubOp == componentSubOp) || (targetShp == componentShape);
                        if (!structMatch && Math.Abs(targetStrokes - componentStrokes) > 2) continue;

                        float dS = targetStrokes - componentStrokes;
                        float wStroke = (float)Math.Exp(-(dS * dS) / (2.0f * sigmaS * sigmaS));
                        float structBonus = (componentSubOp != 0 && targetSubOp == componentSubOp) ? 0.92f : 0.78f;

                        seenCodepoints.Add(rec.CodePoint);
                        results.Add(new CorpusMatchCandidate
                        {
                            Record = rec,
                            MatchedTier = 2,
                            TierWeight = structBonus,
                            StrokeSimilarity = wStroke,
                            CombinedWeight = structBonus * wStroke
                        });
                    }
                }
            }

            // TẦNG 3: Nếu Tầng 1 + Tầng 1.5 + Tầng 2 vẫn có ít hơn 5 mẫu, truy vấn sang MorphologicalBuckets (đa dạng bộ thủ)
            if (results.Count < 5)
            {
                uint morphKey = PackMorphKey(opId, slotIndex, componentShape, GetStrokeBand(componentStrokes));
                List<CorpusExemplarRecord> morphList;
                if (MorphologicalBuckets.TryGetValue(morphKey, out morphList))
                {
                    foreach (var rec in morphList)
                    {
                        if (rec.CodePoint == excludedCp || seenCodepoints.Contains(rec.CodePoint)) continue;
                        int sibStrokes = (slotIndex == 0) ? rec.C1Strokes : rec.C0Strokes;
                        float dS = sibStrokes - siblingStrokes;
                        float wStroke = (float)Math.Exp(-(dS * dS) / (2.0f * sigmaS * sigmaS));

                        seenCodepoints.Add(rec.CodePoint);

                        results.Add(new CorpusMatchCandidate
                        {
                            Record = rec,
                            MatchedTier = 3,
                            TierWeight = 0.45f,
                            StrokeSimilarity = wStroke,
                            CombinedWeight = 0.45f * wStroke
                        });

                        if (results.Count >= 20) break;
                    }
                }
            }

            // Sắp xếp giảm dần theo CombinedWeight và lọc lấy tối đa 10 chữ mẫu thực sự có glyph trong font
            results.Sort((a, b) => b.CombinedWeight.CompareTo(a.CombinedWeight));

            var filtered = new List<CorpusMatchCandidate>(10);
            foreach (var cand in results)
            {
                if (glyphSource != null)
                {
                    string ch = char.ConvertFromUtf32(cand.Record.CodePoint);
                    if (!glyphSource.HasGlyph(ch)) continue;
                }
                filtered.Add(cand);
                if (cand.Record.Tier > maxTierReached) maxTierReached = cand.Record.Tier;
                if (filtered.Count >= 10) break;
            }

            return filtered;
        }

        public static int GetFirstCodePoint(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (char.IsHighSurrogate(s[0]) && s.Length >= 2 && char.IsLowSurrogate(s[1]))
                return char.ConvertToUtf32(s[0], s[1]);
            return s[0];
        }

        // =========================================================================
        // 7. PHÂN LOẠI 16 LỚP HÌNH THÁI (SHAPECLASS16) & SỐ NÉT CHUẨN
        // =========================================================================
        public static byte GetStrokeCount(int cp)
        {
            if (cp <= 0) return 5;
            EnsureInitialized();
            byte st;
            if (StrokeCountByCp.TryGetValue(cp, out st)) return st;
            int folded = FoldKangxiRadicalCodePoint(cp);
            if (StrokeCountByCp.TryGetValue(folded, out st)) return st;
            return 6;
        }

        public static ShapeClass16 GetShapeClass(int cp, byte subOp = 0, MorphologicalProfile profile = null)
        {
            EnsureInitialized();
            if (cp > 0)
            {
                ShapeClass16 sc;
                if (ShapeClassByCp.TryGetValue(cp, out sc)) return sc;
                int folded = FoldKangxiRadicalCodePoint(cp);
                if (ShapeClassByCp.TryGetValue(folded, out sc)) return sc;
            }

            // Suy luận từ subOp nếu là nút phức hợp
            if (subOp == 5) return ShapeClass16.FullRing;       // ⿴
            if (subOp == 6) return ShapeClass16.TopBracket;     // ⿵
            if (subOp == 7) return ShapeClass16.TopCavity;      // ⿶
            if (subOp == 8) return ShapeClass16.LeftBracket;    // ⿷
            if (subOp == 9) return ShapeClass16.DiagTlDrape;    // ⿸
            if (subOp == 10) return ShapeClass16.DiagTrHook;    // ⿹
            if (subOp == 11) return ShapeClass16.DiagBlTray;    // ⿺
            if (subOp == 12) return ShapeClass16.SymmetricCross;// ⿻

            // Suy luận từ MorphologicalProfile thực đo
            if (profile != null)
            {
                if (profile.Aspect >= 2.2f) return ShapeClass16.FlatBar;
                if (profile.Aspect <= 0.42f) return ShapeClass16.ThinPost;
                if (profile.Aspect < 0.62f) return ShapeClass16.NarrowPillar;
                if (profile.IsRoof) return ShapeClass16.WideRoof;
                if (profile.IsWideBase) return ShapeClass16.WideBase;
            }

            return ShapeClass16.SquareBlock;
        }

        /// <summary>
        /// Hàm chuyển tiếp đóng (Closed Algebra Transition) từ toán tử và các lớp hình thái con sang lớp hình thái nút cha.
        /// </summary>
        public static ShapeClass16 SynthesizeCompositeShapeClass(string op, ShapeClass16 c0, ShapeClass16 c1)
        {
            byte opId = GetOperatorId(op);
            switch (opId)
            {
                case 1: // ⿰
                case 3: // ⿲
                    return ShapeClass16.SquareBlock;
                case 2: // ⿱
                case 4: // ⿳
                    if (c0 == ShapeClass16.WideRoof) return ShapeClass16.WideRoof;
                    if (c1 == ShapeClass16.WideBase) return ShapeClass16.WideBase;
                    if (c1 == ShapeClass16.BotCavity) return ShapeClass16.BotCavity;
                    return ShapeClass16.SquareBlock;
                case 5: // ⿴
                    return ShapeClass16.FullRing;
                case 6: // ⿵
                    return ShapeClass16.TopBracket;
                case 7: // ⿶
                    return ShapeClass16.TopCavity;
                case 8: // ⿷
                    return ShapeClass16.LeftBracket;
                case 9: // ⿸
                    return ShapeClass16.DiagTlDrape;
                case 10: // ⿹
                    return ShapeClass16.DiagTrHook;
                case 11: // ⿺
                    return ShapeClass16.DiagBlTray;
                case 12: // ⿻
                    return ShapeClass16.SymmetricCross;
                default:
                    return ShapeClass16.SquareBlock;
            }
        }

        private static void SeedBaseStrokeAndShapeTables()
        {
            StrokeCountByCp.Clear();
            ShapeClassByCp.Clear();

            // 1. Nét cơ bản & Bộ thủ 1-2 nét
            int[] s1 = new int[] { 0x4E00, 0x4E28, 0x4E36, 0x4E3F, 0x4E40, 0x4E41, 0x4E59, 0x4E5A, 0x4E5B, 0x4E85, 0x31C0, 0x31C1, 0x31C2, 0x31C3, 0x31C4, 0x31C5, 0x31C6, 0x31C7, 0x31C8, 0x31C9, 0x31CA, 0x31CB, 0x31CC, 0x31CD, 0x31CE, 0x31CF, 0x31D0, 0x31D1, 0x31D2, 0x31D3, 0x31D4, 0x31D5, 0x31D6, 0x31D7, 0x31D8, 0x31D9, 0x31DA, 0x31DB, 0x31DC, 0x31DD, 0x31DE, 0x31DF, 0x31E0, 0x31E1, 0x31E2, 0x31E3 };
            foreach (int cp in s1) StrokeCountByCp[cp] = 1;

            int[] s2 = new int[] { 0x4E8C, 0x4EA0, 0x4EBA, 0x4EBB, 0x513F, 0x5165, 0x516B, 0x5182, 0x5196, 0x51AB, 0x51E0, 0x51F5, 0x5200, 0x5202, 0x529B, 0x52F9, 0x5315, 0x531A, 0x5338, 0x5341, 0x535C, 0x5369, 0x5382, 0x53B6, 0x53C8, 0x4E01, 0x4E03, 0x4E42, 0x4E5D, 0x4E86, 0x2E81, 0x2E85, 0x2E86, 0x2E87, 0x2E88, 0x2E89, 0x2E8A, 0x2E8B, 0x8EC0 };
            foreach (int cp in s2) StrokeCountByCp[cp] = 2;

            int[] s3 = new int[] { 0x53E3, 0x56D7, 0x571F, 0x58EB, 0x5902, 0x590A, 0x5915, 0x5927, 0x5973, 0x5B50, 0x5B80, 0x5BF8, 0x5C0F, 0x5C22, 0x5C38, 0x5C6E, 0x5C71, 0x5DDB, 0x5DE5, 0x5DF1, 0x5DF3, 0x5DFE, 0x5E72, 0x5E7A, 0x5E7F, 0x5EF4, 0x5EFE, 0x5F0B, 0x5F13, 0x5F50, 0x5F61, 0x5F73, 0x6C35, 0x5FC4, 0x624C, 0x72AD, 0x8279, 0x8FB6, 0x961D, 0x4E09, 0x4E0A, 0x4E0B, 0x4E5F, 0x4E8D, 0x7E9F, 0x9963, 0x95E8 };
            foreach (int cp in s3) StrokeCountByCp[cp] = 3;

            int[] s4 = new int[] { 0x5FC3, 0x6208, 0x6236, 0x6237, 0x624B, 0x652F, 0x6534, 0x6535, 0x6587, 0x6597, 0x65A4, 0x65B9, 0x65E0, 0x65E5, 0x66F0, 0x6708, 0x6728, 0x6B20, 0x6B62, 0x6B79, 0x6BB3, 0x6BCB, 0x6BD4, 0x6BDB, 0x6C0F, 0x6C14, 0x6C34, 0x706B, 0x706C, 0x722A, 0x722B, 0x7236, 0x723B, 0x723F, 0x7247, 0x7259, 0x725B, 0x725C, 0x72AC, 0x738B, 0x248E9, 0x793B, 0x2EBC, 0x4E0D, 0x4E2D, 0x4E91, 0x4ECA, 0x5143, 0x5929, 0x5C14, 0x6209 };
            foreach (int cp in s4) StrokeCountByCp[cp] = 4;

            int[] s5 = new int[] { 0x7384, 0x7389, 0x74DC, 0x74E6, 0x7518, 0x751F, 0x7528, 0x7530, 0x758B, 0x7592, 0x7676, 0x767D, 0x76AE, 0x76BF, 0x76EE, 0x77DB, 0x77E2, 0x77F3, 0x793A, 0x79B8, 0x79BE, 0x7A74, 0x7ACB, 0x8864, 0x7F52, 0x9485, 0x53EF, 0x53E4, 0x53EC, 0x672C, 0x672A, 0x672B, 0x7533, 0x4E14, 0x4E16 };
            foreach (int cp in s5) StrokeCountByCp[cp] = 5;

            int[] s6 = new int[] { 0x7AF9, 0x2EAE, 0x25AD7, 0x7C73, 0x7CF8, 0x7CF9, 0x7F36, 0x7F51, 0x7F8A, 0x7FBD, 0x8001, 0x800C, 0x8012, 0x8033, 0x807F, 0x8089, 0x81E3, 0x81EA, 0x81F3, 0x81FC, 0x820C, 0x821B, 0x821F, 0x826E, 0x8272, 0x8278, 0x864D, 0x866B, 0x8840, 0x884C, 0x8863, 0x897E, 0x540C, 0x65E9, 0x6731, 0x572D, 0x514C, 0x4F59 };
            foreach (int cp in s6) StrokeCountByCp[cp] = 6;

            int[] s7 = new int[] { 0x898B, 0x89D2, 0x8A00, 0x8A01, 0x8C37, 0x8C46, 0x8C55, 0x8C78, 0x8C9D, 0x8D64, 0x8D70, 0x8DB3, 0x2ECA, 0x8EAB, 0x8ECA, 0x8F9B, 0x8FB0, 0x8FB5, 0x9091, 0x9149, 0x91C6, 0x91CC, 0x543E, 0x6BCF, 0x752C, 0x826F };
            foreach (int cp in s7) StrokeCountByCp[cp] = 7;

            int[] s8 = new int[] { 0x91D1, 0x91D2, 0x9577, 0x9580, 0x961C, 0x96B6, 0x96B9, 0x96E8, 0x2ED7, 0x9751, 0x9752, 0x975E, 0x98E0, 0x6797, 0x5C1A, 0x6771, 0x5177 };
            foreach (int cp in s8) StrokeCountByCp[cp] = 8;

            int[] s9 = new int[] { 0x9762, 0x9769, 0x97CB, 0x97ED, 0x97F3, 0x9801, 0x98A8, 0x98DB, 0x98DF, 0x9996, 0x9999, 0x6625, 0x76F8, 0x6CC9, 0x80E1 };
            foreach (int cp in s9) StrokeCountByCp[cp] = 9;

            int[] s10 = new int[] { 0x99AC, 0x9AA8, 0x9AD8, 0x9ADF, 0x9B25, 0x9B2F, 0x9B32, 0x9B3C, 0x9B5A, 0x9CE5, 0x9E75, 0x9E7F, 0x9EA5, 0x9EBB, 0x9EC3, 0x9ECD, 0x9ED1, 0x9F8D, 0x9F9C };
            foreach (int cp in s10) StrokeCountByCp[cp] = 11;

            // 2. Phân loại 16 Lớp Hình Thái Đóng (ShapeClass16)
            int[] squareBlocks = new int[] { 0x53E3, 0x65E5, 0x66F0, 0x7530, 0x76EE, 0x767D, 0x77F3, 0x56DB, 0x7531, 0x7532, 0x7533 };
            foreach (int cp in squareBlocks) ShapeClassByCp[cp] = ShapeClass16.SquareBlock;

            int[] narrowPillars = new int[] { 0x4EBB, 0x6C35, 0x5FC4, 0x624C, 0x72AD, 0x793B, 0x8864, 0x961D, 0x5202, 0x5F73, 0x723F, 0x7247, 0x8EC0, 0x9485, 0x7E9F, 0x9963, 0x5369, 0x51AB };
            foreach (int cp in narrowPillars) ShapeClassByCp[cp] = ShapeClass16.NarrowPillar;

            int[] wideRoofs = new int[] { 0x5B80, 0x5196, 0x96E8, 0x2ED7, 0x7A74, 0x7AF9, 0x2EAE, 0x25AD7, 0x8279, 0x8278, 0x722B, 0x722A, 0x4EA0, 0x7F52, 0x7F51, 0x2EB2 };
            foreach (int cp in wideRoofs) ShapeClassByCp[cp] = ShapeClass16.WideRoof;

            int[] wideBases = new int[] { 0x76BF, 0x706C, 0x571F, 0x58EB, 0x5FC3, 0x6B62, 0x7ACB, 0x81F3, 0x738B, 0x4E0A, 0x65E6, 0x5FC3 };
            foreach (int cp in wideBases) ShapeClassByCp[cp] = ShapeClass16.WideBase;

            int[] topCavities = new int[] { 0x51F5, 0x5C71, 0x81FC };
            foreach (int cp in topCavities) ShapeClassByCp[cp] = ShapeClass16.TopCavity;

            int[] botCavities = new int[] { 0x4EBA, 0x516B, 0x5165, 0x5927, 0x5929, 0x7236, 0x6728, 0x79BE, 0x706B, 0x7F8A, 0x592B };
            foreach (int cp in botCavities) ShapeClassByCp[cp] = ShapeClass16.BotCavity;

            int[] leftBrackets = new int[] { 0x531A, 0x5338 };
            foreach (int cp in leftBrackets) ShapeClassByCp[cp] = ShapeClass16.LeftBracket;

            int[] topBrackets = new int[] { 0x5182, 0x540C, 0x9580, 0x95E8, 0x9B25 };
            foreach (int cp in topBrackets) ShapeClassByCp[cp] = ShapeClass16.TopBracket;

            int[] diagTlDrapes = new int[] { 0x5E7F, 0x5382, 0x5C38, 0x6236, 0x6237, 0x7592, 0x864D, 0x9EBB, 0x9E7F };
            foreach (int cp in diagTlDrapes) ShapeClassByCp[cp] = ShapeClass16.DiagTlDrape;

            int[] diagTrHooks = new int[] { 0x52F9, 0x6C14, 0x6208, 0x5F0B, 0x51E0, 0x98A8 };
            foreach (int cp in diagTrHooks) ShapeClassByCp[cp] = ShapeClass16.DiagTrHook;

            int[] diagBlTrays = new int[] { 0x8FB6, 0x8FB5, 0x5EF4, 0x8D70, 0x9B3C, 0x9EA5, 0x652F, 0x590A };
            foreach (int cp in diagBlTrays) ShapeClassByCp[cp] = ShapeClass16.DiagBlTray;

            ShapeClassByCp[0x56D7] = ShapeClass16.FullRing; // 囗

            int[] symCross = new int[] { 0x5341, 0x7C73, 0x4E42, 0x723B, 0x5DEB, 0x6771, 0x675F };
            foreach (int cp in symCross) ShapeClassByCp[cp] = ShapeClass16.SymmetricCross;

            int[] triUp = new int[] { 0x53B6, 0x5E7A, 0x4E91, 0x4F1A, 0x5408, 0x4EBC };
            foreach (int cp in triUp) ShapeClassByCp[cp] = ShapeClass16.TriangleUp;

            int[] flatBars = new int[] { 0x4E00, 0x4E8C, 0x4E09, 0x31D0 };
            foreach (int cp in flatBars) ShapeClassByCp[cp] = ShapeClass16.FlatBar;

            int[] thinPosts = new int[] { 0x4E28, 0x4E85, 0x4E3F, 0x31D1 };
            foreach (int cp in thinPosts) ShapeClassByCp[cp] = ShapeClass16.ThinPost;
        }

        private static void SeedDefaultMacros()
        {
            RawMacros.Clear();
            RawMacros[1] = "⿹⿺㇉一灬";
            RawMacros[2] = "⿱⺈𫩏";
            RawMacros[3] = "⿱⺈⿵冂人";
            RawMacros[4] = "⿱⿻⿰丨丨丷冖";
            RawMacros[5] = "⿱𠔼⿲丶丨丶";
            RawMacros[8] = "⿻⿱㇒一乚";
            RawMacros[9] = "⿻一曲";
            RawMacros[10] = "⿱𦥑同";
            RawMacros[11] = "⿱𦥑冖";
            RawMacros[13] = "⿻干丷";
            RawMacros[14] = "⿻土丷";
            RawMacros[15] = "⿱爪冖";
            RawMacros[16] = "⿻弋一";
            RawMacros[17] = "⿻戈一";
            RawMacros[18] = "⿱彑☍";
            RawMacros[19] = "⿻口十";
            RawMacros[22] = "⿺㇉一";
            RawMacros[26] = "⿻コ一";
            RawMacros[27] = "⿱⿰丨丨冖";
            RawMacros[28] = "⿻日乚";
            RawMacros[29] = "⿱一⿰丨丨";
            RawMacros[30] = "⿱艹冖";
            RawMacros[31] = "⿻𠃌一";
            RawMacros[32] = "⿻八土";
            RawMacros[36] = "⿱亠丷";
            RawMacros[39] = "⿱丿十";
            RawMacros[40] = "⿻巾丷";
            RawMacros[42] = "⿹𠃌一";
            RawMacros[44] = "⿻口丨";
            RawMacros[47] = "⿻日丨";
            RawMacros[48] = "⿻目乚";
            RawMacros[49] = "⿱丿丶";
            RawMacros[50] = "⿱丶丶";
            RawMacros[51] = "⿻口丷";
            RawMacros[52] = "⿱宀⿰丨丨";
            RawMacros[53] = "⿻廿一";
            RawMacros[55] = "⿻弋丶";
            RawMacros[58] = "⿰{39}丶";
            RawMacros[60] = "⿱止☍";
            RawMacros[61] = "⿱𠂉☍";
            RawMacros[62] = "⿻⺈丶";
            RawMacros[63] = "⿻尸一";
            RawMacros[64] = "⿻日丶";
            RawMacros[65] = "⿻戊一";
            RawMacros[68] = "⿱丷一";
            RawMacros[69] = "⿱𦥑一";
            RawMacros[73] = "⿱一⿻廿一";
            RawMacros[76] = "⿻口八";
            RawMacros[77] = "⿱𠂉丶";
            RawMacros[80] = "⿱穴☍";
            RawMacros[81] = "⿻大丷";
            RawMacros[82] = "⿻土丷";
            RawMacros[83] = "⿱宀八";
            RawMacros[85] = "⿻口乚";
            RawMacros[92] = "⿱丿一";
            RawMacros[93] = "⿻一乚";
            RawMacros[95] = "⿱𠂊一";
            RawMacros[100] = "⿱穴冖";
            RawMacros[103] = "⿻巾八";
            RawMacros[106] = "⿻丿丶";
            RawMacros[113] = "⿱丷冖";
            RawMacros[120] = "⿻弓⿱{50}匚";
            RawMacros[122] = "⿱丿十";
        }
    }
}
