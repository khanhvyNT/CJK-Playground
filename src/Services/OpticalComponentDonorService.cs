using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class VerifiedDonorResult
    {
        public int DonorExemplarCodePoint { get; set; }
        public string DonorCharacter { get; set; }
        public int[] DonorContourIndices { get; set; }
        public RectangleF DonorRelativeBox { get; set; } // Normalized [0..1] inside exemplar's ink box
        public float DonorNaturalAspect { get; set; }
        public float ChamferSimilarity { get; set; }
        public float OrientationSimilarity { get; set; }
    }

    /// <summary>
    /// BỘ MÁY TRÍCH XUẤT NÉT CHỮ THƯ PHÁP & ĐỐI CHIẾU QUANG HỌC 2 CHIỀU (Optical Calligraphic Donor & 2D Stroke Matching Engine):
    /// Thay vì dùng toán học thuần túy để ép chữ độc lập vào khung gạch ("ghép gạch"),
    /// dịch vụ này giải phẫu trực tiếp các chữ mẫu thực tế trong phông chữ (từ cơ sở dữ liệu 98.000 chữ Unicode)
    /// bằng:
    ///   1. Phân rã đường viền vector đóng (GraphicsPathIterator Subpath Decomposition) kết hợp bảo toàn lỗ trắng nội tại.
    ///   2. Tìm khe phân cách thung lũng mật độ mực 2 chiều (2D Projection-Valley & Zero-Penetration Seam Finding).
    ///   3. Kiểm định so khớp nét 2D tương đương OpenCV:
    ///      - Biến đổi khoảng cách Chamfer 2 chiều (2-Pass Borgefors Chamfer 3-4 Distance Transform).
    ///      - Biểu đồ năng lượng hướng nét 4 chiều Sobel/Scharr (0°, 45°, 90°, 135° Directional Stroke Energy).
    ///      - Kiểm định bảo toàn 4 góc phần tư (4-Quadrant Topological Completeness) và số lỗ trắng Euler.
    /// </summary>
    public static class OpticalComponentDonorService
    {
        private static readonly ConcurrentDictionary<string, VerifiedDonorResult> DonorCache =
            new ConcurrentDictionary<string, VerifiedDonorResult>();

        private const int VerifyGridSize = 64;

        public static VerifiedDonorResult TryFindVerifiedDonor(
            string leafChar,
            int leafCp,
            string parentOp,
            int slotIndex,
            int totalSlots,
            int wholeParentResolvedCp,
            List<ExemplarSeed> candidateSeeds,
            FontFamily fontFamily)
        {
            if (string.IsNullOrEmpty(leafChar) || leafCp <= 0 || fontFamily == null)
                return null;

            // Chỉ trích xuất biến thể vị trí cho các toán tử ghép cạnh / chồng tầng (⿰, ⿱, ⿲, ⿳)
            if (parentOp != "⿰" && parentOp != "⿱" && parentOp != "⿲" && parentOp != "⿳")
                return null;

            // Các nét đơn cực giản (như 一 U+4E00, 丨 U+4E28, 丶 U+4E36, 丿 U+4E3F) giữ nguyên vector gốc
            if (leafCp == 0x4E00 || leafCp == 0x4E28 || leafCp == 0x4E36 || leafCp == 0x4E3F)
                return null;

            string excludedTag = ExemplarSeeds.CurrentExcludedCharacter ?? "";
            string cacheKey = string.Format("{0}|{1:X4}|{2}|{3}|{4}|{5:X4}|{6}",
                fontFamily.Name, leafCp, parentOp, slotIndex, totalSlots, wholeParentResolvedCp, excludedTag);

            VerifiedDonorResult cached;
            if (DonorCache.TryGetValue(cacheKey, out cached))
            {
                return (cached != null && cached.DonorExemplarCodePoint > 0) ? cached : null;
            }

            // Chuẩn bị mặt nạ chuẩn 64x64 và đặc trưng của ký tự lá gốc để đối chiếu quang học
            bool[,] refMask;
            int refHoles;
            float[] refSobelHist;
            float[,] refDistField;
            RectangleF refRawBounds;

            using (var refPath = BuildRawGlyphPath(leafChar, fontFamily))
            {
                if (refPath == null || refPath.PointCount == 0)
                {
                    DonorCache[cacheKey] = new VerifiedDonorResult();
                    return null;
                }
                refRawBounds = refPath.GetBounds();
                if (refRawBounds.Width < 2f || refRawBounds.Height < 2f)
                {
                    DonorCache[cacheKey] = new VerifiedDonorResult();
                    return null;
                }

                refMask = RasterizePathToNormalizedGrid(refPath, refRawBounds, VerifyGridSize, out refHoles);
                refDistField = ComputeChamferDistanceTransform(refMask, VerifyGridSize, VerifyGridSize);
                refSobelHist = ComputeSobelOrientationHistogram(refMask, VerifyGridSize, VerifyGridSize);
            }

            // Xây dựng danh sách ứng viên mẫu ưu tiên:
            // 1. Chữ khớp hoàn toàn với nút cha (nếu có và không bị loại trừ bởi Leave-One-Out).
            // 2. Các mẫu BMP từ tập kết quả truy vấn ngữ cảnh (candidateSeeds).
            var orderedCandidates = new List<int>();
            var seenCps = new HashSet<int>();

            if (wholeParentResolvedCp >= 0x4E00 && wholeParentResolvedCp <= 0x9FFF)
            {
                string parentStr = char.ConvertFromUtf32(wholeParentResolvedCp);
                if (string.IsNullOrEmpty(excludedTag) || parentStr != excludedTag)
                {
                    orderedCandidates.Add(wholeParentResolvedCp);
                    seenCps.Add(wholeParentResolvedCp);
                }
            }

            if (candidateSeeds != null)
            {
                foreach (var seed in candidateSeeds)
                {
                    if (seed == null || seed.CodePoint < 0x4E00 || seed.CodePoint > 0x9FFF) continue;
                    if (seed.Operator != parentOp) continue;
                    if (!string.IsNullOrEmpty(excludedTag) && seed.Character == excludedTag) continue;

                    // Kiểm tra thành phần tại đúng slotIndex của mẫu phải tương ứng với leafCp
                    if (seed.Components != null && slotIndex < seed.Components.Length)
                    {
                        int seedSlotCp = HanIdsCorpusIndex.GetFirstCodePoint(seed.Components[slotIndex]);
                        int normSeedSlot = HanIdsCorpusIndex.NormalizeCorpusComponent(seedSlotCp, HanIdsCorpusIndex.GetOperatorId(parentOp), slotIndex);
                        int normLeaf = HanIdsCorpusIndex.NormalizeCorpusComponent(leafCp, HanIdsCorpusIndex.GetOperatorId(parentOp), slotIndex);
                        if (normSeedSlot != normLeaf && seedSlotCp != leafCp)
                            continue;
                    }

                    if (seenCps.Add(seed.CodePoint))
                    {
                        orderedCandidates.Add(seed.CodePoint);
                    }
                }
            }

            VerifiedDonorResult bestDonor = null;
            float bestCombinedScore = -1f;
            int maxToInspect = Math.Min(orderedCandidates.Count, 18);

            for (int idx = 0; idx < maxToInspect; idx++)
            {
                int candCp = orderedCandidates[idx];
                string candChar = char.ConvertFromUtf32(candCp);

                int[] contourIndices;
                RectangleF relBox;
                float naturalAspect;
                GraphicsPath extractedPath;

                if (!TryExtractSlotSubpathsFromExemplar(
                    candChar,
                    candCp,
                    parentOp,
                    slotIndex,
                    totalSlots,
                    fontFamily,
                    out contourIndices,
                    out relBox,
                    out naturalAspect,
                    out extractedPath))
                {
                    continue;
                }

                using (extractedPath)
                {
                    RectangleF extBounds = extractedPath.GetBounds();
                    if (extBounds.Width < 2f || extBounds.Height < 2f) continue;

                    int donorHoles;
                    bool[,] donorMask = RasterizePathToNormalizedGrid(extractedPath, extBounds, VerifyGridSize, out donorHoles);

                    // 1. Kiểm định bảo toàn số lỗ trắng nội tại (Euler Hole Conservation)
                    if (donorHoles != refHoles)
                        continue;

                    // 2. Kiểm định bảo toàn mật độ 4 góc phần tư (4-Quadrant Completeness)
                    if (!VerifyFourQuadrantCompleteness(refMask, donorMask, VerifyGridSize))
                        continue;

                    // 3. Kiểm định so khớp khung xương nét bằng biến đổi khoảng cách Chamfer 2 chiều (Bidirectional Chamfer Distance)
                    float[,] donorDistField = ComputeChamferDistanceTransform(donorMask, VerifyGridSize, VerifyGridSize);
                    float chamferSim = ComputeBidirectionalChamferSimilarity(refMask, refDistField, donorMask, donorDistField, VerifyGridSize);
                    if (chamferSim < 0.65f)
                        continue;

                    // 4. Kiểm định năng lượng hướng nét 4 chiều Sobel (0°, 45°, 90°, 135°)
                    float[] donorSobelHist = ComputeSobelOrientationHistogram(donorMask, VerifyGridSize, VerifyGridSize);
                    float orientSim = ComputeCosineSimilarity(refSobelHist, donorSobelHist);
                    if (orientSim < 0.82f)
                        continue;

                    float exactParentBonus = (candCp == wholeParentResolvedCp) ? 0.18f : 0f;
                    float combinedScore = 0.60f * chamferSim + 0.40f * orientSim + exactParentBonus;

                    if (combinedScore > bestCombinedScore)
                    {
                        bestCombinedScore = combinedScore;
                        bestDonor = new VerifiedDonorResult
                        {
                            DonorExemplarCodePoint = candCp,
                            DonorCharacter = candChar,
                            DonorContourIndices = contourIndices,
                            DonorRelativeBox = relBox,
                            DonorNaturalAspect = naturalAspect,
                            ChamferSimilarity = chamferSim,
                            OrientationSimilarity = orientSim
                        };

                        // Nếu khớp trực tiếp chữ cha hoặc đạt điểm rất cao (>= 0.90), chốt ngay
                        if (candCp == wholeParentResolvedCp || combinedScore >= 0.90f)
                            break;
                    }
                }
            }

            DonorCache[cacheKey] = bestDonor ?? new VerifiedDonorResult();
            return bestDonor;
        }

        public static bool TryBuildDonorVectorPath(
            int donorCodePoint,
            int[] contourIndices,
            FontFamily effectiveFamily,
            GraphicsPath targetRawPath)
        {
            if (donorCodePoint <= 0 || contourIndices == null || contourIndices.Length == 0 || targetRawPath == null)
                return false;

            string donorStr = char.ConvertFromUtf32(donorCodePoint);
            FontFamily resolvedFamily = IdsGlyphRenderer.ResolveComponentFontFamily(donorStr, effectiveFamily);

            using (var fullDonorPath = BuildRawGlyphPath(donorStr, resolvedFamily))
            {
                if (fullDonorPath == null || fullDonorPath.PointCount == 0)
                    return false;

                var wantedSet = new HashSet<int>(contourIndices);
                using (var iter = new GraphicsPathIterator(fullDonorPath))
                {
                    int subCount = iter.SubpathCount;
                    int added = 0;
                    for (int i = 0; i < subCount; i++)
                    {
                        bool isClosed;
                        using (var sp = new GraphicsPath(fullDonorPath.FillMode))
                        {
                            int pts = iter.NextSubpath(sp, out isClosed);
                            if (pts > 0 && wantedSet.Contains(i))
                            {
                                targetRawPath.AddPath(sp, false);
                                added++;
                            }
                        }
                    }
                    return added > 0 && targetRawPath.PointCount > 0;
                }
            }
        }

        private static GraphicsPath BuildRawGlyphPath(string ch, FontFamily family)
        {
            if (string.IsNullOrEmpty(ch) || family == null) return null;
            var rawPath = new GraphicsPath();
            try
            {
                using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    rawPath.AddString(ch, family, (int)FontStyle.Regular, 500f, new PointF(250f, 250f), sf);
                }
                return rawPath;
            }
            catch
            {
                rawPath.Dispose();
                return null;
            }
        }

        private static bool TryExtractSlotSubpathsFromExemplar(
            string exemplarChar,
            int exemplarCp,
            string op,
            int slotIndex,
            int totalSlots,
            FontFamily family,
            out int[] matchedContourIndices,
            out RectangleF relativeBox,
            out float naturalAspect,
            out GraphicsPath extractedPath)
        {
            matchedContourIndices = null;
            relativeBox = RectangleF.Empty;
            naturalAspect = 1.0f;
            extractedPath = null;

            FontFamily resolvedFamily = IdsGlyphRenderer.ResolveComponentFontFamily(exemplarChar, family);
            using (var fullPath = BuildRawGlyphPath(exemplarChar, resolvedFamily))
            {
                if (fullPath == null || fullPath.PointCount == 0) return false;

                RectangleF fullBounds = fullPath.GetBounds();
                if (fullBounds.Width < 10f || fullBounds.Height < 10f) return false;

                var subpaths = new List<GraphicsPath>();
                var subBounds = new List<RectangleF>();
                var origIndices = new List<int>();

                try
                {
                    using (var iter = new GraphicsPathIterator(fullPath))
                    {
                        int count = iter.SubpathCount;
                        if (count < totalSlots) return false;

                        for (int i = 0; i < count; i++)
                        {
                            bool isClosed;
                            var sp = new GraphicsPath(fullPath.FillMode);
                            int pts = iter.NextSubpath(sp, out isClosed);
                            if (pts > 0)
                            {
                                RectangleF b = sp.GetBounds();
                                if (b.Width >= 0.5f && b.Height >= 0.5f)
                                {
                                    subpaths.Add(sp);
                                    subBounds.Add(b);
                                    origIndices.Add(i);
                                    continue;
                                }
                            }
                            sp.Dispose();
                        }
                    }

                    int m = subpaths.Count;
                    if (m < totalSlots) return false;

                    bool isHorizontal = (op == "⿰" || op == "⿲");

                    // Chuẩn hóa khoảng chiếu [l_k, r_k] của từng subpath trên trục phân tách [0..1]
                    float[] normMin = new float[m];
                    float[] normMax = new float[m];
                    float[] normCenter = new float[m];

                    for (int k = 0; k < m; k++)
                    {
                        RectangleF b = subBounds[k];
                        if (isHorizontal)
                        {
                            normMin[k] = (b.Left - fullBounds.Left) / fullBounds.Width;
                            normMax[k] = (b.Right - fullBounds.Left) / fullBounds.Width;
                        }
                        else
                        {
                            normMin[k] = (b.Top - fullBounds.Top) / fullBounds.Height;
                            normMax[k] = (b.Bottom - fullBounds.Top) / fullBounds.Height;
                        }
                        normCenter[k] = 0.5f * (normMin[k] + normMax[k]);
                    }

                    // Gắn kết mọi đường viền lỗ trắng nội tại (inner counter) vào đường viền bao ngoài chứa nó
                    int[] parentOwner = new int[m];
                    for (int k = 0; k < m; k++) parentOwner[k] = k;

                    for (int k = 0; k < m; k++)
                    {
                        RectangleF bk = subBounds[k];
                        float areaK = bk.Width * bk.Height;
                        for (int p = 0; p < m; p++)
                        {
                            if (p == k) continue;
                            RectangleF bp = subBounds[p];
                            float areaP = bp.Width * bp.Height;
                            if (areaP > areaK * 1.15f &&
                                bk.Left >= bp.Left - 2.0f && bk.Right <= bp.Right + 2.0f &&
                                bk.Top >= bp.Top - 2.0f && bk.Bottom <= bp.Bottom + 2.0f)
                            {
                                parentOwner[k] = p;
                                break;
                            }
                        }
                    }

                    for (int k = 0; k < m; k++)
                    {
                        int rootP = parentOwner[k];
                        while (parentOwner[rootP] != rootP) rootP = parentOwner[rootP];
                        parentOwner[k] = rootP;
                        normCenter[k] = normCenter[rootP];
                    }

                    List<int> selectedLocalIndices = null;

                    if (totalSlots == 2)
                    {
                        float bestSplit = 0.50f;
                        float bestCost = float.MaxValue;
                        float bestMaxPen = float.MaxValue;

                        for (int step = 20; step <= 80; step++)
                        {
                            float t = step * 0.01f;
                            int c0 = 0, c1 = 0;
                            float maxPen = 0f;

                            for (int k = 0; k < m; k++)
                            {
                                int rootP = parentOwner[k];
                                if (normCenter[rootP] < t)
                                {
                                    c0++;
                                    float pen = Math.Max(0f, normMax[rootP] - t);
                                    if (pen > maxPen) maxPen = pen;
                                }
                                else
                                {
                                    c1++;
                                    float pen = Math.Max(0f, t - normMin[rootP]);
                                    if (pen > maxPen) maxPen = pen;
                                }
                            }

                            if (c0 == 0 || c1 == 0) continue;

                            float centerBias = 0.08f * Math.Abs(t - 0.45f);
                            float cost = maxPen + centerBias;
                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                bestSplit = t;
                                bestMaxPen = maxPen;
                            }
                        }

                        // Chặn nghiêm ngặt: nếu có đường viền xuyên qua ranh giới quá sâu (> 11% đối với ⿱ có cánh mác bao phủ, hoặc > 6.5% đối với ⿰), loại bỏ!
                        float maxAllowedPen = isHorizontal ? 0.065f : 0.115f;
                        if (bestMaxPen > maxAllowedPen) return false;

                        selectedLocalIndices = new List<int>();
                        for (int k = 0; k < m; k++)
                        {
                            int rootP = parentOwner[k];
                            // Không chấp nhận đường viền đơn lẻ nào kéo dài xuyên suốt > 72% trục phân chia (dấu hiệu dính nét toàn cục)
                            if (normMax[rootP] - normMin[rootP] > 0.72f) return false;
                            int slot = (normCenter[rootP] < bestSplit) ? 0 : 1;
                            if (slot == slotIndex) selectedLocalIndices.Add(k);
                        }
                    }
                    else if (totalSlots == 3)
                    {
                        float bestT1 = 0.32f, bestT2 = 0.68f;
                        float bestCost = float.MaxValue;
                        float bestMaxPen = float.MaxValue;

                        for (int s1 = 18; s1 <= 46; s1 += 2)
                        {
                            float t1 = s1 * 0.01f;
                            for (int s2 = Math.Max(52, s1 + 20); s2 <= 82; s2 += 2)
                            {
                                float t2 = s2 * 0.01f;
                                int c0 = 0, c1 = 0, c2 = 0;
                                float maxPen = 0f;

                                for (int k = 0; k < m; k++)
                                {
                                    int rootP = parentOwner[k];
                                    float c = normCenter[rootP];
                                    if (c < t1)
                                    {
                                        c0++;
                                        float pen = Math.Max(0f, normMax[rootP] - t1);
                                        if (pen > maxPen) maxPen = pen;
                                    }
                                    else if (c < t2)
                                    {
                                        c1++;
                                        float pen = Math.Max(Math.Max(0f, t1 - normMin[rootP]), Math.Max(0f, normMax[rootP] - t2));
                                        if (pen > maxPen) maxPen = pen;
                                    }
                                    else
                                    {
                                        c2++;
                                        float pen = Math.Max(0f, t2 - normMin[rootP]);
                                        if (pen > maxPen) maxPen = pen;
                                    }
                                }

                                if (c0 == 0 || c1 == 0 || c2 == 0) continue;

                                float bias = 0.06f * (Math.Abs(t1 - 0.31f) + Math.Abs(t2 - 0.68f));
                                float cost = maxPen + bias;
                                if (cost < bestCost)
                                {
                                    bestCost = cost;
                                    bestT1 = t1;
                                    bestT2 = t2;
                                    bestMaxPen = maxPen;
                                }
                            }
                        }

                        if (bestMaxPen > 0.055f) return false;

                        selectedLocalIndices = new List<int>();
                        for (int k = 0; k < m; k++)
                        {
                            int rootP = parentOwner[k];
                            float c = normCenter[rootP];
                            int slot = (c < bestT1) ? 0 : (c < bestT2 ? 1 : 2);
                            if (slot == slotIndex) selectedLocalIndices.Add(k);
                        }
                    }

                    if (selectedLocalIndices == null || selectedLocalIndices.Count == 0)
                        return false;

                    var outPath = new GraphicsPath(fullPath.FillMode);
                    var outOrigIndices = new int[selectedLocalIndices.Count];
                    for (int i = 0; i < selectedLocalIndices.Count; i++)
                    {
                        int localIdx = selectedLocalIndices[i];
                        outPath.AddPath(subpaths[localIdx], false);
                        outOrigIndices[i] = origIndices[localIdx];
                    }

                    RectangleF partBounds = outPath.GetBounds();
                    if (partBounds.Width < 4f || partBounds.Height < 4f)
                    {
                        outPath.Dispose();
                        return false;
                    }

                    relativeBox = new RectangleF(
                        (partBounds.Left - fullBounds.Left) / fullBounds.Width,
                        (partBounds.Top - fullBounds.Top) / fullBounds.Height,
                        partBounds.Width / fullBounds.Width,
                        partBounds.Height / fullBounds.Height);

                    // Kiểm tra tính hợp lý hình học của bộ phận được tách:
                    // Ví dụ: trong toán tử ⿰, bộ phận đơn phải chiếm ít nhất 48% chiều cao của chữ mẫu (không phải một mảnh vụn rơi rớt)
                    if (isHorizontal && relativeBox.Height < 0.48f)
                    {
                        outPath.Dispose();
                        return false;
                    }
                    if (!isHorizontal && relativeBox.Width < 0.45f)
                    {
                        outPath.Dispose();
                        return false;
                    }

                    naturalAspect = partBounds.Width / Math.Max(1f, partBounds.Height);
                    matchedContourIndices = outOrigIndices;
                    extractedPath = outPath;
                    return true;
                }
                finally
                {
                    foreach (var sp in subpaths)
                    {
                        sp.Dispose();
                    }
                }
            }
        }

        private static bool[,] RasterizePathToNormalizedGrid(GraphicsPath path, RectangleF bounds, int gridSize, out int holeCount)
        {
            holeCount = 0;
            var mask = new bool[gridSize, gridSize];
            if (path == null || bounds.Width < 1f || bounds.Height < 1f) return mask;

            int pad = 4;
            int innerSz = gridSize - pad * 2;

            using (var bmp = new Bitmap(gridSize, gridSize, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var clonePath = (GraphicsPath)path.Clone())
            using (var matrix = new Matrix())
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                matrix.Translate(-bounds.X, -bounds.Y, MatrixOrder.Append);
                matrix.Scale(innerSz / bounds.Width, innerSz / bounds.Height, MatrixOrder.Append);
                matrix.Translate(pad, pad, MatrixOrder.Append);
                clonePath.Transform(matrix);

                g.FillPath(Brushes.Black, clonePath);

                holeCount = IdsOpticalBeautifier.CountHolesOnRaster(bmp);
                mask = IdsOpticalBeautifier.ExtractBinaryMask(bmp);
            }

            return mask;
        }

        /// <summary>
        /// Biến đổi khoảng cách Chamfer 3-4 hai lượt (2-Pass Borgefors Chamfer Distance Transform - tương đương cv::distanceTransform).
        /// </summary>
        public static float[,] ComputeChamferDistanceTransform(bool[,] mask, int w, int h)
        {
            var dist = new float[w, h];
            float inf = (w + h) * 2f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    dist[x, y] = mask[x, y] ? 0f : inf;
                }
            }

            // Lượt thuận (Forward pass: từ trái-trên xuống phải-dưới)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = dist[x, y];
                    if (d == 0f) continue;

                    if (x > 0) d = Math.Min(d, dist[x - 1, y] + 1.0f);
                    if (y > 0) d = Math.Min(d, dist[x, y - 1] + 1.0f);
                    if (x > 0 && y > 0) d = Math.Min(d, dist[x - 1, y - 1] + 1.4142f);
                    if (x + 1 < w && y > 0) d = Math.Min(d, dist[x + 1, y - 1] + 1.4142f);
                    dist[x, y] = d;
                }
            }

            // Lượt ngược (Backward pass: từ phải-dưới lên trái-trên)
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    float d = dist[x, y];
                    if (d == 0f) continue;

                    if (x + 1 < w) d = Math.Min(d, dist[x + 1, y] + 1.0f);
                    if (y + 1 < h) d = Math.Min(d, dist[x, y + 1] + 1.0f);
                    if (x + 1 < w && y + 1 < h) d = Math.Min(d, dist[x + 1, y + 1] + 1.4142f);
                    if (x > 0 && y + 1 < h) d = Math.Min(d, dist[x - 1, y + 1] + 1.4142f);
                    dist[x, y] = d;
                }
            }

            return dist;
        }

        public static float ComputeBidirectionalChamferSimilarity(
            bool[,] maskA,
            float[,] distA,
            bool[,] maskB,
            float[,] distB,
            int sz)
        {
            float sumAtoB = 0f;
            int countA = 0;
            float sumBtoA = 0f;
            int countB = 0;

            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    if (maskA[x, y])
                    {
                        sumAtoB += distB[x, y];
                        countA++;
                    }
                    if (maskB[x, y])
                    {
                        sumBtoA += distA[x, y];
                        countB++;
                    }
                }
            }

            if (countA < 16 || countB < 16) return 0f;

            // Tỷ lệ diện tích mực không được lệch quá 2.2 lần
            float areaRatio = (float)Math.Max(countA, countB) / Math.Max(1, Math.Min(countA, countB));
            if (areaRatio > 2.2f) return 0f;

            float meanAtoB = sumAtoB / countA;
            float meanBtoA = sumBtoA / countB;
            float symDist = 0.5f * (meanAtoB + meanBtoA);

            return (float)Math.Exp(-symDist / 6.5f);
        }

        /// <summary>
        /// Tính biểu đồ năng lượng hướng nét 4 chiều (0° Ngang, 45° Chéo Sắc, 90° Sổ, 135° Chéo Huyền) bằng bộ lọc Sobel 3x3.
        /// </summary>
        public static float[] ComputeSobelOrientationHistogram(bool[,] mask, int w, int h)
        {
            var hist = new float[4];
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float tl = mask[x - 1, y - 1] ? 1f : 0f;
                    float tc = mask[x, y - 1] ? 1f : 0f;
                    float tr = mask[x + 1, y - 1] ? 1f : 0f;
                    float ml = mask[x - 1, y] ? 1f : 0f;
                    float mr = mask[x + 1, y] ? 1f : 0f;
                    float bl = mask[x - 1, y + 1] ? 1f : 0f;
                    float bc = mask[x, y + 1] ? 1f : 0f;
                    float br = mask[x + 1, y + 1] ? 1f : 0f;

                    float gx = (tr + 2f * mr + br) - (tl + 2f * ml + bl);
                    float gy = (bl + 2f * bc + br) - (tl + 2f * tc + tr);
                    float gd1 = (tc + 2f * tr + mr) - (ml + 2f * bl + bc);
                    float gd2 = (tl + 2f * tc + ml) - (mr + 2f * br + bc);

                    hist[0] += Math.Abs(gy);  // Năng lượng nét ngang (0°)
                    hist[1] += Math.Abs(gd1); // Năng lượng nét chéo (45°)
                    hist[2] += Math.Abs(gx);  // Năng lượng nét sổ dọc (90°)
                    hist[3] += Math.Abs(gd2); // Năng lượng nét chéo (135°)
                }
            }

            float norm = (float)Math.Sqrt(hist[0] * hist[0] + hist[1] * hist[1] + hist[2] * hist[2] + hist[3] * hist[3]);
            if (norm > 1e-4f)
            {
                for (int i = 0; i < 4; i++) hist[i] /= norm;
            }
            return hist;
        }

        private static float ComputeCosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0f;
            float dot = 0f, na = 0f, nb = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            float denom = (float)Math.Sqrt(na * nb);
            return denom > 1e-5f ? (dot / denom) : 0f;
        }

        private static bool VerifyFourQuadrantCompleteness(bool[,] refMask, bool[,] donorMask, int sz)
        {
            int half = sz / 2;
            int[] refQ = new int[4];
            int[] donQ = new int[4];
            int totalRef = 0, totalDon = 0;

            for (int y = 0; y < sz; y++)
            {
                int qy = (y < half) ? 0 : 2;
                for (int x = 0; x < sz; x++)
                {
                    int qIdx = qy + ((x < half) ? 0 : 1);
                    if (refMask[x, y]) { refQ[qIdx]++; totalRef++; }
                    if (donorMask[x, y]) { donQ[qIdx]++; totalDon++; }
                }
            }

            if (totalRef < 16 || totalDon < 16) return false;

            for (int q = 0; q < 4; q++)
            {
                float refShare = (float)refQ[q] / totalRef;
                float donShare = (float)donQ[q] / totalDon;
                // Nếu góc phần tư q có >= 8% lượng mực trong chữ gốc nhưng lại gần như trống rỗng (< 2%) ở bản tách -> bị mất nét!
                if (refShare >= 0.08f && donShare < 0.02f)
                    return false;
            }

            return true;
        }
    }
}
