using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class MorphologicalProfile
    {
        public float Sx { get; set; }           // Zero-crossing vertical stroke complexity
        public float Sy { get; set; }           // Zero-crossing horizontal stroke complexity
        public float Wx0 { get; set; }          // Median vertical stroke thickness (1000 units)
        public float Wy0 { get; set; }          // Median horizontal stroke thickness (1000 units)
        public int NHoles { get; set; }         // Number of internal closed white cavities
        public float MinHoleWidth { get; set; } // Minimum width of internal white gaps (1000 units)
        public float RhoWhite { get; set; }     // White-to-ink ratio
        public float WTop { get; set; }         // Span width ratio in top 20% [0..1]
        public float WMid { get; set; }         // Span width ratio in middle 40-60% [0..1]
        public float WBot { get; set; }         // Span width ratio in bottom 80-100% [0..1]
        public float Aspect { get; set; }       // Ink width / ink height
        public float Cx { get; set; }           // Horizontal Center of Mass [0..1]
        public float Cy { get; set; }           // Vertical Center of Mass [0..1]
        public RectangleF InkBBox { get; set; } // Tight bounding box in 1000 units
        public float[] TopSkyline { get; set; } // 64 distance samples from top [0..1]
        public float[] BottomSkyline { get; set; } // 64 distance samples from bottom [0..1]
        public float[] LeftSkyline { get; set; } // 64 distance samples from left [0..1]
        public float[] RightSkyline { get; set; } // 64 distance samples from right [0..1]

        /// <summary>
        /// Tâm quang học kết hợp: 55% tâm hình học + 45% trọng tâm khối lượng mực - dịch lên 2% thị giác.
        /// </summary>
        public PointF OpticalCentroidNormalized
        {
            get
            {
                float optX = 0.55f * 0.5f + 0.45f * Cx;
                float optY = 0.55f * 0.5f + 0.45f * Cy - 0.02f;
                return new PointF(
                    Math.Max(0.15f, Math.Min(0.85f, optX)),
                    Math.Max(0.15f, Math.Min(0.85f, optY)));
            }
        }

        public bool IsWideBase
        {
            get
            {
                if (Aspect >= 0.95f && WBot >= 0.75f) return true;
                if (WBot < 0.80f) return false;
                float minD = 1.0f, maxD = 0.0f;
                int valid = 0;
                for (int i = 8; i < 56; i++)
                {
                    if (BottomSkyline[i] < 0.90f)
                    {
                        valid++;
                        if (BottomSkyline[i] < minD) minD = BottomSkyline[i];
                        if (BottomSkyline[i] > maxD) maxD = BottomSkyline[i];
                    }
                }
                return valid > 20 && (maxD - minD) < 0.28f;
            }
        }

        public bool IsRoof
        {
            get
            {
                if (Aspect >= 1.35f && (WTop >= 0.55f || WMid >= 0.70f)) return true;
                if (WTop < 0.80f) return false;
                if (WMid >= WTop * 0.85f) return false;
                float minD = 1.0f, maxD = 0.0f;
                int valid = 0;
                for (int i = 8; i < 56; i++)
                {
                    if (TopSkyline[i] < 0.90f)
                    {
                        valid++;
                        if (TopSkyline[i] < minD) minD = TopSkyline[i];
                        if (TopSkyline[i] > maxD) maxD = TopSkyline[i];
                    }
                }
                return valid > 20 && (maxD - minD) < 0.28f;
            }
        }

        public bool IsNarrow
        {
            get { return Aspect < 0.55f; }
        }

        public MorphologicalProfile()
        {
            TopSkyline = new float[64];
            BottomSkyline = new float[64];
            LeftSkyline = new float[64];
            RightSkyline = new float[64];
            Aspect = 1.0f;
            WTop = 0.8f;
            WMid = 0.8f;
            WBot = 0.8f;
            Sx = 2.0f;
            Sy = 2.0f;
            Wx0 = 68.0f;
            Wy0 = 68.0f;
            Cx = 0.5f;
            Cy = 0.5f;
            MinHoleWidth = 40.0f;
        }
    }

    public class SanityResult
    {
        public bool Ok { get; set; }
        public string MessageVi { get; set; }
        public string MessageEn { get; set; }
        public int ErrorPos { get; set; }
    }

    public class BeautifyResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsAlreadyOptimal { get; set; }
        public float AestheticScore { get; set; }
        public List<string> ReportBulletPoints { get; set; }
        public Dictionary<string, NodeCustomTransform> BeautifiedTransforms { get; set; }

        public BeautifyResult()
        {
            ReportBulletPoints = new List<string>();
            BeautifiedTransforms = new Dictionary<string, NodeCustomTransform>();
            AestheticScore = 95.0f;
        }

        public string GetFormattedReport()
        {
            return string.Join("\r\n", ReportBulletPoints);
        }
    }

    public class DissectedExemplarMetrics
    {
        public ExemplarSeed Seed { get; set; }
        public RectangleF ExemplarOuterMargin { get; set; }
        public RectangleF PartRelativeBox { get; set; }
        public float RealSplitRatio { get; set; }
        public float RealInterlockDepth { get; set; }
        public float RealClearanceGap { get; set; }
        public float RealTx { get; set; }
        public float RealTy { get; set; }
        public float MatchScore { get; set; }
    }

    public class ConsensusBlueprint
    {
        public float ConsensusSplitRatio { get; set; }
        public float ConsensusClearanceGap { get; set; }
        public float TxStar { get; set; }
        public float TyStar { get; set; }
        public float TotalEvidenceWeight { get; set; }
        public float ShrinkageAlpha { get; set; }
        public Dictionary<string, RectangleF> PartRelativeBoxes { get; set; }

        public ConsensusBlueprint()
        {
            ConsensusSplitRatio = 0.5f;
            ConsensusClearanceGap = 26.0f;
            TxStar = 68.0f;
            TyStar = 68.0f;
            TotalEvidenceWeight = 0f;
            ShrinkageAlpha = 0f;
            PartRelativeBoxes = new Dictionary<string, RectangleF>();
        }
    }

    /// <summary>
    /// BEAUTIFUL IDS v2.0 ENGINE:
    /// Kiến trúc 5 Giai đoạn dựa trên Chỉ mục 98.000 chữ All HAN IDS.TXT + Co rút Empirical Bayes + Hình học Quang học Đệ quy.
    /// </summary>
    public static class IdsOpticalBeautifier
    {
        private static readonly Dictionary<string, MorphologicalProfile> ProfileCache = new Dictionary<string, MorphologicalProfile>();
        private static readonly ConcurrentDictionary<string, bool[,]> MaskCache = new ConcurrentDictionary<string, bool[,]>();
        private static readonly object CacheLock = new object();

        // ====================================================================================
        // GIAI ĐOẠN 1: GÁC CỔNG NGỮ PHÁP & BIẾN HÌNH BỘ THỦ NGỮ CẢNH (Stage 1)
        // ====================================================================================
        public static SanityResult Stage1_ValidateAstSanity(string input, IdsNode root, IGlyphSource glyphSource)
        {
            var res = new SanityResult { Ok = true };
            if (string.IsNullOrWhiteSpace(input) || root == null)
            {
                res.Ok = false;
                res.MessageVi = "Công thức IDS chưa hoàn chỉnh (chuỗi rỗng hoặc rỗng cây).";
                res.MessageEn = "Empty or unparseable IDS syntax tree.";
                return res;
            }

            if (IdsParserService.ContainsUnpairedSurrogate(input))
            {
                res.Ok = false;
                res.MessageVi = "Công thức IDS lỗi mã hóa UTF-16 (chứa surrogate lẻ).";
                res.MessageEn = "Invalid UTF-16 surrogate pair in IDS formula.";
                return res;
            }

            // 1. Kiểm tra placeholder □ hoặc thiếu thành phần con
            if (input.Contains("□") || (root != null && root.HasPlaceholders) || HasPlaceholderNodes(root))
            {
                res.Ok = false;
                res.MessageVi = "Công thức IDS chưa hoàn chỉnh (còn ô trống □ hoặc thiếu thành phần con).";
                res.MessageEn = "Incomplete IDS formula (contains placeholder □ or missing operands).";
                return res;
            }

            // 2. Kiểm tra ngữ pháp nghiêm ngặt bằng Parser không đệ quy (dư thừa token, sai arity, vượt MAX_AST_DEPTH = 32)
            string strictErr;
            int strictPos;
            var strictAst = IdsParserService.ParseIdsStrict(input, out strictErr, out strictPos);
            if (strictAst == null)
            {
                res.Ok = false;
                res.MessageVi = strictErr;
                res.MessageEn = strictErr;
                res.ErrorPos = strictPos;
                return res;
            }

            int tokenCount = CountTokensInTree(root);
            if (tokenCount > 32)
            {
                res.Ok = false;
                res.MessageVi = string.Format("Cây IDS quá sâu ({0} nút, vượt quá giới hạn an toàn 32).", tokenCount);
                res.MessageEn = string.Format("IDS tree exceeds complexity limit ({0} nodes > 32).", tokenCount);
                return res;
            }

            int hDepth = GetMaxAxisDepth(root, true, 0);
            int vDepth = GetMaxAxisDepth(root, false, 0);
            if (hDepth > 6 || vDepth > 6)
            {
                res.Ok = false;
                res.MessageVi = string.Format("Cây lồng trục quá sâu ({0} tầng trục ngang / {1} tầng trục dọc > 6).", hDepth, vDepth);
                res.MessageEn = string.Format("Axis recursion depth exceeds safety threshold (H={0}, V={1} > 6).", hDepth, vDepth);
                return res;
            }

            return res;
        }

        private static bool HasPlaceholderNodes(IdsNode node)
        {
            if (node == null) return false;
            if (!node.IsOperator)
            {
                if (node.Char == "□" || (node.Id != null && node.Id.StartsWith("ph_"))) return true;
            }
            if (node.Children != null)
            {
                foreach (var c in node.Children)
                {
                    if (HasPlaceholderNodes(c)) return true;
                }
            }
            return false;
        }

        private static int CountTokensInTree(IdsNode node)
        {
            if (node == null) return 0;
            int count = 1;
            if (node.Children != null)
            {
                foreach (var c in node.Children) count += CountTokensInTree(c);
            }
            return count;
        }

        private static int GetMaxAxisDepth(IdsNode node, bool isHorizontal, int currentConsecutive)
        {
            if (node == null || !node.IsOperator) return currentConsecutive;
            bool matches = isHorizontal
                ? (node.Operator == "⿰" || node.Operator == "⿲")
                : (node.Operator == "⿱" || node.Operator == "⿳");

            int nextConsecutive = matches ? currentConsecutive + 1 : 0;
            int maxDepth = nextConsecutive;
            if (node.Children != null)
            {
                foreach (var c in node.Children)
                {
                    int d = GetMaxAxisDepth(c, isHorizontal, nextConsecutive);
                    if (d > maxDepth) maxDepth = d;
                }
            }
            return maxDepth;
        }

        // ====================================================================================
        // GIAI ĐOẠN 2: TỔNG HỢP HÌNH THÁI HỌC TỪ DƯỚI LÊN & TRA NGƯỢC SUB-IDS (Stage 2)
        // ====================================================================================
        public static void Stage2_SynthesizeBottomUpAst(
            IdsNode node,
            byte parentOpId,
            int slotInParent,
            IGlyphSource glyphSource,
            Dictionary<string, MorphologicalProfile> nodeProfiles,
            SolverConfig cfg)
        {
            if (node == null) return;

            if (!node.IsOperator)
            {
                var prof = Stage2_ExtractLeafProfile(node.Char, glyphSource);
                nodeProfiles[node.Id] = prof;

                int rawCp = HanIdsCorpusIndex.GetFirstCodePoint(node.Char);
                node.RawCodePoint = (node.RawCodePoint > 0) ? node.RawCodePoint : rawCp;
                node.ResolvedCodePoint = rawCp;

                int normCp = HanIdsCorpusIndex.NormalizeCorpusComponent(rawCp, parentOpId, slotInParent);
                node.CanonicalHash = HanIdsCorpusIndex.ComputeLeafFnv1a(normCp);

                int corpusStrokes = HanIdsCorpusIndex.GetStrokeCount(rawCp);
                int opticalStrokes = (int)Math.Round((prof.Sx + prof.Sy) * 1.15f);
                node.EffectiveStrokes = Math.Max(1, (int)Math.Round(corpusStrokes * 0.75f + opticalStrokes * 0.25f));

                ShapeClass16 sc = HanIdsCorpusIndex.GetShapeClass(rawCp, 0, prof);
                node.EffectiveShapeClassId = (byte)sc;
                return;
            }

            byte opId = HanIdsCorpusIndex.GetOperatorId(node.Operator);
            var childProfiles = new List<MorphologicalProfile>();

            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    var child = node.Children[i];
                    Stage2_SynthesizeBottomUpAst(child, opId, i, glyphSource, nodeProfiles, cfg);
                    MorphologicalProfile cpProf;
                    if (child != null && nodeProfiles.TryGetValue(child.Id, out cpProf))
                    {
                        childProfiles.Add(cpProf);
                    }
                }
            }

            var aggProf = Stage2_AggregateOperatorProfile(node.Operator, childProfiles, cfg);
            nodeProfiles[node.Id] = aggProf;

            // Tính mã băm CanonicalHash cho cây con và tra ngược trong kho 98.000 chữ All HAN IDS.TXT
            ulong h0 = (node.Children != null && node.Children.Count > 0) ? node.Children[0].CanonicalHash : 0;
            ulong h1 = (node.Children != null && node.Children.Count > 1) ? node.Children[1].CanonicalHash : 0;
            ulong h2 = (node.Children != null && node.Children.Count > 2) ? node.Children[2].CanonicalHash : 0;

            node.CanonicalHash = HanIdsCorpusIndex.ComputeSubIdsFnv1a(opId, h0, h1, h2);
            int knownSubCharCp = HanIdsCorpusIndex.ResolveSubIdsToKnownCodePoint(node.CanonicalHash);
            node.ResolvedCodePoint = knownSubCharCp;

            // Nếu cây con khớp một chữ đã biết (như ⿱一木 -> 未 U+672A), cập nhật lại CanonicalHash theo chữ đó để nút cha tra cứu chính xác!
            if (knownSubCharCp > 0 && parentOpId != 0)
            {
                int normKnown = HanIdsCorpusIndex.NormalizeCorpusComponent(knownSubCharCp, parentOpId, slotInParent);
                node.CanonicalHash = HanIdsCorpusIndex.ComputeLeafFnv1a(normKnown);
            }

            // Tổng hợp số nét hiệu dụng (EffectiveStrokes)
            int s0 = (node.Children != null && node.Children.Count > 0) ? node.Children[0].EffectiveStrokes : 4;
            int s1 = (node.Children != null && node.Children.Count > 1) ? node.Children[1].EffectiveStrokes : 4;
            int s2 = (node.Children != null && node.Children.Count > 2) ? node.Children[2].EffectiveStrokes : 0;

            if (opId >= 1 && opId <= 4)
            {
                node.EffectiveStrokes = s0 + s1 + s2;
            }
            else if (opId >= 5 && opId <= 11)
            {
                float lambda = cfg != null ? cfg.LambdaSurround : 0.50f;
                node.EffectiveStrokes = Math.Max(2, (int)Math.Round(s0 + lambda * s1));
            }
            else
            {
                node.EffectiveStrokes = Math.Max(s0, s1) + Math.Min(s0, s1) / 2;
            }

            // Tổng hợp lớp hình thái đóng 16 lớp (EffectiveShapeClass)
            ShapeClass16 sc0 = (node.Children != null && node.Children.Count > 0)
                ? (ShapeClass16)node.Children[0].EffectiveShapeClassId
                : ShapeClass16.SquareBlock;
            ShapeClass16 sc1 = (node.Children != null && node.Children.Count > 1)
                ? (ShapeClass16)node.Children[1].EffectiveShapeClassId
                : ShapeClass16.SquareBlock;

            ShapeClass16 synthShape = (knownSubCharCp > 0)
                ? HanIdsCorpusIndex.GetShapeClass(knownSubCharCp, opId, aggProf)
                : HanIdsCorpusIndex.SynthesizeCompositeShapeClass(node.Operator, sc0, sc1);

            node.EffectiveShapeClassId = (byte)synthShape;
        }

        // ====================================================================================
        // GIAI ĐOẠN 3: TRUY VẤN 4 TẦNG TRÊN 98.000 CHỮ & GIẢI PHẪU THỰC NGHIỆM (Stage 3)
        // ====================================================================================
        public static List<ExemplarSeed> DeepTieredSearch(
            string userComponent,
            string op,
            int posIndex,
            MorphologicalProfile userProfile,
            out UnicodeTier maxTierReached)
        {
            return DeepTieredSearchContextual(
                userComponent,
                op,
                posIndex,
                userProfile,
                0,
                ShapeClass16.SquareBlock,
                6,
                null,
                out maxTierReached);
        }

        public static List<ExemplarSeed> DeepTieredSearchContextual(
            string userComponent,
            string op,
            int posIndex,
            MorphologicalProfile userProfile,
            byte siblingSubOp,
            ShapeClass16 siblingShape,
            int siblingStrokes,
            IGlyphSource glyphSource,
            out UnicodeTier maxTierReached)
        {
            int compCp = HanIdsCorpusIndex.GetFirstCodePoint(userComponent);
            int compStrokes = (compCp > 0) ? HanIdsCorpusIndex.GetStrokeCount(compCp) : 5;
            ShapeClass16 compShape = HanIdsCorpusIndex.GetShapeClass(compCp, 0, userProfile);
            return DeepTieredSearchContextual(
                userComponent,
                op,
                posIndex,
                userProfile,
                0,
                compShape,
                compStrokes,
                0,
                siblingSubOp,
                siblingShape,
                siblingStrokes,
                glyphSource,
                out maxTierReached);
        }

        public static List<ExemplarSeed> DeepTieredSearchContextual(
            string userComponent,
            string op,
            int posIndex,
            MorphologicalProfile userProfile,
            byte compSubOp,
            ShapeClass16 compShape,
            int compStrokes,
            int siblingCp,
            byte siblingSubOp,
            ShapeClass16 siblingShape,
            int siblingStrokes,
            IGlyphSource glyphSource,
            out UnicodeTier maxTierReached)
        {
            maxTierReached = UnicodeTier.Bmp;
            int compCp = HanIdsCorpusIndex.GetFirstCodePoint(userComponent);
            if (compStrokes <= 0)
            {
                compStrokes = (compCp > 0) ? HanIdsCorpusIndex.GetStrokeCount(compCp) : 5;
            }

            // 1. Truy vấn 4 tầng (kèm Tầng 1.5 Sibling-Anchored Contextual Search) từ kho 98.000 chữ All HAN IDS.TXT
            UnicodeTier corpusTier;
            var corpusCandidates = HanIdsCorpusIndex.QueryContextualExemplars(
                op,
                posIndex,
                compCp,
                compSubOp,
                compShape,
                compStrokes,
                siblingCp,
                siblingSubOp,
                siblingShape,
                siblingStrokes,
                glyphSource,
                out corpusTier);

            var combined = new Dictionary<int, ExemplarSeed>();
            foreach (var cand in corpusCandidates)
            {
                int cp = cand.Record.CodePoint;
                string c0Str = cand.Record.C0Cp > 0 ? char.ConvertFromUtf32(cand.Record.C0Cp) : "";
                string c1Str = cand.Record.C1Cp > 0 ? char.ConvertFromUtf32(cand.Record.C1Cp) : "";
                string c2Str = cand.Record.C2Cp > 0 ? char.ConvertFromUtf32(cand.Record.C2Cp) : "";
                string idsStr = HanIdsCorpusIndex.GetExpandedIdsForCodePoint(cp) ?? (op + c0Str + c1Str + c2Str);

                string[] comps = (cand.Record.C2Cp > 0)
                    ? new string[] { c0Str, c1Str, c2Str }
                    : new string[] { c0Str, c1Str };

                var seed = new ExemplarSeed(
                    cp,
                    idsStr,
                    op,
                    "tier_" + cand.MatchedTier,
                    cand.Record.Tier,
                    comps)
                {
                    MatchScore = Math.Max(10f, cand.CombinedWeight * 100f)
                };
                combined[cp] = seed;
            }

            // 2. Nếu chưa đủ 8 mẫu và có ký tự tra cứu đơn, bổ sung từ ExemplarSeeds truyền thống
            if (combined.Count < 8 && !string.IsNullOrEmpty(userComponent) && !HanIdsCorpusIndex.IsAblationMode)
            {
                UnicodeTier seedTier;
                var fallbackSeeds = ExemplarSeeds.SearchTiered(userComponent, op, posIndex, userProfile, out seedTier);
                foreach (var s in fallbackSeeds)
                {
                    if (!combined.ContainsKey(s.CodePoint))
                    {
                        combined[s.CodePoint] = s;
                    }
                }
            }

            var top8 = combined.Values
                .OrderByDescending(s => s.MatchScore)
                .Take(8)
                .ToList();

            foreach (var s in top8)
            {
                if (s.Tier > maxTierReached) maxTierReached = s.Tier;
            }

            return top8;
        }

        public static List<DissectedExemplarMetrics> ExhaustiveExemplarDissect(
            List<ExemplarSeed> exemplars,
            string userComponent,
            string op,
            int posIndex,
            FontFamily defaultFont,
            Action<string> logProgress)
        {
            var results = new List<DissectedExemplarMetrics>();
            if (exemplars == null || exemplars.Count == 0) return results;

            int rasterSz = 128;
            bool[,] maskPart = GetOrRenderBinaryMask(userComponent, 0, defaultFont, rasterSz);
            int partW = (maskPart != null) ? maskPart.GetLength(0) : 0;
            int partH = (maskPart != null) ? maskPart.GetLength(1) : 0;

            foreach (var seed in exemplars)
            {
                if (logProgress != null)
                {
                    logProgress(string.Format("Đang giải phẫu thực nghiệm mẫu {0} ({1})...", seed.Character, seed.IdsDecomp));
                }

                try
                {
                    bool[,] maskEx = GetOrRenderBinaryMask(seed.Character, seed.CodePoint, defaultFont, rasterSz);
                    if (maskEx != null)
                    {
                        var metrics = DissectSingleExemplarFromMask(maskEx, maskPart, partW, partH, seed, op, posIndex, rasterSz);
                        if (metrics != null)
                        {
                            results.Add(metrics);
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        private static bool[,] GetOrRenderBinaryMask(string ch, int codePoint, FontFamily defaultFont, int rasterSz)
        {
            if (string.IsNullOrEmpty(ch) || ch == "□") return null;
            string fontName = defaultFont != null ? defaultFont.Name : "Default";
            string key = string.Format("{0}_{1}_{2}", fontName, ch, rasterSz);

            bool[,] cached;
            if (MaskCache.TryGetValue(key, out cached)) return cached;

            int cp = codePoint > 0 ? codePoint : HanIdsCorpusIndex.GetFirstCodePoint(ch);
            FontFamily fam = FontLoaderService.ResolveFontFamilyForCodePoint(cp, defaultFont);
            using (var bmp = FontLoaderService.RenderGlyphToBitmap(ch, cp, fam, rasterSz))
            {
                if (bmp == null) return null;
                var mask = ExtractBinaryMask(bmp);
                MaskCache[key] = mask;
                return mask;
            }
        }

        public static bool[,] ExtractBinaryMask(Bitmap bmp)
        {
            if (bmp == null) return new bool[0, 0];
            int w = bmp.Width;
            int h = bmp.Height;
            var mask = new bool[w, h];
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bd.Scan0;
                    int stride = bd.Stride;
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            if (row[x * 4 + 2] < 180 || row[x * 4 + 1] < 180 || row[x * 4] < 180)
                            {
                                mask[x, y] = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return mask;
        }

        private static Rectangle FindInkBoundsFromMask(bool[,] mask, int w, int h)
        {
            return FindInkBoundsInSubRect(mask, w, h, new Rectangle(0, 0, w, h));
        }

        private static Rectangle FindInkBoundsInSubRect(bool[,] mask, int w, int h, Rectangle region)
        {
            int x0 = Math.Max(0, region.Left);
            int y0 = Math.Max(0, region.Top);
            int x1 = Math.Min(w, region.Right);
            int y1 = Math.Min(h, region.Bottom);

            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (mask[x, y])
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX || maxY < minY) return region;
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static int FindVerticalValleySplit(
            bool[,] mask,
            int w,
            int h,
            Rectangle outerInk,
            float expectedRatio,
            int minPct,
            int maxPct)
        {
            int startX = outerInk.Left + (outerInk.Width * minPct) / 100;
            int endX = outerInk.Left + (outerInk.Width * maxPct) / 100;
            if (endX <= startX) return outerInk.Left + (int)(outerInk.Width * expectedRatio);

            int bestX = outerInk.Left + (int)(outerInk.Width * expectedRatio);
            float bestCost = float.MaxValue;

            for (int x = startX; x <= endX; x++)
            {
                int inkCount = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = Math.Max(outerInk.Left, Math.Min(outerInk.Right - 1, x + dx));
                    for (int y = outerInk.Top; y < outerInk.Bottom; y++)
                    {
                        if (mask[cx, y]) inkCount++;
                    }
                }

                float inkDensity = (float)inkCount / Math.Max(1f, 3f * outerInk.Height);
                float relX = (float)(x - outerInk.Left) / Math.Max(1f, outerInk.Width);
                float distPenalty = 0.55f * Math.Abs(relX - expectedRatio);
                float cost = inkDensity + distPenalty;

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestX = x;
                }
            }

            return bestX;
        }

        private static int FindHorizontalValleySplit(
            bool[,] mask,
            int w,
            int h,
            Rectangle outerInk,
            float expectedRatio,
            int minPct,
            int maxPct)
        {
            int startY = outerInk.Top + (outerInk.Height * minPct) / 100;
            int endY = outerInk.Top + (outerInk.Height * maxPct) / 100;
            if (endY <= startY) return outerInk.Top + (int)(outerInk.Height * expectedRatio);

            int bestY = outerInk.Top + (int)(outerInk.Height * expectedRatio);
            float bestCost = float.MaxValue;

            for (int y = startY; y <= endY; y++)
            {
                int inkCount = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int cy = Math.Max(outerInk.Top, Math.Min(outerInk.Bottom - 1, y + dy));
                    for (int x = outerInk.Left; x < outerInk.Right; x++)
                    {
                        if (mask[x, cy]) inkCount++;
                    }
                }

                float inkDensity = (float)inkCount / Math.Max(1f, 3f * outerInk.Width);
                float relY = (float)(y - outerInk.Top) / Math.Max(1f, outerInk.Height);
                float distPenalty = 0.55f * Math.Abs(relY - expectedRatio);
                float cost = inkDensity + distPenalty;

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestY = y;
                }
            }

            return bestY;
        }

        private static DissectedExemplarMetrics DissectSingleExemplarFromMask(
            bool[,] maskEx,
            bool[,] maskPart,
            int partW,
            int partH,
            ExemplarSeed seed,
            string op,
            int posIndex,
            int sz)
        {
            if (maskEx == null) return null;

            Rectangle outerInk = FindInkBoundsFromMask(maskEx, sz, sz);
            if (outerInk.Width <= 4 || outerInk.Height <= 4) outerInk = new Rectangle(8, 8, sz - 16, sz - 16);

            RectangleF outerMargin1000 = new RectangleF(
                outerInk.Left * 1000f / sz,
                outerInk.Top * 1000f / sz,
                outerInk.Width * 1000f / sz,
                outerInk.Height * 1000f / sz
            );

            // Giải phẫu thực nghiệm bằng thuật toán Cắt Thung Lũng Mật Độ Mực (Projection-Valley Cut)
            // và đo trực tiếp Hộp Mực Thực Tế (True Sub-Region Ink Bounding Box) của từng bộ phận trong chữ mẫu!
            int s0 = (seed != null && seed.Components != null && seed.Components.Length > 0)
                ? HanIdsCorpusIndex.GetStrokeCount(HanIdsCorpusIndex.GetFirstCodePoint(seed.Components[0]))
                : 4;
            int s1 = (seed != null && seed.Components != null && seed.Components.Length > 1)
                ? HanIdsCorpusIndex.GetStrokeCount(HanIdsCorpusIndex.GetFirstCodePoint(seed.Components[1]))
                : 5;

            float expectedSplit = (float)Math.Pow(Math.Max(1, s0), 0.36) /
                ((float)Math.Pow(Math.Max(1, s0), 0.36) + (float)Math.Pow(Math.Max(1, s1), 0.36));
            expectedSplit = Math.Max(0.28f, Math.Min(0.72f, expectedSplit));

            Rectangle matchedSlot = outerInk;
            float realSplitRatio = expectedSplit;
            float clearance = 22.0f;

            if (op == "⿰")
            {
                int splitX = FindVerticalValleySplit(maskEx, sz, sz, outerInk, expectedSplit, 24, 76);
                var boxLeft = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(outerInk.Left, outerInk.Top, Math.Max(4, splitX - outerInk.Left), outerInk.Height));
                var boxRight = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(splitX, outerInk.Top, Math.Max(4, outerInk.Right - splitX), outerInk.Height));

                matchedSlot = (posIndex == 0) ? boxLeft : boxRight;
                realSplitRatio = Math.Max(0.26f, Math.Min(0.74f, (float)(splitX - outerInk.Left) / Math.Max(1, outerInk.Width)));
                clearance = Math.Max(14f, Math.Min(40f, (boxRight.Left - boxLeft.Right) * 1000f / sz));
            }
            else if (op == "⿱")
            {
                int splitY = FindHorizontalValleySplit(maskEx, sz, sz, outerInk, expectedSplit, 22, 78);
                var boxTop = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(outerInk.Left, outerInk.Top, outerInk.Width, Math.Max(4, splitY - outerInk.Top)));
                var boxBot = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(outerInk.Left, splitY, outerInk.Width, Math.Max(4, outerInk.Bottom - splitY)));

                matchedSlot = (posIndex == 0) ? boxTop : boxBot;
                realSplitRatio = Math.Max(0.24f, Math.Min(0.76f, (float)(splitY - outerInk.Top) / Math.Max(1, outerInk.Height)));
                clearance = Math.Max(12f, Math.Min(36f, (boxBot.Top - boxTop.Bottom) * 1000f / sz));
            }
            else if (op == "⿲")
            {
                int splitX1 = FindVerticalValleySplit(maskEx, sz, sz, outerInk, 0.30f, 18, 42);
                int splitX2 = FindVerticalValleySplit(maskEx, sz, sz, outerInk, 0.68f, Math.Max(52, (splitX1 - outerInk.Left) * 100 / Math.Max(1, outerInk.Width) + 18), 82);
                var box0 = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(outerInk.Left, outerInk.Top, Math.Max(4, splitX1 - outerInk.Left), outerInk.Height));
                var box1 = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(splitX1, outerInk.Top, Math.Max(4, splitX2 - splitX1), outerInk.Height));
                var box2 = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(splitX2, outerInk.Top, Math.Max(4, outerInk.Right - splitX2), outerInk.Height));

                matchedSlot = (posIndex == 0) ? box0 : (posIndex == 1 ? box1 : box2);
                realSplitRatio = Math.Max(0.20f, Math.Min(0.55f, (float)matchedSlot.Width / Math.Max(1, outerInk.Width)));
            }
            else if (op == "⿳")
            {
                int splitY1 = FindHorizontalValleySplit(maskEx, sz, sz, outerInk, 0.30f, 18, 42);
                int splitY2 = FindHorizontalValleySplit(maskEx, sz, sz, outerInk, 0.68f, Math.Max(52, (splitY1 - outerInk.Top) * 100 / Math.Max(1, outerInk.Height) + 18), 82);
                var box0 = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(outerInk.Left, outerInk.Top, outerInk.Width, Math.Max(4, splitY1 - outerInk.Top)));
                var box1 = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(outerInk.Left, splitY1, outerInk.Width, Math.Max(4, splitY2 - splitY1)));
                var box2 = FindInkBoundsInSubRect(maskEx, sz, sz, new Rectangle(outerInk.Left, splitY2, outerInk.Width, Math.Max(4, outerInk.Bottom - splitY2)));

                matchedSlot = (posIndex == 0) ? box0 : (posIndex == 1 ? box1 : box2);
                realSplitRatio = Math.Max(0.20f, Math.Min(0.55f, (float)matchedSlot.Height / Math.Max(1, outerInk.Height)));
            }
            else
            {
                Rectangle searchRegion = GetOperatorSearchWindow(outerInk, op, posIndex);
                matchedSlot = FindBestCorrelationSlot(maskEx, sz, sz, maskPart, partW, partH, searchRegion);
            }

            RectangleF partRelativeBox = new RectangleF(
                matchedSlot.Left * 1000f / sz,
                matchedSlot.Top * 1000f / sz,
                matchedSlot.Width * 1000f / sz,
                matchedSlot.Height * 1000f / sz
            );

            float tx, ty;
            MeasureSlotStrokeThickness(maskEx, sz, sz, matchedSlot, out tx, out ty);

            float interlock = 0f;
            if (op == "⿱" && posIndex == 0)
            {
                interlock = Math.Max(0f, partRelativeBox.Bottom - 500f);
            }
            else if (op == "⿰" && posIndex == 0)
            {
                interlock = Math.Max(0f, partRelativeBox.Right - 500f);
            }

            return new DissectedExemplarMetrics
            {
                Seed = seed,
                ExemplarOuterMargin = outerMargin1000,
                PartRelativeBox = partRelativeBox,
                RealSplitRatio = realSplitRatio,
                RealInterlockDepth = interlock,
                RealClearanceGap = clearance,
                RealTx = (tx > 0) ? tx : 68.0f,
                RealTy = (ty > 0) ? ty : 68.0f,
                MatchScore = seed.MatchScore
            };
        }

        private static Rectangle GetOperatorSearchWindow(Rectangle outerInk, string op, int posIndex)
        {
            int x = outerInk.Left;
            int y = outerInk.Top;
            int w = outerInk.Width;
            int h = outerInk.Height;

            if (op == "⿰")
            {
                if (posIndex == 0) return new Rectangle(x, y, Math.Max(8, (int)(w * 0.68f)), h);
                int startX = x + (int)(w * 0.24f);
                return new Rectangle(startX, y, Math.Max(8, (x + w) - startX), h);
            }
            if (op == "⿱")
            {
                if (posIndex == 0) return new Rectangle(x, y, w, Math.Max(8, (int)(h * 0.68f)));
                int startY = y + (int)(h * 0.24f);
                return new Rectangle(x, startY, w, Math.Max(8, (y + h) - startY));
            }
            if (op == "⿲")
            {
                int third = Math.Max(6, w / 3);
                if (posIndex == 0) return new Rectangle(x, y, Math.Max(8, (int)(w * 0.42f)), h);
                if (posIndex == 1) return new Rectangle(x + third - 4, y, Math.Max(8, (int)(w * 0.42f)), h);
                return new Rectangle(x + w - third - 6, y, Math.Max(8, (int)(w * 0.42f)), h);
            }
            if (op == "⿳")
            {
                int third = Math.Max(6, h / 3);
                if (posIndex == 0) return new Rectangle(x, y, w, Math.Max(8, (int)(h * 0.42f)));
                if (posIndex == 1) return new Rectangle(x, y + third - 4, w, Math.Max(8, (int)(h * 0.42f)));
                return new Rectangle(x, y + h - third - 6, w, Math.Max(8, (int)(h * 0.42f)));
            }

            if ("⿴⿵⿶⿷⿸⿹⿺".Contains(op))
            {
                if (posIndex == 0) return outerInk;
                return new Rectangle(x + (int)(w * 0.22f), y + (int)(h * 0.22f), Math.Max(8, (int)(w * 0.56f)), Math.Max(8, (int)(h * 0.56f)));
            }

            return outerInk;
        }

        private static Rectangle FindBestCorrelationSlot(
            bool[,] maskEx, int exW, int exH,
            bool[,] maskPart, int partW, int partH,
            Rectangle searchRegion)
        {
            if (maskPart == null || maskEx == null) return searchRegion;

            int bestX = searchRegion.X;
            int bestY = searchRegion.Y;
            int bestW = searchRegion.Width;
            int bestH = searchRegion.Height;
            float bestScore = -1f;

            int stepX = Math.Max(4, searchRegion.Width / 5);
            int stepY = Math.Max(4, searchRegion.Height / 5);

            for (int w = Math.Max(8, (int)(searchRegion.Width * 0.36f)); w <= searchRegion.Width; w += stepX)
            {
                for (int h = Math.Max(8, (int)(searchRegion.Height * 0.36f)); h <= searchRegion.Height; h += stepY)
                {
                    for (int x = searchRegion.Left; x + w <= searchRegion.Right; x += stepX)
                    {
                        for (int y = searchRegion.Top; y + h <= searchRegion.Bottom; y += stepY)
                        {
                            float score = ComputeProjectionNCC(maskEx, exW, exH, new Rectangle(x, y, w, h), maskPart, partW, partH);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestX = x;
                                bestY = y;
                                bestW = w;
                                bestH = h;
                            }
                        }
                    }
                }
            }

            return new Rectangle(bestX, bestY, bestW, bestH);
        }

        private static float ComputeProjectionNCC(
            bool[,] maskEx, int exW, int exH,
            Rectangle slot,
            bool[,] maskPart, int partW, int partH)
        {
            if (slot.Width <= 0 || slot.Height <= 0) return 0f;

            float[] exHoriz = new float[slot.Height];
            float[] exVert = new float[slot.Width];
            float totalExInk = 0f;

            for (int r = 0; r < slot.Height; r++)
            {
                int py = slot.Y + r;
                if (py < 0 || py >= exH) continue;
                for (int c = 0; c < slot.Width; c++)
                {
                    int px = slot.X + c;
                    if (px < 0 || px >= exW) continue;
                    if (maskEx[px, py])
                    {
                        exHoriz[r]++;
                        exVert[c]++;
                        totalExInk++;
                    }
                }
            }

            float area = slot.Width * slot.Height;
            float exDensity = (area > 0) ? totalExInk / area : 0f;
            if (exDensity < 0.03f) return 0.05f;

            if (maskPart != null && partW > 0 && partH > 0)
            {
                float[] partHoriz = new float[slot.Height];
                float[] partVert = new float[slot.Width];

                float stepY = (float)partH / slot.Height;
                float stepX = (float)partW / slot.Width;

                for (int r = 0; r < slot.Height; r++)
                {
                    int py = Math.Min(partH - 1, (int)(r * stepY));
                    for (int c = 0; c < slot.Width; c++)
                    {
                        int px = Math.Min(partW - 1, (int)(c * stepX));
                        if (maskPart[px, py])
                        {
                            partHoriz[r]++;
                            partVert[c]++;
                        }
                    }
                }

                float nccH = ComputePearsonCorrelation(exHoriz, partHoriz);
                float nccV = ComputePearsonCorrelation(exVert, partVert);
                float shapeScore = (nccH + nccV + 2f) / 4f;
                return Math.Max(0.05f, Math.Min(1.0f, shapeScore));
            }

            return (exDensity >= 0.08f && exDensity <= 0.55f) ? (1.0f - Math.Abs(exDensity - 0.28f)) : 0.1f;
        }

        private static float ComputePearsonCorrelation(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length) return 0f;
            int n = a.Length;
            float meanA = 0f, meanB = 0f;
            for (int i = 0; i < n; i++) { meanA += a[i]; meanB += b[i]; }
            meanA /= n;
            meanB /= n;

            float num = 0f, denA = 0f, denB = 0f;
            for (int i = 0; i < n; i++)
            {
                float da = a[i] - meanA;
                float db = b[i] - meanB;
                num += da * db;
                denA += da * da;
                denB += db * db;
            }

            float den = (float)(Math.Sqrt(denA) * Math.Sqrt(denB));
            if (den < 0.0001f) return 0f;
            return num / den;
        }

        private static void MeasureSlotStrokeThickness(bool[,] mask, int w, int h, Rectangle slot, out float tx, out float ty)
        {
            tx = 68.0f;
            ty = 68.0f;
            if (mask == null || slot.Width <= 0 || slot.Height <= 0) return;

            var hRuns = new List<int>();
            var vRuns = new List<int>();

            for (int y = slot.Top; y < slot.Bottom; y += 4)
            {
                if (y < 0 || y >= h) continue;
                int run = 0;
                for (int x = slot.Left; x < slot.Right; x++)
                {
                    if (x < 0 || x >= w) continue;
                    if (mask[x, y]) run++;
                    else
                    {
                        if (run >= 2 && run <= slot.Width / 2) hRuns.Add(run);
                        run = 0;
                    }
                }
                if (run >= 2 && run <= slot.Width / 2) hRuns.Add(run);
            }

            for (int x = slot.Left; x < slot.Right; x += 4)
            {
                if (x < 0 || x >= w) continue;
                int run = 0;
                for (int y = slot.Top; y < slot.Bottom; y++)
                {
                    if (y < 0 || y >= h) continue;
                    if (mask[x, y]) run++;
                    else
                    {
                        if (run >= 2 && run <= slot.Height / 2) vRuns.Add(run);
                        run = 0;
                    }
                }
                if (run >= 2 && run <= slot.Height / 2) vRuns.Add(run);
            }

            if (hRuns.Count > 0)
            {
                hRuns.Sort();
                tx = hRuns[hRuns.Count / 2] * 1000f / w;
            }
            if (vRuns.Count > 0)
            {
                vRuns.Sort();
                ty = vRuns[vRuns.Count / 2] * 1000f / h;
            }
        }

        private static void MeasureSlotStrokeThickness(Bitmap bmp, Rectangle slot, out float tx, out float ty)
        {
            var mask = ExtractBinaryMask(bmp);
            MeasureSlotStrokeThickness(mask, bmp.Width, bmp.Height, slot, out tx, out ty);
        }

        private static Rectangle FindInkBoundsPixel(Bitmap bmp)
        {
            if (bmp == null) return new Rectangle(0, 0, 0, 0);
            var mask = ExtractBinaryMask(bmp);
            return FindInkBoundsFromMask(mask, bmp.Width, bmp.Height);
        }

        // ====================================================================================
        // GIAI ĐOẠN 3B: CO RÚT EMPIRICAL BAYES & TIÊN NGHIỆM HÌNH THÁI (ConsensusBuilder)
        // ====================================================================================
        public static float ComputeAnalyticalOperatorPrior(
            string op,
            IdsNode c0,
            IdsNode c1,
            MorphologicalProfile p0,
            MorphologicalProfile p1,
            SolverConfig cfg)
        {
            if (cfg == null) cfg = new SolverConfig();
            float gamma = cfg.Gamma;

            int s0 = (c0 != null && c0.EffectiveStrokes > 0) ? c0.EffectiveStrokes : 4;
            int s1 = (c1 != null && c1.EffectiveStrokes > 0) ? c1.EffectiveStrokes : 4;
            ShapeClass16 sc0 = (c0 != null) ? (ShapeClass16)c0.EffectiveShapeClassId : ShapeClass16.SquareBlock;
            ShapeClass16 sc1 = (c1 != null) ? (ShapeClass16)c1.EffectiveShapeClassId : ShapeClass16.SquareBlock;

            float rPrior = 0.5f;

            if (op == "⿰")
            {
                float w0 = (float)Math.Pow(Math.Max(1.0f, s0 * (p0 != null ? Math.Max(0.45f, Math.Min(1.4f, p0.Aspect)) : 1.0f)), gamma);
                float w1 = (float)Math.Pow(Math.Max(1.0f, s1 * (p1 != null ? Math.Max(0.45f, Math.Min(1.4f, p1.Aspect)) : 1.0f)), gamma);
                rPrior = w0 / Math.Max(0.01f, w0 + w1);

                // Điều chỉnh theo 16 lớp hình thái học đóng (ShapeClass16) và cấu trúc bộ thủ trái - thân phải
                if (sc0 == ShapeClass16.NarrowPillar && sc1 != ShapeClass16.NarrowPillar)
                {
                    rPrior = Math.Min(rPrior, 0.36f);
                }
                else if (sc1 == ShapeClass16.NarrowPillar && sc0 != ShapeClass16.NarrowPillar)
                {
                    rPrior = Math.Max(rPrior, 0.64f);
                }
                else if (c0 != null && !c0.IsOperator && c1 != null && c1.IsOperator && s0 <= s1)
                {
                    // Khi nhánh trái là bộ thủ đơn (như 女, 木, 口, 日) và nhánh phải là cụm phức hợp (như ⿱天土),
                    // bộ thủ trái nhường không gian cho thân phải theo chuẩn ngữ pháp chữ Hán-Nôm (~38%..41%)
                    rPrior = Math.Min(rPrior, 0.40f);
                }
                else if (c0 != null && !c0.IsOperator && s0 <= 4 && p0 != null && p0.NHoles >= 1 && p0.WTop >= 0.58f && p0.WBot >= 0.58f)
                {
                    // Bộ thủ hộp đóng bên trái (như 口, 日 trong ⿰日月) luôn gọn bề ngang (~40%..42%)
                    rPrior = Math.Min(rPrior, 0.42f);
                }
            }
            else if (op == "⿱")
            {
                float h0 = (float)Math.Pow(Math.Max(1.0f, s0), gamma);
                float h1 = (float)Math.Pow(Math.Max(1.0f, s1), gamma);
                rPrior = h0 / Math.Max(0.01f, h0 + h1);

                if (sc0 == ShapeClass16.FlatBar)
                {
                    rPrior = Math.Min(rPrior, 0.28f);
                }
                else if (sc0 == ShapeClass16.WideRoof && s1 >= s0)
                {
                    rPrior = Math.Min(rPrior, 0.38f);
                }
                else if (sc1 == ShapeClass16.WideBase && s0 >= s1)
                {
                    rPrior = Math.Max(rPrior, 0.54f);
                }
            }

            return Math.Max(cfg.SplitMin, Math.Min(cfg.SplitMax, rPrior));
        }

        public static ConsensusBlueprint BuildConsensusBlueprint(
            Dictionary<string, List<DissectedExemplarMetrics>> dissectedByNode,
            string rootOp,
            SolverConfig cfg)
        {
            return BuildConsensusBlueprintWithPrior(dissectedByNode, rootOp, 0.5f, cfg);
        }

        public static ConsensusBlueprint BuildConsensusBlueprintWithPrior(
            Dictionary<string, List<DissectedExemplarMetrics>> dissectedByNode,
            string rootOp,
            float analyticalPriorRatio,
            SolverConfig cfg)
        {
            if (cfg == null) cfg = new SolverConfig();
            var bp = new ConsensusBlueprint();
            var allMetrics = dissectedByNode != null
                ? dissectedByNode.Values.SelectMany(x => x).ToList()
                : new List<DissectedExemplarMetrics>();

            float rPrior = Math.Max(cfg.SplitMin, Math.Min(cfg.SplitMax, analyticalPriorRatio));

            if (allMetrics.Count == 0)
            {
                bp.ConsensusSplitRatio = rPrior;
                bp.ConsensusClearanceGap = cfg.GTarget;
                bp.TxStar = 68.0f;
                bp.TyStar = 68.0f;
                bp.TotalEvidenceWeight = 0f;
                bp.ShrinkageAlpha = 0f;
                return bp;
            }

            // 1. Trimmed Weighted Mean (cắt bỏ 20% đầu/cuối khi N >= 5)
            float empiricalRatio = ComputeTrimmedWeightedMean(allMetrics, m => m.RealSplitRatio);

            // Kết hợp thêm k-NN Gaussian kernel theo cfg.TopK, cfg.AlphaMax, cfg.Sigma, cfg.CMin
            var topKMetrics = allMetrics.OrderByDescending(m => m.MatchScore).Take(cfg.TopK > 0 ? cfg.TopK : 3).ToList();
            float totalKernelW = 0f, weightedKernelRatio = 0f;
            foreach (var m in topKMetrics)
            {
                float dist = Math.Abs(m.RealSplitRatio - rPrior);
                float weight = cfg.AlphaMax * (float)Math.Exp(-(dist * dist) / (2f * cfg.Sigma * cfg.Sigma));
                if (weight < cfg.CMin) weight = cfg.CMin;
                weightedKernelRatio += m.RealSplitRatio * weight;
                totalKernelW += weight;
            }
            if (totalKernelW > 0)
            {
                empiricalRatio = 0.65f * empiricalRatio + 0.35f * (weightedKernelRatio / totalKernelW);
            }

            // 2. Co rút Empirical Bayes (Shrinkage với hằng số tau = 1.5)
            float sumEvidenceW = allMetrics.Sum(m => Math.Max(0.1f, m.MatchScore / 100f));
            float tau = 1.5f;
            float alpha = sumEvidenceW / (sumEvidenceW + tau);
            alpha = Math.Max(0f, Math.Min(0.90f, alpha));

            float blendedRatio = alpha * empiricalRatio + (1.0f - alpha) * rPrior;
            bp.ConsensusSplitRatio = Math.Max(cfg.SplitMin, Math.Min(cfg.SplitMax, blendedRatio));
            bp.TotalEvidenceWeight = sumEvidenceW;
            bp.ShrinkageAlpha = alpha;

            bp.ConsensusClearanceGap = ComputeTrimmedWeightedMean(allMetrics, m => m.RealClearanceGap);
            bp.ConsensusClearanceGap = Math.Max(cfg.GTarget * 0.9f, Math.Min(cfg.GTarget * 1.25f, bp.ConsensusClearanceGap));

            bp.TxStar = ComputeTrimmedWeightedMean(allMetrics, m => m.RealTx);
            bp.TyStar = ComputeTrimmedWeightedMean(allMetrics, m => m.RealTy);

            foreach (var kvp in dissectedByNode)
            {
                string nodeId = kvp.Key;
                var list = kvp.Value;
                if (list.Count > 0)
                {
                    float w = ComputeTrimmedWeightedMean(list, m => m.PartRelativeBox.Width);
                    float h = ComputeTrimmedWeightedMean(list, m => m.PartRelativeBox.Height);
                    float x = ComputeTrimmedWeightedMean(list, m => m.PartRelativeBox.X);
                    float y = ComputeTrimmedWeightedMean(list, m => m.PartRelativeBox.Y);
                    bp.PartRelativeBoxes[nodeId] = new RectangleF(x, y, w, h);
                }
            }

            return bp;
        }

        private static float ComputeTrimmedWeightedMean(List<DissectedExemplarMetrics> list, Func<DissectedExemplarMetrics, float> selector)
        {
            if (list == null || list.Count == 0) return 0f;
            var ordered = list.OrderBy(selector).ToList();

            int n = ordered.Count;
            int trimCount = (n >= 5) ? (int)Math.Round(n * 0.2f) : 0;
            int start = trimCount;
            int end = n - trimCount;

            float sum = 0f;
            float totalWeight = 0f;

            for (int i = start; i < end; i++)
            {
                float val = selector(ordered[i]);
                float w = Math.Max(1.0f, ordered[i].MatchScore);
                sum += val * w;
                totalWeight += w;
            }

            return (totalWeight > 0) ? (sum / totalWeight) : selector(ordered[n / 2]);
        }

        // ====================================================================================
        // GIAI ĐOẠN 4 & 5: GIẢI ĐỆ QUY TOP-DOWN, KHỚP MỘNG SKYLINE & BẢO TOÀN LỖ TRẮNG
        // ====================================================================================
        public static BeautifyResult BeautifyThisIds(
            string idsString,
            IdsNode root,
            FontFamily font,
            bool isVietnamese)
        {
            return BeautifyThisIds(idsString, root, font, null, isVietnamese, null, null);
        }

        public static BeautifyResult BeautifyThisIds(
            string idsString,
            IdsNode root,
            FontFamily font,
            Dictionary<string, NodeCustomTransform> currentTransforms = null,
            bool isVietnamese = true,
            SolverConfig cfg = null,
            Action<string> progressCallback = null)
        {
            var res = new BeautifyResult();
            if (cfg == null) cfg = new SolverConfig();
            var glyphSource = new GdiGlyphSource(font);
            var swTotal = Stopwatch.StartNew();

            Action<string> reportProgress = msg =>
            {
                if (progressCallback != null) progressCallback(msg);
            };

            reportProgress(isVietnamese ? "Khởi tạo kiểm định ngữ pháp cú pháp IDS..." : "Validating IDS grammar...");

            // 1. STAGE 1: Kiểm định ngữ pháp AST nghiêm ngặt
            var sanity = Stage1_ValidateAstSanity(idsString, root, glyphSource);
            if (!sanity.Ok)
            {
                res.Success = false;
                res.ErrorMessage = isVietnamese ? sanity.MessageVi : sanity.MessageEn;
                return res;
            }

            // Đo độ dày nét chuẩn của font và safeBox Mễ Tự Cách
            float txFont, tyFont;
            glyphSource.GetFontStrokeThickness(out txFont, out tyFont);
            RectangleF safeBox = glyphSource.MeasureExemplarSafeBox();
            if (safeBox.Width <= 0 || safeBox.Height <= 0)
            {
                safeBox = new RectangleF(cfg.SafeMinX, cfg.SafeMinY, cfg.SafeMaxX - cfg.SafeMinX, cfg.SafeMaxY - cfg.SafeMinY);
            }

            // 2. Khóa Gốc Lũy Đẳng (Idempotent Reset - Luôn khởi tạo từ Raw Identity)
            var transforms = new Dictionary<string, NodeCustomTransform>();
            var nodeProfiles = new Dictionary<string, MorphologicalProfile>();

            // 3. STAGE 2: Tổng hợp hình thái học từ dưới lên (Bottom-Up Synthesis) & Gập Sub-IDS
            Stage2_SynthesizeBottomUpAst(root, 0, 0, glyphSource, nodeProfiles, cfg);

            // Khởi tạo transform mặc định kèm ComponentAspect cho mọi node trong cây
            Action<IdsNode> initDefaultTransforms = null;
            initDefaultTransforms = n =>
            {
                if (n == null) return;
                var t = new NodeCustomTransform();
                if (!n.IsOperator)
                {
                    MorphologicalProfile lp;
                    if (nodeProfiles.TryGetValue(n.Id, out lp) && lp != null)
                    {
                        t.ComponentAspect = lp.Aspect;
                    }
                }
                transforms[n.Id] = t;
                if (n.Children != null)
                {
                    foreach (var c in n.Children) initDefaultTransforms(c);
                }
            };
            initDefaultTransforms(root);

            // 4. STAGE 3 & 4: Giải đệ quy Top-Down cho MỌI nút toán tử trong cây AST
            var matchedSeedsLog = new List<string>();
            UnicodeTier highestTierReached = UnicodeTier.Bmp;
            bool anyExemplarsFound = false;
            float rootTxStar = txFont;
            float rootTyStar = tyFont;

            SolveOperatorSubtreeRecursive(
                root,
                true,
                font,
                glyphSource,
                nodeProfiles,
                transforms,
                cfg,
                isVietnamese,
                reportProgress,
                matchedSeedsLog,
                res.ReportBulletPoints,
                ref highestTierReached,
                ref anyExemplarsFound,
                ref rootTxStar,
                ref rootTyStar);

            if (!anyExemplarsFound || HanIdsCorpusIndex.IsAblationMode)
            {
                res.ReportBulletPoints.Insert(0, isVietnamese
                    ? "• Chế độ tổng quát (General mode): Tối ưu hình thái học 16 lớp và phân bổ lũy thừa"
                    : "• General mode: 16-class morphological optimization and power law split");
            }

            foreach (var log in matchedSeedsLog)
            {
                res.ReportBulletPoints.Add(log);
            }

            // 5. STAGE 5: Bồi Nét Dị Hướng & Khóa Bảo Toàn Lỗ Trắng (Counter-Space Guard)
            reportProgress(isVietnamese ? "Đo đạc bồi nét dị hướng và kiểm định CounterSpaceGuard..." : "Applying anisotropic dilation & CounterSpaceGuard...");
            int totalHolesPreserved = 0;
            foreach (var kvp in nodeProfiles)
            {
                var n = FindNode(root, kvp.Key);
                if (n != null && !n.IsOperator)
                {
                    var t = transforms[n.Id];
                    float dx, dy;
                    Stage5_ComputeAnisotropicDilation(n.Char, kvp.Value, t.ScaleX, t.ScaleY, rootTxStar, rootTyStar, font, cfg, out dx, out dy);

                    int initialHoles = kvp.Value.NHoles;
                    if (initialHoles > 0 && (dx > 0 || dy > 0))
                    {
                        while (dx > 0 || dy > 0)
                        {
                            using (var testBmp = RenderTestDilatedGlyph(n.Char, t.ScaleX, t.ScaleY, dx, dy))
                            {
                                int finalHoles = CountHolesOnRaster(testBmp);
                                if (finalHoles >= initialHoles) break;
                            }
                            dx = Math.Max(0f, dx - cfg.DilateStep);
                            dy = Math.Max(0f, dy - cfg.DilateStep);
                        }
                    }

                    t.StrokeDilationX = (float)Math.Round(dx, 1);
                    t.StrokeDilationY = (float)Math.Round(dy, 1);
                    transforms[n.Id] = t;

                    if (initialHoles > 0) totalHolesPreserved += initialHoles;
                }
            }

            if (totalHolesPreserved > 0)
            {
                res.ReportBulletPoints.Add(string.Format(
                    isVietnamese
                        ? "• Khóa Counter-Space Guard: Bảo toàn 100% ({0} lỗ trắng nội tại sắc nét)"
                        : "• Counter-Space Guard: 100% preserved ({0} internal holes kept crisp)",
                    totalHolesPreserved));
            }

            // Tinh chỉnh tọa độ giới hạn (Bounded Coordinate Descent) và chống va chạm mực
            RunBoundedCoordinateDescent(root, transforms, nodeProfiles, font, cfg);

            // Căn tâm Ink Bounding Box về safeBox Mễ Tự Cách
            ApplyFinalSafeBoxCentering(root, transforms, font, safeBox, res.ReportBulletPoints, isVietnamese);

            swTotal.Stop();
            res.ReportBulletPoints.Insert(0, string.Format(
                isVietnamese
                    ? "✨ HOÀN TẤT ĐỐI CHIẾU 98.000 CHỮ & GIẢI PHẪU QUANG HỌC ({0:F2} giây | Quét đến: {1})"
                    : "✨ 98K CORPUS CROSS-REFERENCE & OPTICAL SOLVER COMPLETE ({0:F2}s | Tier: {1})",
                swTotal.Elapsed.TotalSeconds, highestTierReached));

            // 6. Kiểm tra Lũy Đẳng (Idempotence Check: nếu sai lệch < 1% so với hiện tại thì báo IsAlreadyOptimal)
            if (currentTransforms != null && currentTransforms.Count > 0)
            {
                bool isClose = true;
                foreach (var kvp in transforms)
                {
                    NodeCustomTransform cur;
                    if (currentTransforms.TryGetValue(kvp.Key, out cur))
                    {
                        var t = kvp.Value;
                        if (Math.Abs(t.ScaleX - cur.ScaleX) > 0.01f ||
                            Math.Abs(t.ScaleY - cur.ScaleY) > 0.01f ||
                            Math.Abs(t.OffsetX - cur.OffsetX) > 2.0f ||
                            Math.Abs(t.OffsetY - cur.OffsetY) > 2.0f ||
                            Math.Abs(t.StrokeDilationX - cur.StrokeDilationX) > 0.15f ||
                            Math.Abs(t.StrokeDilationY - cur.StrokeDilationY) > 0.15f)
                        {
                            isClose = false;
                            break;
                        }
                    }
                    else
                    {
                        isClose = false;
                        break;
                    }
                }

                if (isClose)
                {
                    res.Success = true;
                    res.IsAlreadyOptimal = true;
                    res.ReportBulletPoints.Clear();
                    res.ReportBulletPoints.Add(isVietnamese
                        ? "✔ Bố cục hiện tại đã đạt chuẩn đồng thuận tối ưu từ tập mẫu đối chiếu. Không cần biến đổi thêm."
                        : "✔ Current layout has reached optimal consensus from reference exemplars. No additional transformation needed.");
                    res.BeautifiedTransforms = CloneTransforms(currentTransforms);
                    return res;
                }
            }

            res.Success = true;
            res.BeautifiedTransforms = transforms;
            return res;
        }

        private static void SolveOperatorSubtreeRecursive(
            IdsNode opNode,
            bool isRoot,
            FontFamily font,
            IGlyphSource glyphSource,
            Dictionary<string, MorphologicalProfile> nodeProfiles,
            Dictionary<string, NodeCustomTransform> transforms,
            SolverConfig cfg,
            bool isVietnamese,
            Action<string> reportProgress,
            List<string> matchedSeedsLog,
            List<string> reportBullets,
            ref UnicodeTier highestTierReached,
            ref bool anyExemplarsFound,
            ref float rootTxStar,
            ref float rootTyStar)
        {
            if (opNode == null || !opNode.IsOperator || opNode.Children == null || opNode.Children.Count == 0)
                return;

            string op = opNode.Operator;
            int childCount = opNode.Children.Count;

            // 1. Giải toán tử hiện tại
            var dissectedByNode = new Dictionary<string, List<DissectedExemplarMetrics>>();

            for (int i = 0; i < childCount; i++)
            {
                var child = opNode.Children[i];
                if (child == null) continue;

                // Chỉ hiển thị và dùng chữ tra ngược khi nó nằm trong vùng BMP và thực sự có glyph render được trong font (chặn triệt để ≈□!)
                bool hasRenderableResolved = child.IsOperator &&
                    child.ResolvedCodePoint > 0 &&
                    child.ResolvedCodePoint <= 0xFFFF &&
                    (glyphSource == null || glyphSource.HasGlyph(char.ConvertFromUtf32(child.ResolvedCodePoint)));

                string queryChar = !child.IsOperator
                    ? child.Char
                    : (hasRenderableResolved ? char.ConvertFromUtf32(child.ResolvedCodePoint) : "");

                string displayLabel = !child.IsOperator
                    ? child.Char
                    : (hasRenderableResolved
                        ? string.Format("{0}≈{1}", IdsParserService.AstToIdsString(child), char.ConvertFromUtf32(child.ResolvedCodePoint))
                        : IdsParserService.AstToIdsString(child));

                byte compSubOp = child.IsOperator ? HanIdsCorpusIndex.GetOperatorId(child.Operator) : (byte)0;
                ShapeClass16 compShape = (ShapeClass16)child.EffectiveShapeClassId;
                int compStrokes = child.EffectiveStrokes > 0 ? child.EffectiveStrokes : 5;

                int sibIdx = (i == 0 && childCount > 1) ? 1 : 0;
                var sibNode = (sibIdx < childCount) ? opNode.Children[sibIdx] : null;
                int sibCp = 0;
                if (sibNode != null)
                {
                    if (!sibNode.IsOperator) sibCp = HanIdsCorpusIndex.GetFirstCodePoint(sibNode.Char);
                    else if (sibNode.ResolvedCodePoint > 0) sibCp = sibNode.ResolvedCodePoint;
                }
                byte sibSubOp = (sibNode != null && sibNode.IsOperator) ? HanIdsCorpusIndex.GetOperatorId(sibNode.Operator) : (byte)0;
                ShapeClass16 sibShape = (sibNode != null) ? (ShapeClass16)sibNode.EffectiveShapeClassId : ShapeClass16.SquareBlock;
                int sibStrokes = (sibNode != null && sibNode.EffectiveStrokes > 0) ? sibNode.EffectiveStrokes : 5;

                MorphologicalProfile prof;
                nodeProfiles.TryGetValue(child.Id, out prof);

                if (isRoot)
                {
                    reportProgress(string.Format(
                        isVietnamese
                            ? "Đang truy vấn 4 tầng cho nhánh [{0}/{1}] ({2}) trong 98.000 chữ..."
                            : "Querying 4-tier index for branch [{0}/{1}] ({2}) across 98k corpus...",
                        i + 1, childCount, displayLabel));
                }

                UnicodeTier tier;
                var seeds = DeepTieredSearchContextual(
                    queryChar,
                    op,
                    i,
                    prof,
                    compSubOp,
                    compShape,
                    compStrokes,
                    sibCp,
                    sibSubOp,
                    sibShape,
                    sibStrokes,
                    glyphSource,
                    out tier);

                if (tier > highestTierReached) highestTierReached = tier;

                var dissections = ExhaustiveExemplarDissect(seeds, queryChar, op, i, font, isRoot ? reportProgress : null);
                dissectedByNode[child.Id] = dissections;

                // Trích xuất nét chữ thư pháp thực nghiệm (Calligraphic Donor Allomorph) bằng đối chiếu quang học 2D (Chamfer + Sobel 4 hướng)
                VerifiedDonorResult verifiedDonor = null;
                if (!child.IsOperator && !string.IsNullOrEmpty(child.Char))
                {
                    int leafCp = child.RawCodePoint > 0 ? child.RawCodePoint : HanIdsCorpusIndex.GetFirstCodePoint(child.Char);
                    verifiedDonor = OpticalComponentDonorService.TryFindVerifiedDonor(
                        child.Char,
                        leafCp,
                        op,
                        i,
                        childCount,
                        opNode.ResolvedCodePoint,
                        seeds,
                        font);

                    if (verifiedDonor != null && verifiedDonor.DonorExemplarCodePoint > 0)
                    {
                        var tc = transforms[child.Id];
                        tc.DonorExemplarCodePoint = verifiedDonor.DonorExemplarCodePoint;
                        tc.DonorContourIndices = verifiedDonor.DonorContourIndices;
                        tc.DonorNaturalAspect = verifiedDonor.DonorNaturalAspect;
                        transforms[child.Id] = tc;
                    }
                }

                if (seeds.Count > 0)
                {
                    anyExemplarsFound = true;
                    string donorNote = (verifiedDonor != null && verifiedDonor.DonorExemplarCodePoint > 0)
                        ? string.Format(
                            isVietnamese
                                ? " | Khớp nét thư pháp từ '{0}' U+{1:X4} (Chamfer {2:F0}%)"
                                : " | Extracted calligraphic strokes from '{0}' U+{1:X4} (Chamfer {2:F0}%)",
                            verifiedDonor.DonorCharacter,
                            verifiedDonor.DonorExemplarCodePoint,
                            verifiedDonor.ChamferSimilarity * 100f)
                        : "";

                    if (isRoot)
                    {
                        var sampleStr = string.Join(", ", seeds.Take(4).Select(s => string.Format("{0} U+{1:X4}", s.Character, s.CodePoint)));
                        matchedSeedsLog.Add(string.Format(
                            isVietnamese
                                ? "• Nhánh [{0}] ({1}): Đối chiếu {2} mẫu ({3}... - Tầng {4}){5}"
                                : "• Branch [{0}] ({1}): Cross-referenced {2} samples ({3}... - Tier {4}){5}",
                            i + 1, displayLabel, seeds.Count, sampleStr, tier, donorNote));
                    }
                    else if (verifiedDonor != null && verifiedDonor.DonorExemplarCodePoint > 0)
                    {
                        matchedSeedsLog.Add(string.Format(
                            isVietnamese
                                ? "  ↳ Nhánh con ({0} trong {1}): Khớp nét thư pháp từ '{2}' U+{3:X4} (Chamfer {4:F0}%)"
                                : "  ↳ Sub-branch ({0} in {1}): Extracted calligraphic strokes from '{2}' U+{3:X4} (Chamfer {4:F0}%)",
                            displayLabel, op, verifiedDonor.DonorCharacter, verifiedDonor.DonorExemplarCodePoint, verifiedDonor.ChamferSimilarity * 100f));
                    }
                }
            }

            // Tính tiên nghiệm giải tích (Analytical Prior) và Co rút Empirical Bayes
            IdsNode c0 = childCount > 0 ? opNode.Children[0] : null;
            IdsNode c1 = childCount > 1 ? opNode.Children[1] : null;
            MorphologicalProfile p0 = (c0 != null && nodeProfiles.ContainsKey(c0.Id)) ? nodeProfiles[c0.Id] : new MorphologicalProfile();
            MorphologicalProfile p1 = (c1 != null && nodeProfiles.ContainsKey(c1.Id)) ? nodeProfiles[c1.Id] : new MorphologicalProfile();

            float rPrior = ComputeAnalyticalOperatorPrior(op, c0, c1, p0, p1, cfg);
            var blueprint = BuildConsensusBlueprintWithPrior(dissectedByNode, op, rPrior, cfg);

            if (isRoot)
            {
                if (blueprint.TxStar > 20f) rootTxStar = blueprint.TxStar;
                if (blueprint.TyStar > 20f) rootTyStar = blueprint.TyStar;
            }

            var tOp = transforms[opNode.Id];
            if (op != "⿲" && op != "⿳")
            {
                tOp.SplitRatio = (float)Math.Round(blueprint.ConsensusSplitRatio, 2);
                transforms[opNode.Id] = tOp;

                if (isRoot)
                {
                    reportBullets.Add(string.Format(
                        isVietnamese
                            ? "• Toán tử {0}: Tỷ lệ phân chia đồng thuận {1}% : {2}% (Co rút Bayes α={3:F2}, Khe chuẩn {4:F0} units)"
                            : "• Operator {0}: Consensus split ratio {1}% : {2}% (Bayes α={3:F2}, Clearance {4:F0} units)",
                        op, (int)Math.Round(tOp.SplitRatio.Value * 100), 100 - (int)Math.Round(tOp.SplitRatio.Value * 100), blueprint.ShrinkageAlpha, blueprint.ConsensusClearanceGap));
                }
            }

            // Áp dụng quy tắc chuyên biệt cho từng họ toán tử (⿰, ⿱, ⿲, ⿳, ⿴..⿺, ⿻)
            if (childCount >= 2 && c0 != null && c1 != null)
            {
                if (op == "⿰")
                {
                    var t0 = transforms[c0.Id];
                    var t1 = transforms[c1.Id];

                    ShapeClass16 sc0 = (ShapeClass16)c0.EffectiveShapeClassId;
                    ShapeClass16 sc1 = (ShapeClass16)c1.EffectiveShapeClassId;

                    // Khi nhánh trái là bộ thủ đơn và nhánh phải là cụm phức hợp (như ⿰女⿱天土), khóa trần tỷ lệ bộ trái <= 41%
                    if (!c0.IsOperator && c1.IsOperator && c0.EffectiveStrokes <= 5 && tOp.SplitRatio.HasValue && tOp.SplitRatio.Value > 0.41f)
                    {
                        tOp.SplitRatio = 0.40f;
                        transforms[opNode.Id] = tOp;
                    }

                    // Quy tắc "Nhật biên / Khẩu biên thượng đề" (như chữ 明 ⿰日月: bộ 日/口 bên trái thu gọn ~78% chiều cao và nhấc nhẹ lên trên, thân phải giữ nguyên 100% chiều cao)
                    bool isLeftEnclosedBox = !c0.IsOperator && c0.EffectiveStrokes <= 5 && p0.NHoles >= 1 && p0.WTop >= 0.45f && p0.WBot >= 0.45f && sc0 == ShapeClass16.SquareBlock;
                    bool isRightEnclosedBox = !c1.IsOperator && c1.EffectiveStrokes <= 3 && p1.NHoles >= 1 && p1.WTop >= 0.45f && p1.WBot >= 0.45f && sc1 == ShapeClass16.SquareBlock && c1.EffectiveStrokes < c0.EffectiveStrokes;

                    // Căn tâm quang học theo phương dọc giữa 2 nhánh trái-phải
                    PointF opt0 = p0.OpticalCentroidNormalized;
                    PointF opt1 = p1.OpticalCentroidNormalized;
                    int optDeltaY = (int)Math.Round((opt0.Y - opt1.Y) * 90f);
                    optDeltaY = Math.Max(-18, Math.Min(18, optDeltaY));

                    t1.OffsetY += optDeltaY / 2;
                    t0.OffsetY -= optDeltaY / 2;

                    if (isLeftEnclosedBox && !isRightEnclosedBox)
                    {
                        float boxScaleY = (c0.EffectiveStrokes <= 3 && p0.NHoles == 1) ? 0.68f : 0.78f;
                        int boxLiftY = (c0.EffectiveStrokes <= 3 && p0.NHoles == 1) ? -18 : -12;
                        t0.ScaleY = boxScaleY;
                        t0.OffsetY = Math.Max(-26, Math.Min(16, t0.OffsetY + boxLiftY));
                    }

                    float dShift = Stage5_ComputeSkylineKerning(p0.RightSkyline, p1.LeftSkyline, 1000f * tOp.SplitRatio.Value, 1000f * (1f - tOp.SplitRatio.Value), cfg);

                    if (dShift > 2f)
                    {
                        float deltaX = Math.Min(dShift, 64f);
                        t1.OffsetX = -(int)Math.Round(deltaX * 0.60f);
                        t0.OffsetX = +(int)Math.Round(deltaX * 0.40f);
                        if (isRoot)
                        {
                            reportBullets.Add(string.Format(
                                isVietnamese
                                    ? "• Khớp mộng đường chân trời 64 cột: Kéo lồng {0}px (Khe thực {1:F0} units)"
                                    : "• 64-Bin Skyline Interlocking: Nested {0}px (Actual gap {1:F0} units)",
                                Math.Abs(t1.OffsetX), blueprint.ConsensusClearanceGap));
                        }
                    }
                    else if (isRoot)
                    {
                        reportBullets.Add(isVietnamese
                            ? "• Khớp mộng đường chân trời 64 cột: Bố cục đã đạt độ khít tự nhiên tối ưu"
                            : "• 64-Bin Skyline Interlocking: Natural clearance achieved");
                    }

                    transforms[c0.Id] = t0;
                    transforms[c1.Id] = t1;
                }
                else if (op == "⿱")
                {
                    float dShift = Stage5_ComputeSkylineKerning(p0.BottomSkyline, p1.TopSkyline, 1000f * tOp.SplitRatio.Value, 1000f * (1f - tOp.SplitRatio.Value), cfg);

                    // Căn tâm quang học theo phương ngang giữa 2 nhánh trên-dưới
                    PointF opt0 = p0.OpticalCentroidNormalized;
                    PointF opt1 = p1.OpticalCentroidNormalized;
                    int optDeltaX = (int)Math.Round((opt0.X - opt1.X) * 90f);
                    optDeltaX = Math.Max(-18, Math.Min(18, optDeltaX));

                    var t0 = transforms[c0.Id];
                    var t1 = transforms[c1.Id];
                    t1.OffsetX += optDeltaX / 2;
                    t0.OffsetX -= optDeltaX / 2;

                    float effectiveVShift = !isRoot ? Math.Max(dShift, 16f) : dShift;
                    if (effectiveVShift > 2f)
                    {
                        float deltaY = Math.Min(effectiveVShift, 64f);
                        t1.OffsetY = -(int)Math.Round(deltaY * 0.60f);
                        t0.OffsetY = +(int)Math.Round(deltaY * 0.40f);
                        if (isRoot)
                        {
                            reportBullets.Add(string.Format(
                                isVietnamese
                                    ? "• Khớp mộng đường chân trời 64 hàng: Kéo lồng {0}px theo phương dọc"
                                    : "• 64-Bin Skyline Interlocking: Nested {0}px vertically",
                                Math.Abs(t1.OffsetY)));
                        }
                    }
                    else if (isRoot)
                    {
                        reportBullets.Add(isVietnamese
                            ? "• Khớp mộng đường chân trời 64 hàng: Bố cục đã đạt độ khít tự nhiên tối ưu"
                            : "• 64-Bin Skyline Interlocking: Natural clearance achieved");
                    }

                    // Điều chỉnh sải ngang nếu có bệ đỡ (Wide Base) hoặc mái che (Wide Roof)
                    ShapeClass16 sc0 = (ShapeClass16)c0.EffectiveShapeClassId;
                    ShapeClass16 sc1 = (ShapeClass16)c1.EffectiveShapeClassId;

                    if (p1.IsWideBase || sc1 == ShapeClass16.WideBase || (p1.WBot >= 0.70f && !p0.IsRoof && sc0 != ShapeClass16.FlatBar))
                    {
                        if (sc0 != ShapeClass16.FlatBar && (isRoot || p0.WBot < 0.72f))
                        {
                            t0.ScaleX = cfg.STopDefault;
                        }
                        t1.ScaleX = cfg.SBotDefault;
                        if (isRoot)
                        {
                            reportBullets.Add(string.Format(
                                isVietnamese
                                    ? "• Bệ đỡ ngang đồng thuận: Thu gọn đỉnh {0}% để đáy mở rộng {1}% làm bệ đỡ vững"
                                    : "• Base-Anchor Harmony: Tucked top to {0}% so wide bottom anchors at {1}%",
                                (int)(cfg.STopDefault * 100), (int)(cfg.SBotDefault * 100)));
                        }
                    }

                    // Kẹp đàn hồi riêng cho thanh ngang dẹt (FlatBar: như 一 trong ⿱一木 hoặc ⿱木一)
                    if (sc0 == ShapeClass16.FlatBar && sc1 != ShapeClass16.FlatBar)
                    {
                        tOp.SplitRatio = (float)Math.Round(Math.Min(tOp.SplitRatio ?? 0.22f, 0.22f), 2);
                        transforms[opNode.Id] = tOp;
                        t0.ScaleY = Math.Min(t0.ScaleY, 0.85f);
                        t0.ScaleX = Math.Min(t0.ScaleX, 0.88f);
                        t0.OffsetY = Math.Max(t0.OffsetY, 24);
                        t1.OffsetY = Math.Min(t1.OffsetY, -14);
                    }
                    else if (sc1 == ShapeClass16.FlatBar && sc0 != ShapeClass16.FlatBar)
                    {
                        tOp.SplitRatio = (float)Math.Round(Math.Max(tOp.SplitRatio ?? 0.78f, 0.78f), 2);
                        transforms[opNode.Id] = tOp;
                        t1.ScaleY = Math.Min(t1.ScaleY, 0.85f);
                        t1.OffsetY = Math.Min(t1.OffsetY, -24);
                        t0.OffsetY = Math.Max(t0.OffsetY, 14);
                    }

                    transforms[c0.Id] = t0;
                    transforms[c1.Id] = t1;
                }
                else if (op == "⿲" && childCount >= 3)
                {
                    var c2 = opNode.Children[2];

                    float gamma = cfg.Gamma;
                    float w0 = (float)Math.Pow(Math.Max(1, c0.EffectiveStrokes), gamma);
                    float w1 = (float)Math.Pow(Math.Max(1, c1.EffectiveStrokes), gamma);
                    float w2 = (float)Math.Pow(Math.Max(1, c2 != null ? c2.EffectiveStrokes : 4), gamma);
                    float sumPrior = Math.Max(0.01f, w0 + w1 + w2);

                    float pR0 = Math.Max(cfg.TernaryMin, w0 / sumPrior);
                    float pR1 = Math.Max(cfg.TernaryMin, w1 / sumPrior);
                    float pR2 = Math.Max(cfg.TernaryMin, w2 / sumPrior);

                    // Kết hợp tỷ lệ thực nghiệm từ các chữ mẫu 3 cột (như 樹 U+6A39, 街 U+8857, 謝 U+8B1D)
                    float eR0 = pR0, eR1 = pR1, eR2 = pR2;
                    List<DissectedExemplarMetrics> l0, l1, l2;
                    if (dissectedByNode.TryGetValue(c0.Id, out l0) && l0 != null && l0.Count > 0)
                        eR0 = ComputeTrimmedWeightedMean(l0, m => m.RealSplitRatio);
                    if (dissectedByNode.TryGetValue(c1.Id, out l1) && l1 != null && l1.Count > 0)
                        eR1 = ComputeTrimmedWeightedMean(l1, m => m.RealSplitRatio);
                    if (c2 != null && dissectedByNode.TryGetValue(c2.Id, out l2) && l2 != null && l2.Count > 0)
                        eR2 = ComputeTrimmedWeightedMean(l2, m => m.RealSplitRatio);

                    float alpha3 = blueprint.ShrinkageAlpha;
                    float r0 = Math.Max(cfg.TernaryMin, alpha3 * eR0 + (1f - alpha3) * pR0);
                    float r1 = Math.Max(cfg.TernaryMin, alpha3 * eR1 + (1f - alpha3) * pR1);
                    float r2 = Math.Max(cfg.TernaryMin, alpha3 * eR2 + (1f - alpha3) * pR2);
                    float norm = Math.Max(0.01f, r0 + r1 + r2);
                    r0 /= norm;
                    r1 /= norm;
                    r2 /= norm;

                    tOp.SplitRatios3 = new float[] { (float)Math.Round(r0, 2), (float)Math.Round(r1, 2), (float)Math.Round(r2, 2) };
                    tOp.SplitRatio = tOp.SplitRatios3[0];
                    transforms[opNode.Id] = tOp;

                    if (isRoot)
                    {
                        int pct0 = (int)Math.Round(r0 * 100);
                        int pct1 = (int)Math.Round(r1 * 100);
                        int pct2 = Math.Max(1, 100 - pct0 - pct1);
                        reportBullets.Add(string.Format(
                            isVietnamese
                                ? "• Toán tử ⿲ (3 cột): Tỷ lệ phân bổ đồng thuận {0}% : {1}% : {2}% (Co rút Bayes α={3:F2}, Khe chuẩn {4:F0} units)"
                                : "• Operator ⿲ (3-column): Consensus split {0}% : {1}% : {2}% (Bayes α={3:F2}, Clearance {4:F0} units)",
                            pct0, pct1, pct2, alpha3, blueprint.ConsensusClearanceGap));
                    }
                }
                else if (op == "⿳" && childCount >= 3)
                {
                    var c2 = opNode.Children[2];
                    float gamma = cfg.Gamma;
                    float h0 = (float)Math.Pow(Math.Max(1, c0.EffectiveStrokes), gamma);
                    float h1 = (float)Math.Pow(Math.Max(1, c1.EffectiveStrokes), gamma);
                    float h2 = (float)Math.Pow(Math.Max(1, c2 != null ? c2.EffectiveStrokes : 4), gamma);
                    float sumPrior = Math.Max(0.01f, h0 + h1 + h2);

                    float pR0 = Math.Max(cfg.TernaryMin, h0 / sumPrior);
                    float pR1 = Math.Max(cfg.TernaryMin, h1 / sumPrior);
                    float pR2 = Math.Max(cfg.TernaryMin, h2 / sumPrior);

                    float eR0 = pR0, eR1 = pR1, eR2 = pR2;
                    List<DissectedExemplarMetrics> l0, l1, l2;
                    if (dissectedByNode.TryGetValue(c0.Id, out l0) && l0 != null && l0.Count > 0)
                        eR0 = ComputeTrimmedWeightedMean(l0, m => m.RealSplitRatio);
                    if (dissectedByNode.TryGetValue(c1.Id, out l1) && l1 != null && l1.Count > 0)
                        eR1 = ComputeTrimmedWeightedMean(l1, m => m.RealSplitRatio);
                    if (c2 != null && dissectedByNode.TryGetValue(c2.Id, out l2) && l2 != null && l2.Count > 0)
                        eR2 = ComputeTrimmedWeightedMean(l2, m => m.RealSplitRatio);

                    float alpha3 = blueprint.ShrinkageAlpha;
                    float r0 = Math.Max(cfg.TernaryMin, alpha3 * eR0 + (1f - alpha3) * pR0);
                    float r1 = Math.Max(cfg.TernaryMin, alpha3 * eR1 + (1f - alpha3) * pR1);
                    float r2 = Math.Max(cfg.TernaryMin, alpha3 * eR2 + (1f - alpha3) * pR2);
                    float norm = Math.Max(0.01f, r0 + r1 + r2);
                    r0 /= norm;
                    r1 /= norm;
                    r2 /= norm;

                    tOp.SplitRatios3 = new float[] { (float)Math.Round(r0, 2), (float)Math.Round(r1, 2), (float)Math.Round(r2, 2) };
                    tOp.SplitRatio = tOp.SplitRatios3[0];
                    transforms[opNode.Id] = tOp;

                    if (isRoot)
                    {
                        int pct0 = (int)Math.Round(r0 * 100);
                        int pct1 = (int)Math.Round(r1 * 100);
                        int pct2 = Math.Max(1, 100 - pct0 - pct1);
                        reportBullets.Add(string.Format(
                            isVietnamese
                                ? "• Toán tử ⿳ (3 tầng): Tỷ lệ phân bổ đồng thuận {0}% : {1}% : {2}% (Co rút Bayes α={3:F2}, Khe chuẩn {4:F0} units)"
                                : "• Operator ⿳ (3-tier): Consensus split {0}% : {1}% : {2}% (Bayes α={3:F2}, Clearance {4:F0} units)",
                            pct0, pct1, pct2, alpha3, blueprint.ConsensusClearanceGap));
                    }
                }
                else if ("⿴⿵⿶⿷⿸⿹⿺⿼⿽".Contains(op))
                {
                    float lambda = cfg.LambdaSurround;
                    string outerChar = !c0.IsOperator ? c0.Char : (c0.ResolvedCodePoint > 0 ? char.ConvertFromUtf32(c0.ResolvedCodePoint) : "");
                    RectangleF cavity = Stage4_FindLargestEmptyCavity(outerChar, op, font, cfg);

                    // Lưu ý: IdsLayoutEngine đã đặt sẵn slot mặc định cho c1 của toán tử bao quanh (khoảng 0.64..0.70 em-box),
                    // nên ta hiệu chỉnh ScaleX/ScaleY tương đối quanh 1.0f để tránh nhân kép làm nhân trong bị quá nhỏ!
                    RectangleF defaultInnerSlot = GetDefaultEnclosureInnerSlot(op);
                    if (cavity.Width > 50 && cavity.Height > 50 && defaultInnerSlot.Width > 0 && defaultInnerSlot.Height > 0)
                    {
                        var tInner = transforms[c1.Id];
                        float relScaleX = (cavity.Width / defaultInnerSlot.Width) * (0.85f + 0.15f * lambda);
                        float relScaleY = (cavity.Height / defaultInnerSlot.Height) * (0.85f + 0.15f * lambda);
                        tInner.ScaleX = (float)Math.Round(Math.Max(0.78f, Math.Min(1.15f, relScaleX)), 2);
                        tInner.ScaleY = (float)Math.Round(Math.Max(0.78f, Math.Min(1.15f, relScaleY)), 2);

                        float cavCx = cavity.X + cavity.Width / 2f;
                        float cavCy = cavity.Y + cavity.Height / 2f;
                        float slotCx = defaultInnerSlot.X + defaultInnerSlot.Width / 2f;
                        float slotCy = defaultInnerSlot.Y + defaultInnerSlot.Height / 2f;

                        tInner.OffsetX = (int)Math.Round(Math.Max(-28f, Math.Min(28f, (cavCx - slotCx) * 0.5f)));
                        tInner.OffsetY = (int)Math.Round(Math.Max(-28f, Math.Min(28f, (cavCy - slotCy) * 0.5f)));
                        transforms[c1.Id] = tInner;
                    }

                    if (isRoot)
                    {
                        reportBullets.Add(string.Format(
                            isVietnamese
                                ? "• Tối ưu cấu trúc bao quanh: Định vị nhân trong [{0:F0}x{1:F0}] khớp mộng hoàn hảo"
                                : "• Enclosure structure optimization: Positioned inner core [{0:F0}x{1:F0}] precisely into cavity",
                            cavity.Width, cavity.Height));
                    }
                }
                else if (op == "⿻" || op == "㇯")
                {
                    // Toán tử chồng khít (Overlaid ⿻): Khóa trùng tâm quang học, tắt hoàn toàn lực đẩy khe trắng (G_min = 0)
                    var t0 = transforms[c0.Id];
                    var t1 = transforms[c1.Id];
                    t0.OffsetX = 0;
                    t0.OffsetY = 0;
                    t1.OffsetX = 0;
                    t1.OffsetY = 0;
                    transforms[c0.Id] = t0;
                    transforms[c1.Id] = t1;

                    if (isRoot)
                    {
                        reportBullets.Add(isVietnamese
                            ? "• Toán tử giao nhau (⿻): Khóa đồng tâm quang học hai thành phần (Khe hở = 0)"
                            : "• Overlaid operator (⿻): Concentric optical centroid lock (Gutter = 0)");
                    }
                }
            }

            // 2. Giải đệ quy xuống các nhánh con là toán tử lồng bên trong (như ⿱一木 bên trong ⿰亻⿱一木)
            for (int i = 0; i < childCount; i++)
            {
                var child = opNode.Children[i];
                if (child != null && child.IsOperator)
                {
                    SolveOperatorSubtreeRecursive(
                        child,
                        false,
                        font,
                        glyphSource,
                        nodeProfiles,
                        transforms,
                        cfg,
                        isVietnamese,
                        reportProgress,
                        matchedSeedsLog,
                        reportBullets,
                        ref highestTierReached,
                        ref anyExemplarsFound,
                        ref rootTxStar,
                        ref rootTyStar);
                }
            }
        }

        private static RectangleF GetDefaultEnclosureInnerSlot(string op)
        {
            if (op == "⿴") return new RectangleF(180f, 180f, 640f, 640f);
            if (op == "⿵") return new RectangleF(180f, 260f, 640f, 680f);
            if (op == "⿶") return new RectangleF(180f, 60f, 640f, 680f);
            if (op == "⿷") return new RectangleF(240f, 160f, 700f, 680f);
            if (op == "⿸") return new RectangleF(260f, 260f, 700f, 700f);
            if (op == "⿹") return new RectangleF(60f, 260f, 700f, 700f);
            if (op == "⿺") return new RectangleF(260f, 60f, 700f, 700f);
            return new RectangleF(200f, 200f, 600f, 600f);
        }

        private static Dictionary<string, NodeCustomTransform> CloneTransforms(Dictionary<string, NodeCustomTransform> src)
        {
            var res = new Dictionary<string, NodeCustomTransform>();
            if (src != null)
            {
                foreach (var kvp in src) res[kvp.Key] = kvp.Value.Clone();
            }
            return res;
        }

        private static void ApplyFinalSafeBoxCentering(
            IdsNode root,
            Dictionary<string, NodeCustomTransform> transforms,
            FontFamily font,
            RectangleF safeBox,
            List<string> reportBulletPoints,
            bool isVietnamese)
        {
            Action<IdsNode> apply = null;
            apply = n =>
            {
                if (n == null) return;
                NodeCustomTransform t;
                if (transforms.TryGetValue(n.Id, out t)) n.Transform = t;
                if (n.Children != null) { foreach (var c in n.Children) apply(c); }
            };
            apply(root);

            var items = IdsLayoutEngine.ComputeLayout(root, new RectangleF(0, 0, 1000f, 1000f));
            if (items.Count == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var it in items)
            {
                if (string.IsNullOrEmpty(it.Char) || it.Char == "□") continue;
                var p = Stage2_ExtractLeafProfile(it.Char, new GdiGlyphSource(font));
                if (p != null)
                {
                    float l = it.TargetRect.Left + it.TargetRect.Width * (p.InkBBox.Left / 1000f);
                    float r = it.TargetRect.Left + it.TargetRect.Width * (p.InkBBox.Right / 1000f);
                    float t = it.TargetRect.Top + it.TargetRect.Height * (p.InkBBox.Top / 1000f);
                    float b = it.TargetRect.Top + it.TargetRect.Height * (p.InkBBox.Bottom / 1000f);

                    if (l < minX) minX = l;
                    if (r > maxX) maxX = r;
                    if (t < minY) minY = t;
                    if (b > maxY) maxY = b;
                }
            }

            if (maxX > minX && maxY > minY)
            {
                float cxInk = (minX + maxX) / 2f;
                float cyInk = (minY + maxY) / 2f;
                float cxSafe = safeBox.X + safeBox.Width / 2f;
                float cySafe = safeBox.Y + safeBox.Height / 2f;

                float shiftX = Math.Max(-35f, Math.Min(35f, cxSafe - cxInk));
                float shiftY = Math.Max(-35f, Math.Min(35f, cySafe - cyInk));

                if (Math.Abs(shiftX) > 2f || Math.Abs(shiftY) > 2f)
                {
                    // Chỉ dịch chuyển các nút lá để tránh cộng dồn cả nút cha lẫn nút con
                    foreach (var it in items)
                    {
                        NodeCustomTransform t;
                        if (transforms.TryGetValue(it.NodeId, out t))
                        {
                            t.OffsetX += (int)Math.Round(shiftX);
                            t.OffsetY += (int)Math.Round(shiftY);
                            transforms[it.NodeId] = t;
                        }
                    }
                }
            }

            if (reportBulletPoints != null)
            {
                reportBulletPoints.Add(string.Format(
                    isVietnamese
                        ? "• Chuẩn hóa khung an toàn Mễ Tự Cách [{0:F0}, {1:F0}, {2:F0}, {3:F0}]: Cân bằng trọng tâm mực"
                        : "• Safe-frame Normalization [{0:F0}, {1:F0}, {2:F0}, {3:F0}]: Ink centroid balanced",
                    safeBox.Left, safeBox.Top, safeBox.Right, safeBox.Bottom));
            }
        }

        // ====================================================================================
        // CÁC HÀM TIỆN ÍCH TRÍCH XUẤT HÌNH THÁI HỌC VÀ ĐO ĐẠC RASTER
        // ====================================================================================
        public static MorphologicalProfile Stage2_ExtractLeafProfile(string ch, IGlyphSource glyphSource)
        {
            if (string.IsNullOrEmpty(ch)) return new MorphologicalProfile();

            string fontKey = glyphSource != null ? glyphSource.FontKey : "default";
            string cacheKey = fontKey + "_" + ch;

            lock (CacheLock)
            {
                MorphologicalProfile cached;
                if (ProfileCache.TryGetValue(cacheKey, out cached)) return cached;
            }

            var profile = new MorphologicalProfile();
            RectangleF dummyBox;
            using (var bmp256 = glyphSource.RenderGlyphBitmap(ch, 256, out dummyBox))
            {
                if (bmp256 == null) return profile;

                int holeCount;
                float minHoleWidth, rhoWhite;
                MeasureInternalHoles(bmp256, out holeCount, out minHoleWidth, out rhoWhite);
                profile.NHoles = holeCount;
                profile.MinHoleWidth = minHoleWidth;
                profile.RhoWhite = rhoWhite;

                float wx0, wy0;
                MeasureStrokeThickness(bmp256, out wx0, out wy0);
                profile.Wx0 = wx0;
                profile.Wy0 = wy0;

                bool[,] grid64 = DownsampleToBinaryGrid(bmp256, 64);
                MeasureSkylinesAndSpans(grid64, profile);

                float sx, sy;
                MeasureZeroCrossings(grid64, out sx, out sy);
                profile.Sx = Math.Max(1.0f, sx);
                profile.Sy = Math.Max(1.0f, sy);

                float sumX = 0f, sumY = 0f, countInk = 0f;
                for (int r = 0; r < 64; r++)
                {
                    for (int c = 0; c < 64; c++)
                    {
                        if (grid64[r, c])
                        {
                            sumX += c;
                            sumY += r;
                            countInk++;
                        }
                    }
                }
                profile.Cx = (countInk > 0) ? Math.Max(0.05f, Math.Min(0.95f, (sumX / countInk) / 63f)) : 0.5f;
                profile.Cy = (countInk > 0) ? Math.Max(0.05f, Math.Min(0.95f, (sumY / countInk) / 63f)) : 0.5f;
            }

            lock (CacheLock)
            {
                ProfileCache[cacheKey] = profile;
            }

            return profile;
        }

        private static void MeasureInternalHoles(Bitmap bmp256, out int holeCount, out float minHoleWidth, out float rhoWhite)
        {
            holeCount = CountHolesOnRaster(bmp256);
            minHoleWidth = 40.0f;
            rhoWhite = 0.75f;
        }

        private static void MeasureStrokeThickness(Bitmap bmp256, out float wx0, out float wy0)
        {
            wx0 = 68.0f;
            wy0 = 68.0f;
            Rectangle ink = FindInkBoundsPixel(bmp256);
            MeasureSlotStrokeThickness(bmp256, ink, out wx0, out wy0);
        }

        private static bool[,] DownsampleToBinaryGrid(Bitmap bmp, int sz)
        {
            var grid = new bool[sz, sz];
            int w = bmp.Width;
            int h = bmp.Height;
            float stepX = (float)w / sz;
            float stepY = (float)h / sz;

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bd.Scan0;
                    int stride = bd.Stride;
                    for (int r = 0; r < sz; r++)
                    {
                        int py = Math.Min(h - 1, (int)(r * stepY));
                        byte* row = ptr + py * stride;
                        for (int c = 0; c < sz; c++)
                        {
                            int px = Math.Min(w - 1, (int)(c * stepX));
                            if (row[px * 4 + 2] < 180 || row[px * 4 + 1] < 180 || row[px * 4] < 180)
                            {
                                grid[r, c] = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return grid;
        }

        private static void MeasureSkylinesAndSpans(bool[,] grid, MorphologicalProfile p)
        {
            int sz = 64;
            int minX = sz, maxX = -1, minY = sz, maxY = -1;

            for (int r = 0; r < sz; r++)
            {
                p.LeftSkyline[r] = 1.0f;
                p.RightSkyline[r] = 1.0f;
                for (int c = 0; c < sz; c++)
                {
                    if (grid[r, c])
                    {
                        if (p.LeftSkyline[r] == 1.0f) p.LeftSkyline[r] = (float)c / sz;
                        p.RightSkyline[r] = (float)(sz - 1 - c) / sz;
                        if (c < minX) minX = c;
                        if (c > maxX) maxX = c;
                        if (r < minY) minY = r;
                        if (r > maxY) maxY = r;
                    }
                }
            }

            for (int c = 0; c < sz; c++)
            {
                p.TopSkyline[c] = 1.0f;
                p.BottomSkyline[c] = 1.0f;
                for (int r = 0; r < sz; r++)
                {
                    if (grid[r, c])
                    {
                        if (p.TopSkyline[c] == 1.0f) p.TopSkyline[c] = (float)r / sz;
                        p.BottomSkyline[c] = (float)(sz - 1 - r) / sz;
                    }
                }
            }

            if (maxX >= minX && maxY >= minY)
            {
                int inkW = Math.Max(1, maxX - minX + 1);
                int inkH = Math.Max(1, maxY - minY + 1);
                p.InkBBox = new RectangleF(minX * 1000f / sz, minY * 1000f / sz, inkW * 1000f / sz, inkH * 1000f / sz);
                p.Aspect = (float)inkW / inkH;
                p.WTop = MeasureSpanInBand(grid, minY, minY + Math.Max(1, (int)(inkH * 0.28f)), minX, maxX, inkW);
                p.WMid = MeasureSpanInBand(grid, minY + (int)(inkH * 0.35f), minY + Math.Max(1, (int)(inkH * 0.65f)), minX, maxX, inkW);
                p.WBot = MeasureSpanInBand(grid, minY + (int)(inkH * 0.72f), maxY + 1, minX, maxX, inkW);
            }
            else
            {
                p.InkBBox = new RectangleF(100f, 100f, 800f, 800f);
                p.Aspect = 1.0f;
                p.WTop = 0.8f;
                p.WMid = 0.8f;
                p.WBot = 0.8f;
            }
        }

        private static float MeasureSpanInBand(bool[,] grid, int startY, int endY, int minX, int maxX, int inkW)
        {
            int sz = 64;
            float maxSpan = 0f;
            for (int y = Math.Max(0, startY); y < endY && y < sz; y++)
            {
                int first = -1, last = -1;
                for (int x = Math.Max(0, minX); x <= maxX && x < sz; x++)
                {
                    if (grid[y, x])
                    {
                        if (first == -1) first = x;
                        last = x;
                    }
                }
                if (first != -1 && last >= first)
                {
                    float span = (float)(last - first + 1) / Math.Max(1, inkW);
                    if (span > maxSpan) maxSpan = span;
                }
            }
            return maxSpan;
        }

        private static void MeasureZeroCrossings(bool[,] grid, out float sx, out float sy)
        {
            int sz = 64;
            int totalCrossH = 0;
            int totalCrossV = 0;

            for (int y = 0; y < sz; y++)
            {
                bool prev = false;
                for (int x = 0; x < sz; x++)
                {
                    if (grid[y, x] != prev) { if (grid[y, x]) totalCrossH++; prev = grid[y, x]; }
                }
            }

            for (int x = 0; x < sz; x++)
            {
                bool prev = false;
                for (int y = 0; y < sz; y++)
                {
                    if (grid[y, x] != prev) { if (grid[y, x]) totalCrossV++; prev = grid[y, x]; }
                }
            }

            sx = (float)totalCrossH / sz;
            sy = (float)totalCrossV / sz;
        }

        public static MorphologicalProfile Stage2_AggregateOperatorProfile(string op, List<MorphologicalProfile> childProfiles, SolverConfig cfg)
        {
            var p = new MorphologicalProfile();
            if (childProfiles == null || childProfiles.Count == 0) return p;

            p.Sx = childProfiles.Average(c => c.Sx);
            p.Sy = childProfiles.Average(c => c.Sy);
            p.Wx0 = childProfiles.Average(c => c.Wx0);
            p.Wy0 = childProfiles.Average(c => c.Wy0);
            p.NHoles = childProfiles.Sum(c => c.NHoles);

            if (op == "⿰" && childProfiles.Count >= 2)
            {
                p.LeftSkyline = (float[])childProfiles[0].LeftSkyline.Clone();
                p.RightSkyline = (float[])childProfiles[1].RightSkyline.Clone();
                for (int i = 0; i < 64; i++)
                {
                    int srcIdx = Math.Min(63, i * 2);
                    p.TopSkyline[i] = ( i < 32 ) ? childProfiles[0].TopSkyline[srcIdx] : childProfiles[1].TopSkyline[Math.Min(63, (i - 32) * 2)];
                    p.BottomSkyline[i] = ( i < 32 ) ? childProfiles[0].BottomSkyline[srcIdx] : childProfiles[1].BottomSkyline[Math.Min(63, (i - 32) * 2)];
                }
                p.WTop = Math.Max(childProfiles[0].WTop, childProfiles[1].WTop);
                p.WBot = Math.Max(childProfiles[0].WBot, childProfiles[1].WBot);
            }
            else if (op == "⿱" && childProfiles.Count >= 2)
            {
                p.TopSkyline = (float[])childProfiles[0].TopSkyline.Clone();
                p.BottomSkyline = (float[])childProfiles[1].BottomSkyline.Clone();
                for (int i = 0; i < 64; i++)
                {
                    int srcIdx = Math.Min(63, i * 2);
                    p.LeftSkyline[i] = ( i < 32 ) ? childProfiles[0].LeftSkyline[srcIdx] : childProfiles[1].LeftSkyline[Math.Min(63, (i - 32) * 2)];
                    p.RightSkyline[i] = ( i < 32 ) ? childProfiles[0].RightSkyline[srcIdx] : childProfiles[1].RightSkyline[Math.Min(63, (i - 32) * 2)];
                }
                p.WTop = childProfiles[0].WTop;
                p.WBot = childProfiles[1].WBot;
            }
            else
            {
                p.TopSkyline = (float[])childProfiles[0].TopSkyline.Clone();
                p.BottomSkyline = (float[])childProfiles[0].BottomSkyline.Clone();
                p.LeftSkyline = (float[])childProfiles[0].LeftSkyline.Clone();
                p.RightSkyline = (float[])childProfiles[0].RightSkyline.Clone();
            }

            p.Aspect = 1.0f;
            return p;
        }

        public static int CountHolesOnRaster(Bitmap bmp)
        {
            if (bmp == null) return 0;
            int w = bmp.Width;
            int h = bmp.Height;

            int pw = w + 2;
            int ph = h + 2;
            bool[,] isInk = new bool[pw, ph];

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bd.Scan0;
                    int stride = bd.Stride;
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            byte r = row[x * 4 + 2];
                            byte g = row[x * 4 + 1];
                            byte b = row[x * 4];
                            if (r < 180 || g < 180 || b < 180)
                            {
                                isInk[x + 1, y + 1] = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }

            bool[,] visitedExterior = new bool[pw, ph];
            var q = new Queue<Point>();
            q.Enqueue(new Point(0, 0));
            visitedExterior[0, 0] = true;

            int[] dx = new int[] { -1, 1, 0, 0 };
            int[] dy = new int[] { 0, 0, -1, 1 };

            while (q.Count > 0)
            {
                var pt = q.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nx = pt.X + dx[i];
                    int ny = pt.Y + dy[i];
                    if (nx >= 0 && nx < pw && ny >= 0 && ny < ph)
                    {
                        if (!isInk[nx, ny] && !visitedExterior[nx, ny])
                        {
                            visitedExterior[nx, ny] = true;
                            q.Enqueue(new Point(nx, ny));
                        }
                    }
                }
            }

            bool[,] visitedHole = new bool[pw, ph];
            int holeCount = 0;

            for (int y = 1; y <= h; y++)
            {
                for (int x = 1; x <= w; x++)
                {
                    if (!isInk[x, y] && !visitedExterior[x, y] && !visitedHole[x, y])
                    {
                        holeCount++;
                        var hq = new Queue<Point>();
                        hq.Enqueue(new Point(x, y));
                        visitedHole[x, y] = true;

                        while (hq.Count > 0)
                        {
                            var hp = hq.Dequeue();
                            for (int i = 0; i < 4; i++)
                            {
                                int nx = hp.X + dx[i];
                                int ny = hp.Y + dy[i];
                                if (nx >= 1 && nx <= w && ny >= 1 && ny <= h)
                                {
                                    if (!isInk[nx, ny] && !visitedExterior[nx, ny] && !visitedHole[nx, ny])
                                    {
                                        visitedHole[nx, ny] = true;
                                        hq.Enqueue(new Point(nx, ny));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return holeCount;
        }

        public static Bitmap RenderTestDilatedGlyph(string ch, float sx, float sy, float dx, float dy)
        {
            int sz = 256;
            var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var transform = new NodeCustomTransform
                {
                    ScaleX = sx,
                    ScaleY = sy,
                    StrokeDilationX = dx,
                    StrokeDilationY = dy
                };

                float w = sz * 0.85f * sx;
                float h = sz * 0.85f * sy;
                float x = (sz - w) / 2f;
                float y = (sz - h) / 2f;
                var rect = new RectangleF(x, y, w, h);

                int cp = HanIdsCorpusIndex.GetFirstCodePoint(ch);
                FontFamily fam = FontLoaderService.ResolveFontFamilyForCodePoint(cp, IdsGlyphRenderer.ResolveFontFamily());

                using (var path = IdsGlyphRenderer.CreateComponentVectorPath(ch, fam, rect, transform))
                {
                    if (dx > 0.5f || dy > 0.5f)
                    {
                        float rx = Math.Max(0.2f, dx * sz / 1000f);
                        float ry = Math.Max(0.2f, dy * sz / 1000f);
                        using (var pen = new Pen(Color.Black, rx * 2f))
                        {
                            pen.LineJoin = LineJoin.Round;
                            if (rx > 0.001f)
                                pen.ScaleTransform(1.0f, ry / rx);
                            g.DrawPath(pen, path);
                        }
                    }
                    using (var br = new SolidBrush(Color.Black))
                    {
                        g.FillPath(br, path);
                    }
                }
            }
            return bmp;
        }

        public static RectangleF Stage4_FindLargestEmptyCavity(
            string enclosureChar,
            string op,
            FontFamily font,
            SolverConfig cfg)
        {
            int sz = 256;
            using (var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.None;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

                var rect = new RectangleF(sz * 0.05f, sz * 0.05f, sz * 0.9f, sz * 0.9f);
                using (var path = IdsGlyphRenderer.CreateComponentVectorPath(enclosureChar, font, rect, null))
                using (var br = new SolidBrush(Color.Black))
                {
                    g.FillPath(br, path);
                }

                float bufPx = (cfg != null ? cfg.CavityBuffer : 26f) * sz / 1000f;
                int buf = Math.Max(2, (int)Math.Round(bufPx));

                bool[,] isInk = ExtractBinaryMask(bmp);

                bool[,] blocked = new bool[sz, sz];
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        if (isInk[x, y])
                        {
                            int y0 = Math.Max(0, y - buf), y1 = Math.Min(sz - 1, y + buf);
                            int x0 = Math.Max(0, x - buf), x1 = Math.Min(sz - 1, x + buf);
                            for (int by = y0; by <= y1; by++)
                            {
                                for (int bx = x0; bx <= x1; bx++) blocked[bx, by] = true;
                            }
                        }
                    }
                }

                int minSearchX = (int)(sz * 0.15f), maxSearchX = (int)(sz * 0.85f);
                int minSearchY = (int)(sz * 0.15f), maxSearchY = (int)(sz * 0.85f);

                if (op == "⿵") { minSearchY = (int)(sz * 0.30f); maxSearchY = (int)(sz * 0.95f); }
                else if (op == "⿶") { minSearchY = (int)(sz * 0.05f); maxSearchY = (int)(sz * 0.70f); }
                else if (op == "⿷") { minSearchX = (int)(sz * 0.30f); maxSearchX = (int)(sz * 0.95f); }
                else if (op == "⿸") { minSearchX = (int)(sz * 0.25f); minSearchY = (int)(sz * 0.25f); maxSearchX = (int)(sz * 0.95f); maxSearchY = (int)(sz * 0.95f); }
                else if (op == "⿺") { minSearchX = (int)(sz * 0.25f); minSearchY = (int)(sz * 0.05f); maxSearchX = (int)(sz * 0.95f); maxSearchY = (int)(sz * 0.75f); }

                int bestArea = 0;
                int bestX = minSearchX, bestY = minSearchY, bestW = 80, bestH = 80;

                int[] colH = new int[sz];
                for (int y = minSearchY; y < maxSearchY; y++)
                {
                    for (int x = minSearchX; x < maxSearchX; x++)
                    {
                        if (!blocked[x, y]) colH[x]++;
                        else colH[x] = 0;
                    }

                    for (int x = minSearchX; x < maxSearchX; x++)
                    {
                        if (colH[x] == 0) continue;
                        int minH = colH[x];
                        for (int rx = x; rx < maxSearchX; rx++)
                        {
                            if (colH[rx] == 0) break;
                            if (colH[rx] < minH) minH = colH[rx];
                            int curW = rx - x + 1;
                            int curH = minH;
                            int area = curW * curH;

                            float aspect = (float)curW / curH;
                            if (aspect >= 0.5f && aspect <= 2.0f && area > bestArea)
                            {
                                bestArea = area;
                                bestX = x;
                                bestY = y - minH + 1;
                                bestW = curW;
                                bestH = curH;
                            }
                        }
                    }
                }

                float minRatio = cfg != null ? cfg.CavityMinRatio : 0.18f;
                int minDim = (int)(sz * minRatio);
                if (bestW < minDim) bestW = minDim;
                if (bestH < minDim) bestH = minDim;

                return new RectangleF(
                    bestX * 1000f / sz,
                    bestY * 1000f / sz,
                    bestW * 1000f / sz,
                    bestH * 1000f / sz
                );
            }
        }

        public static float Stage5_ComputeSkylineKerning(
            float[] skyline1,
            float[] skyline2,
            float width1,
            float width2,
            SolverConfig cfg)
        {
            if (skyline1 == null || skyline2 == null) return 0f;
            float minGap = float.MaxValue;
            int valid = 0;

            float min1 = 1f, max1 = 0f, min2 = 1f, max2 = 0f;
            for (int i = 16; i <= 48; i++)
            {
                if (skyline1[i] < 0.90f)
                {
                    if (skyline1[i] < min1) min1 = skyline1[i];
                    if (skyline1[i] > max1) max1 = skyline1[i];
                }
                if (skyline2[i] < 0.90f)
                {
                    if (skyline2[i] < min2) min2 = skyline2[i];
                    if (skyline2[i] > max2) max2 = skyline2[i];
                }
            }

            // Nếu một bên là vách thẳng đứng phẳng (như vách phải của 日 <= 0.055f) và bên kia không có hốc lõm sâu (như vách trái của 月 <= 0.16f),
            // không tồn tại cấu trúc răng lược để cài mộng -> trả về 0f để tránh kéo dính hai vách phẳng!
            if (max1 >= min1 && max2 >= min2 &&
                Math.Min(max1 - min1, max2 - min2) <= 0.055f &&
                Math.Max(max1 - min1, max2 - min2) <= 0.16f)
            {
                return 0f;
            }

            for (int i = 0; i < 64; i++)
            {
                if (skyline1[i] < 0.95f && skyline2[i] < 0.95f)
                {
                    float gap = (skyline1[i] + skyline2[i]) * 100f;
                    if (gap < minGap) minGap = gap;
                    valid++;
                }
            }

            float targetGap = cfg != null ? cfg.GTarget : 26.0f;
            if (valid > 8 && minGap > (targetGap * 0.6f) && minGap < 120f)
            {
                return minGap - (targetGap - 6f);
            }
            return 0f;
        }

        public static void Stage5_ComputeAnisotropicDilation(
            string ch,
            MorphologicalProfile profile,
            float scaleX,
            float scaleY,
            float txFont,
            float tyFont,
            FontFamily font,
            SolverConfig cfg,
            out float dilateX,
            out float dilateY)
        {
            dilateX = 0f;
            dilateY = 0f;
            if (profile == null) return;

            float coeff = cfg != null ? cfg.DilateCoeff : 0.55f;
            float effWx = profile.Wx0 * scaleX;
            float effWy = profile.Wy0 * scaleY;

            if (scaleX < 0.85f && effWx < txFont * 0.9f)
            {
                dilateX = Math.Max(0f, Math.Min(12f, (txFont - effWx) * coeff));
            }
            if (scaleY < 0.85f && effWy < tyFont * 0.9f)
            {
                dilateY = Math.Max(0f, Math.Min(12f, (tyFont - effWy) * coeff));
            }
        }

        public static void Stage5_ComputeAnisotropicDilation(
            MorphologicalProfile profile,
            float scaleX,
            float scaleY,
            float txFont,
            float tyFont,
            SolverConfig cfg,
            out float dilateX,
            out float dilateY)
        {
            Stage5_ComputeAnisotropicDilation(null, profile, scaleX, scaleY, txFont, tyFont, null, cfg, out dilateX, out dilateY);
        }

        public static float EvaluateLayoutObjective(
            float gapDeviation,
            float strokeDeviation,
            float densityDeviation,
            float centroidDeviation,
            int holesViolated,
            float priorDeviation,
            SolverConfig cfg)
        {
            if (cfg == null) cfg = new SolverConfig();
            return cfg.W1Gap * gapDeviation +
                   cfg.W2Weight * strokeDeviation +
                   cfg.W3Density * densityDeviation +
                   cfg.W4Centroid * centroidDeviation +
                   cfg.W5CounterSpace * holesViolated +
                   cfg.W6Prior * priorDeviation;
        }

        private static void RunBoundedCoordinateDescent(
            IdsNode root,
            Dictionary<string, NodeCustomTransform> transforms,
            Dictionary<string, MorphologicalProfile> nodeProfiles,
            FontFamily font,
            SolverConfig cfg)
        {
            if (cfg == null || root == null || transforms == null) return;
            int maxIters = cfg.MaxIterations;
            float splitStep = cfg.SplitStep;
            float offsetStep = cfg.OffsetStep;
            float minHoleWidth = cfg.MinHoleWidth;

            if (transforms.ContainsKey(root.Id) && transforms[root.Id].SplitRatio.HasValue)
            {
                var t = transforms[root.Id];
                if (t.SplitRatio.Value < cfg.SplitMin) t.SplitRatio = cfg.SplitMin;
                if (t.SplitRatio.Value > cfg.SplitMax) t.SplitRatio = cfg.SplitMax;
                transforms[root.Id] = t;
            }

            // Kiểm tra và giải tỏa va chạm nét mực giữa các nhánh anh em (ngoại trừ toán tử bao quanh và đè nhau)
            string idsStr = IdsParserService.AstToIdsString(root);
            bool isEnclosureOrOverlap = idsStr.Any(c => "⿴⿵⿶⿷⿸⿹⿺⿻⿼⿽".Contains(c));
            if (!isEnclosureOrOverlap && maxIters > 0 && offsetStep > 0 && splitStep > 0 && minHoleWidth > 0)
            {
                for (int iter = 0; iter < Math.Min(4, maxIters); iter++)
                {
                    Action<IdsNode> apply = null;
                    apply = n =>
                    {
                        if (n == null) return;
                        NodeCustomTransform tr;
                        if (transforms.TryGetValue(n.Id, out tr)) n.Transform = tr;
                        if (n.Children != null) { foreach (var c in n.Children) apply(c); }
                    };
                    apply(root);

                    var items = IdsLayoutEngine.ComputeLayout(root, new RectangleF(0, 0, 1000f, 1000f));
                    bool anyCollision = false;

                    for (int i = 0; i < items.Count; i++)
                    {
                        for (int j = i + 1; j < items.Count; j++)
                        {
                            var p1 = ExtractOpticalMetrics(items[i].Char, font);
                            var p2 = ExtractOpticalMetrics(items[j].Char, font);
                            if (p1 != null && p2 != null)
                            {
                                float collision;
                                float cx1 = items[i].TargetRect.X + items[i].TargetRect.Width / 2f;
                                float cy1 = items[i].TargetRect.Y + items[i].TargetRect.Height / 2f;
                                float cx2 = items[j].TargetRect.X + items[j].TargetRect.Width / 2f;
                                float cy2 = items[j].TargetRect.Y + items[j].TargetRect.Height / 2f;
                                bool isHoriz = Math.Abs(cx1 - cx2) > Math.Abs(cy1 - cy2);

                                ComputeTrueInkGap(items[i].TargetRect, items[j].TargetRect, p1, p2, isHoriz, out collision);
                                if (collision > 40.0f)
                                {
                                    anyCollision = true;
                                    var t1 = transforms[items[i].NodeId];
                                    var t2 = transforms[items[j].NodeId];
                                    int step = (int)Math.Round(offsetStep);
                                    if (isHoriz)
                                    {
                                        if (cx1 <= cx2) { t1.OffsetX -= step; t2.OffsetX += step; }
                                        else { t1.OffsetX += step; t2.OffsetX -= step; }
                                    }
                                    else
                                    {
                                        if (cy1 <= cy2) { t1.OffsetY -= step; t2.OffsetY += step; }
                                        else { t1.OffsetY += step; t2.OffsetY -= step; }
                                    }
                                    transforms[items[i].NodeId] = t1;
                                    transforms[items[j].NodeId] = t2;
                                }
                            }
                        }
                    }

                    if (!anyCollision) break;
                }
            }
        }

        public static float ComputeTrueInkGap(RectangleF r1, RectangleF r2, MorphologicalProfile p1, MorphologicalProfile p2, bool isHorizontal, out float collision)
        {
            collision = 0f;
            if (p1 == null || p2 == null || p1.TopSkyline == null || p2.TopSkyline == null)
            {
                if (RectangleF.Intersect(r1, r2) != RectangleF.Empty)
                {
                    var inter = RectangleF.Intersect(r1, r2);
                    collision = inter.Width * inter.Height / 100f;
                    return 0f;
                }
                float distH = Math.Max(0f, Math.Max(r1.Left - r2.Right, r2.Left - r1.Right));
                float distV = Math.Max(0f, Math.Max(r1.Top - r2.Bottom, r2.Top - r1.Bottom));
                return Math.Max(distH, distV);
            }

            float minGap = float.MaxValue;
            int valid = 0;
            if (isHorizontal)
            {
                RectangleF leftR = r1.Left <= r2.Left ? r1 : r2;
                RectangleF rightR = r1.Left <= r2.Left ? r2 : r1;
                MorphologicalProfile leftP = r1.Left <= r2.Left ? p1 : p2;
                MorphologicalProfile rightP = r1.Left <= r2.Left ? p2 : p1;

                for (int c = 0; c < 64; c++)
                {
                    if (leftP.RightSkyline[c] < 0.95f && rightP.LeftSkyline[c] < 0.95f)
                    {
                        float xLeftInk = leftR.Left + leftR.Width * (1.0f - leftP.RightSkyline[c]);
                        float xRightInk = rightR.Left + rightR.Width * rightP.LeftSkyline[c];
                        float g = xRightInk - xLeftInk;
                        if (g < 0) collision += (-g);
                        if (g < minGap) minGap = g;
                        valid++;
                    }
                }
            }
            else
            {
                RectangleF topR = r1.Top <= r2.Top ? r1 : r2;
                RectangleF botR = r1.Top <= r2.Top ? r2 : r1;
                MorphologicalProfile topP = r1.Top <= r2.Top ? p1 : p2;
                MorphologicalProfile botP = r1.Top <= r2.Top ? p2 : p1;

                for (int c = 0; c < 64; c++)
                {
                    if (topP.BottomSkyline[c] < 0.95f && botP.TopSkyline[c] < 0.95f)
                    {
                        float yTopInk = topR.Top + topR.Height * (1.0f - topP.BottomSkyline[c]);
                        float yBotInk = botR.Top + botR.Height * botP.TopSkyline[c];
                        float g = yBotInk - yTopInk;
                        if (g < 0) collision += (-g);
                        if (g < minGap) minGap = g;
                        valid++;
                    }
                }
            }

            return (valid > 0 && minGap != float.MaxValue) ? minGap : 26.0f;
        }

        public static bool ValidateAstSanity(string ids, IdsNode root, out string err)
        {
            var sanity = Stage1_ValidateAstSanity(ids, root, new GdiGlyphSource(FontFamily.GenericSansSerif));
            err = sanity.MessageVi;
            return sanity.Ok;
        }

        public static MorphologicalProfile ExtractOpticalMetrics(string glyph, FontFamily font)
        {
            if (string.IsNullOrEmpty(glyph)) return null;
            return Stage2_ExtractLeafProfile(glyph, new GdiGlyphSource(font));
        }

        private static IdsNode FindNode(IdsNode root, string id)
        {
            if (root == null || root.Id == id) return root;
            if (root.Children != null)
            {
                foreach (var c in root.Children)
                {
                    var found = FindNode(c, id);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
