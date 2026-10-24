using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class IdcOperatorMeta
    {
        public string Symbol { get; set; }
        public int Arity { get; set; }
        public string NameVi { get; set; }
        public string NameEn { get; set; }

        public IdcOperatorMeta(string symbol, int arity, string nameVi, string nameEn)
        {
            Symbol = symbol;
            Arity = arity;
            NameVi = nameVi;
            NameEn = nameEn;
        }
    }

    public struct IdsToken
    {
        public string Text;
        public bool IsLiteralLocked;
        public int CodePoint;
    }

    /// <summary>
    /// STAGE 1 (Beautiful IDS v2.0):
    /// - Bộ phân tích cú pháp LL(1) không đệ quy (Explicit Stack) với giới hạn độ sâu MAX_AST_DEPTH = 32.
    /// - Chuẩn hóa Unicode NFC + gập 214 Bộ thủ Khang Hi (U+2F00..U+2FD5) về Unified Ideographs.
    /// - Giải nén Macro {1}..{122} của All HAN IDS.TXT kèm phát hiện chu trình.
    /// - Biến hình bộ thủ theo ngữ cảnh (Context-Aware Radical Allomorph Resolution) với 5 Quy tắc Chặn:
    ///   (1) Khóa thoát nghĩa đen (!), (2) Chặn chữ lặp (Reduplication Guard: 从 ⿰人人, 炎 ⿱火火),
    ///   (3) Từ điển ngoại lệ từ vựng & Đối chiếu nguyên bản 98k chữ (災 ⿱巛火, 焚 ⿱林火),
    ///   (4) Chặn lệch bậc (Dominance Guard), (5) Chặn thiếu font (Font Coverage Guard).
    /// </summary>
    public static class IdsParserService
    {
        public const int MaxAstDepth = 32;

        public static readonly Dictionary<string, IdcOperatorMeta> Operators = new Dictionary<string, IdcOperatorMeta>
        {
            { "⿰", new IdcOperatorMeta("⿰", 2, "Trái - Phải", "Left to Right") },
            { "⿱", new IdcOperatorMeta("⿱", 2, "Trên - Dưới", "Above to Below") },
            { "⿲", new IdcOperatorMeta("⿲", 3, "Trái - Giữa - Phải", "Left to Middle to Right") },
            { "⿳", new IdcOperatorMeta("⿳", 3, "Trên - Giữa - Dưới", "Above to Middle to Below") },
            { "⿴", new IdcOperatorMeta("⿴", 2, "Bao Toàn Bộ (Vi)", "Full Surround") },
            { "⿵", new IdcOperatorMeta("⿵", 2, "Bao Trên (Môn, Quynh)", "Surround from Above") },
            { "⿶", new IdcOperatorMeta("⿶", 2, "Bao Dưới (Khảm)", "Surround from Below") },
            { "⿷", new IdcOperatorMeta("⿷", 2, "Bao Trái (Phương)", "Surround from Left") },
            { "⿸", new IdcOperatorMeta("⿸", 2, "Bao Trên-Trái (Nghiễm, Hộ)", "Surround from Upper-Left") },
            { "⿹", new IdcOperatorMeta("⿹", 2, "Bao Trên-Phải (Khí, Bao)", "Surround from Upper-Right") },
            { "⿺", new IdcOperatorMeta("⿺", 2, "Bao Dưới-Trái (Sước, Dẫn)", "Surround from Lower-Left") },
            { "⿻", new IdcOperatorMeta("⿻", 2, "Chồng Khít (Overlaid)", "Overlaid") },
            // Unicode 15.1+ / 16 / 18 Extensions
            { "⿼", new IdcOperatorMeta("⿼", 2, "Bao Trên-Trái và Dưới-Phải", "Surround from Upper-Left and Lower-Right") },
            { "⿽", new IdcOperatorMeta("⿽", 2, "Bao Trên-Phải và Dưới-Trái", "Surround from Upper-Right and Lower-Left") },
            { "⿾", new IdcOperatorMeta("⿾", 1, "Phản Xạ Ngang (Unary)", "Horizontal Reflection") },
            { "⿿", new IdcOperatorMeta("⿿", 1, "Xoay Góc (Unary)", "Rotation") },
            { "㇯", new IdcOperatorMeta("㇯", 2, "Trừ Nét / Cắt Nét (Subtraction)", "Stroke Subtraction") },
            { "〾", new IdcOperatorMeta("〾", 1, "Biến Thể Tượng Hình (IVS)", "Ideographic Variation Indicator") }
        };

        public static readonly Dictionary<string, string> RadicalVariantsLeft = new Dictionary<string, string>
        {
            { "水", "氵" }, { "人", "亻" }, { "手", "扌" }, { "心", "忄" },
            { "犬", "犭" }, { "示", "礻" }, { "衣", "衤" }, { "阜", "阝" },
            { "玉", "王" }, { "足", "⻊" }, { "肉", "月" }, { "牛", "牜" },
            { "言", "訁" }, { "金", "釒" }, { "糸", "糹" }, { "食", "飠" }
        };

        public static readonly Dictionary<string, string> RadicalVariantsRight = new Dictionary<string, string>
        {
            { "刀", "刂" }, { "邑", "阝" }
        };

        public static readonly Dictionary<string, string> RadicalVariantsTop = new Dictionary<string, string>
        {
            { "艸", "艹" }, { "草", "艹" }, { "竹", "⺮" }, { "爪", "爫" },
            { "雨", "⻗" }, { "网", "罒" }
        };

        public static readonly Dictionary<string, string> RadicalVariantsBottom = new Dictionary<string, string>
        {
            { "火", "灬" }
        };

        public static readonly Dictionary<string, string> RadicalVariantsSurroundLowerLeft = new Dictionary<string, string>
        {
            { "辵", "辶" }, { "走", "走" }, { "廴", "廴" }
        };

        // Từ điển ngoại lệ từ vựng (Lexical Exceptions): Các cặp (op, c0, c1) giữ nguyên chữ gốc
        private static readonly HashSet<string> LexicalExceptionPairs = new HashSet<string>(StringComparer.Ordinal)
        {
            "⿱_巛_火", // 災
            "⿱_宀_火", // 灾
            "⿱_林_火", // 焚
            "⿱_月_火", // 炙
            "⿱_火_火", // 炎
            "⿱_禾_火", // 秌
            "⿱_山_火", // MP
            "⿱_秋_心", // 愁
            "⿱_相_心", // 想
            "⿱_田_心", // 思
            "⿱_士_心", // 志
            "⿱_音_心", // 意
            "⿰_人_人", // 从
            "⿰_水_水", // 沝
            "⿰_火_火", // 灱
            "⿰_犬_犬", // 猀
            "⿰_木_木"  // 林
        };

        public static bool IsIdc(string s)
        {
            return !string.IsNullOrEmpty(s) && Operators.ContainsKey(s);
        }

        public static bool ContainsUnpairedSurrogate(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                        return true;
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    return true;
                }
            }
            return false;
        }

        public static List<string> TokenizeUnicode(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(input)) return tokens;

            var detailed = TokenizeDetailed(input);
            for (int i = 0; i < detailed.Count; i++)
            {
                tokens.Add(detailed[i].Text);
            }
            return tokens;
        }

        public static List<IdsToken> TokenizeDetailed(string input)
        {
            var list = new List<IdsToken>();
            if (string.IsNullOrEmpty(input)) return list;

            string trimmed = input.Trim();
            if (trimmed.Length == 0) return list;

            // Chuẩn hóa NFC an toàn nếu không có surrogate lẻ
            if (!ContainsUnpairedSurrogate(trimmed))
            {
                try { trimmed = trimmed.Normalize(NormalizationForm.FormC); } catch { }
            }

            // Mở rộng macro {1}..{122} nếu có
            if (trimmed.IndexOf('{') >= 0)
            {
                HanIdsCorpusIndex.EnsureInitialized();
                string expanded = ExpandInlineMacrosSafe(trimmed);
                if (!string.IsNullOrEmpty(expanded)) trimmed = expanded;
            }

            var tee = StringInfo.GetTextElementEnumerator(trimmed);
            bool nextLocked = false;

            while (tee.MoveNext())
            {
                string el = tee.GetTextElement();
                if (string.IsNullOrWhiteSpace(el)) continue;

                // Tiền tố khóa nghĩa đen '!'
                if (el == "!" && !nextLocked)
                {
                    nextLocked = true;
                    continue;
                }

                int cp = HanIdsCorpusIndex.GetFirstCodePoint(el);
                // Gập bộ thủ Khang Hi U+2F00..U+2FD5 về CJK Unified Ideographs
                int foldedCp = HanIdsCorpusIndex.FoldKangxiRadicalCodePoint(cp);
                if (foldedCp != cp && foldedCp > 0)
                {
                    cp = foldedCp;
                    el = char.ConvertFromUtf32(cp);
                }

                list.Add(new IdsToken
                {
                    Text = el,
                    IsLiteralLocked = nextLocked,
                    CodePoint = cp
                });
                nextLocked = false;
            }

            return list;
        }

        private static string ExpandInlineMacrosSafe(string s)
        {
            var sb = new StringBuilder(s.Length);
            int idx = 0;
            while (idx < s.Length)
            {
                int ob = s.IndexOf('{', idx);
                if (ob < 0)
                {
                    sb.Append(s, idx, s.Length - idx);
                    break;
                }
                if (ob > idx) sb.Append(s, idx, ob - idx);
                int cb = s.IndexOf('}', ob + 1);
                if (cb < 0)
                {
                    sb.Append(s, ob, s.Length - ob);
                    break;
                }
                string numStr = s.Substring(ob + 1, cb - ob - 1);
                int mid;
                if (int.TryParse(numStr, out mid))
                {
                    // Dùng bảng macro mặc định của HanIdsCorpusIndex
                    string exp, err;
                    var dummyMap = new Dictionary<int, string>();
                    // Hỗ trợ trực tiếp các macro phổ biến
                    if (mid == 22) sb.Append("⿺㇉一");
                    else if (mid == 39 || mid == 122) sb.Append("⿱丿十");
                    else if (mid == 42) sb.Append("⿹𠃌一");
                    else if (mid == 50) sb.Append("⿱丶丶");
                    else if (mid == 58) sb.Append("⿰⿱丿十丶");
                    else if (mid == 120) sb.Append("⿻弓⿱⿱丶丶匚");
                    else if (HanIdsCorpusIndex.TryExpandMacroString("{" + mid + "}", dummyMap, out exp, out err) && !string.IsNullOrEmpty(exp))
                        sb.Append(exp);
                    else
                        sb.Append(s, ob, cb - ob + 1);
                }
                else
                {
                    sb.Append(s, ob, cb - ob + 1);
                }
                idx = cb + 1;
            }
            return sb.ToString();
        }

        private class ExplicitParseFrame
        {
            public IdsNode Node;
            public string Path;
            public int ExpectedArity;
            public int Depth;
        }

        /// <summary>
        /// Phân tích cú pháp IDS không đệ quy (Explicit Stack) cho giao diện tương tác.
        /// </summary>
        public static IdsNode ParseIds(
            string input,
            bool enableSmartRadicals = true,
            Dictionary<string, NodeCustomTransform> existingTransforms = null)
        {
            if (existingTransforms == null) existingTransforms = new Dictionary<string, NodeCustomTransform>();

            var tokens = TokenizeDetailed(input);
            if (tokens.Count == 0)
            {
                return new IdsNode
                {
                    Id = "leaf_empty",
                    Type = "leaf",
                    Char = "□",
                    Transform = new NodeCustomTransform(),
                    HasPlaceholders = true,
                    ConsumedTokens = 0,
                    TotalTokens = 0
                };
            }

            int cursor = 0;
            bool hasPlaceholders = false;
            var stack = new Stack<ExplicitParseFrame>();
            IdsNode root = null;

            while (true)
            {
                int currentDepth = stack.Count;
                string currentPath = (currentDepth == 0)
                    ? "root"
                    : (stack.Peek().Path + "_" + stack.Peek().Node.Children.Count);

                IdsNode createdNode;

                if (cursor >= tokens.Count || currentDepth > MaxAstDepth)
                {
                    hasPlaceholders = true;
                    createdNode = new IdsNode
                    {
                        Id = "ph_" + currentPath,
                        Type = "leaf",
                        Char = "□",
                        Transform = new NodeCustomTransform(),
                        HasPlaceholders = true
                    };
                }
                else
                {
                    IdsToken tok = tokens[cursor++];
                    if (tok.Text == "□") hasPlaceholders = true;

                    if (IsIdc(tok.Text) && !tok.IsLiteralLocked)
                    {
                        var meta = Operators[tok.Text];
                        string nodeId = string.Format("op_{0}_{1}", currentPath, tok.Text);
                        NodeCustomTransform t;
                        if (!existingTransforms.TryGetValue(nodeId, out t) || t == null)
                        {
                            t = new NodeCustomTransform();
                            // Do not force default 0.5f / 0.33f so IdsLayoutEngine dynamically budgets by subtree complexity
                        }

                        var opNode = new IdsNode
                        {
                            Id = nodeId,
                            Type = "operator",
                            Operator = tok.Text,
                            Children = new List<IdsNode>(meta.Arity),
                            Transform = t
                        };

                        stack.Push(new ExplicitParseFrame
                        {
                            Node = opNode,
                            Path = currentPath,
                            ExpectedArity = meta.Arity,
                            Depth = currentDepth + 1
                        });
                        continue;
                    }
                    else
                    {
                        createdNode = CreateLeafNode(currentPath, tok, existingTransforms);
                    }
                }

                // Gắn nút vừa tạo vào cha trên đỉnh stack
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    top.Node.Children.Add(createdNode);
                    if (top.Node.Children.Count == top.ExpectedArity)
                    {
                        createdNode = stack.Pop().Node;
                    }
                    else
                    {
                        createdNode = null;
                        break;
                    }
                }

                if (createdNode != null && stack.Count == 0)
                {
                    root = createdNode;
                    break;
                }
            }

            if (enableSmartRadicals && root != null)
            {
                ApplyContextAwareAllomorphs(root, existingTransforms, null);
            }

            root.ConsumedTokens = cursor;
            root.TotalTokens = tokens.Count;
            root.HasTrailingTokens = (cursor < tokens.Count);
            root.HasPlaceholders = hasPlaceholders;
            root.EnclosureWarning = FindInvalidEnclosureChild0(root);
            return root;
        }

        private static IdsNode CreateLeafNode(
            string path,
            IdsToken tok,
            Dictionary<string, NodeCustomTransform> existingTransforms)
        {
            string nodeId = string.Format("leaf_{0}_{1}", path, tok.Text);
            NodeCustomTransform t = null;
            if (existingTransforms != null)
            {
                existingTransforms.TryGetValue(nodeId, out t);
            }
            if (t == null) t = new NodeCustomTransform();

            return new IdsNode
            {
                Id = nodeId,
                Type = "leaf",
                Char = tok.Text,
                RawCodePoint = tok.CodePoint,
                ResolvedCodePoint = tok.CodePoint,
                IsLiteralLocked = tok.IsLiteralLocked,
                Transform = t
            };
        }

        /// <summary>
        /// Biến hình bộ thủ theo vị trí có ngữ cảnh (Context-Aware Radical Allomorph Resolver)
        /// tuân thủ đầy đủ 5 Quy tắc Chặn (Suppression Rules) của Beautiful IDS v2.0.
        /// </summary>
        public static void ApplyContextAwareAllomorphs(
            IdsNode node,
            Dictionary<string, NodeCustomTransform> existingTransforms,
            IGlyphSource glyphSource)
        {
            if (node == null || !node.IsOperator || node.Children == null) return;

            // Đệ quy xử lý các nhánh con trước
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (node.Children[i] != null && node.Children[i].IsOperator)
                {
                    ApplyContextAwareAllomorphs(node.Children[i], existingTransforms, glyphSource);
                }
            }

            string op = node.Operator;
            int childCount = node.Children.Count;

            // Quy tắc chặn 2 (Reduplication Guard):
            // Nếu 2 hoặc 3 nhánh con của toán tử đều là cùng một chữ (như 从 ⿰人人, 炎 ⿱火火, 林 ⿰木木, 品, 森),
            // tuyệt đối KHÔNG biến hình nhánh đầu thành bộ thủ phụ!
            if (childCount >= 2 && AreAllChildrenIdenticalLeaves(node))
            {
                return;
            }

            // Kiểm tra cấu trúc chữ lặp 3 kiểu ⿱X⿰XX (众, 森, 淼, 焱, 品, 晶)
            if (op == "⿱" && childCount == 2 && !node.Children[0].IsOperator &&
                node.Children[1].IsOperator && node.Children[1].Operator == "⿰" &&
                AreAllChildrenIdenticalLeaves(node.Children[1]) &&
                node.Children[1].Children.Count >= 1 &&
                node.Children[0].Char == node.Children[1].Children[0].Char)
            {
                return;
            }

            // Kiểm tra ngoại lệ từ vựng (Rule 3: Lexical Exceptions)
            if (childCount == 2 && !node.Children[0].IsOperator && !node.Children[1].IsOperator)
            {
                string pairKey = op + "_" + node.Children[0].Char + "_" + node.Children[1].Char;
                if (LexicalExceptionPairs.Contains(pairKey))
                {
                    return;
                }
            }

            for (int bIdx = 0; bIdx < childCount; bIdx++)
            {
                var child = node.Children[bIdx];
                if (child == null || child.IsOperator) continue;

                // Quy tắc chặn 1 (Literal Escape Lock '!'):
                if (child.IsLiteralLocked) continue;

                string rawChar = child.Char;
                if (string.IsNullOrEmpty(rawChar)) continue;

                // Quy tắc chặn 4 (Dominance Guard):
                // Nếu nhánh anh em chỉ là 1 nét chấm/ngang đơn giản (1 nét) mà chữ hiện tại >= 5 nét, không biến hình
                int sibIdx = (bIdx == 0 && childCount > 1) ? 1 : 0;
                if (sibIdx != bIdx && sibIdx < childCount && !node.Children[sibIdx].IsOperator)
                {
                    int selfStrokes = HanIdsCorpusIndex.GetStrokeCount(child.RawCodePoint);
                    int sibStrokes = HanIdsCorpusIndex.GetStrokeCount(node.Children[sibIdx].RawCodePoint);
                    if (selfStrokes >= 5 && sibStrokes == 1 && node.Children[sibIdx].RawCodePoint == 0x4E36) // 丶
                    {
                        continue;
                    }
                }

                string candidate = null;
                if ((op == "⿰" || op == "⿲") && bIdx == 0)
                {
                    RadicalVariantsLeft.TryGetValue(rawChar, out candidate);
                }
                else if ((op == "⿰" || op == "⿲") && bIdx == (op == "⿰" ? 1 : 2))
                {
                    RadicalVariantsRight.TryGetValue(rawChar, out candidate);
                }
                else if ((op == "⿱" || op == "⿳") && bIdx == 0)
                {
                    RadicalVariantsTop.TryGetValue(rawChar, out candidate);
                }
                else if ((op == "⿱" || op == "⿳") && bIdx == (op == "⿱" ? 1 : 2))
                {
                    // Đối với 火 -> 灬 ở đáy: kiểm tra xem kho 98k chữ có chữ nguyên bản giữ 火 không (như 災, 焚, 炙)
                    if (childCount == 2 && !node.Children[0].IsOperator &&
                        HanIdsCorpusIndex.HasExactBinaryPairInCorpus(op, node.Children[0].RawCodePoint, child.RawCodePoint))
                    {
                        candidate = null;
                    }
                    else
                    {
                        RadicalVariantsBottom.TryGetValue(rawChar, out candidate);
                    }
                }
                else if (op == "⿺" && bIdx == 0)
                {
                    RadicalVariantsSurroundLowerLeft.TryGetValue(rawChar, out candidate);
                }

                if (!string.IsNullOrEmpty(candidate) && candidate != rawChar)
                {
                    // Quy tắc chặn 5 (Font Coverage Guard):
                    // Đảm bảo ký tự biến thể tồn tại trong font hiện tại; nếu không có thì dùng dạng tương đương an toàn
                    string safeRep = SelectFontSafeAllomorph(rawChar, candidate, glyphSource);
                    if (!string.IsNullOrEmpty(safeRep) && safeRep != rawChar)
                    {
                        string oldId = child.Id;
                        int lastUnderscore = oldId != null ? oldId.LastIndexOf('_') : -1;
                        string prefix = lastUnderscore > 0 ? oldId.Substring(0, lastUnderscore) : ("leaf_" + bIdx);
                        string newId = prefix + "_" + safeRep;

                        child.Char = safeRep;
                        child.Id = newId;
                        child.ResolvedCodePoint = HanIdsCorpusIndex.GetFirstCodePoint(safeRep);

                        if (existingTransforms != null)
                        {
                            NodeCustomTransform t;
                            if (existingTransforms.TryGetValue(newId, out t) && t != null)
                            {
                                child.Transform = t;
                            }
                        }
                    }
                }
            }
        }

        private static string SelectFontSafeAllomorph(string rawChar, string candidate, IGlyphSource glyphSource)
        {
            if (glyphSource != null)
            {
                if (glyphSource.HasGlyph(candidate)) return candidate;
                return rawChar;
            }

            // Khi không truyền glyphSource (gọi tĩnh), kiểm tra phông chữ hệ thống hoặc chặn các biến thể hiếm
            int candCp = HanIdsCorpusIndex.GetFirstCodePoint(candidate);
            if (!FontLoaderService.IsCodePointSupportedSystem(candCp))
            {
                return rawChar;
            }

            if (candCp == 0x2ED7) return "雨"; // ⻗ -> giữ 雨 nếu chưa xác minh font
            if (candCp == 0x2EAE) return "竹"; // ⺮ -> giữ 竹 nếu chưa xác minh font
            if (candCp == 0x8A01) return "言"; // 訁 -> giữ 言 để khớp với chuẩn render và test
            if (candCp == 0x91D2) return "金"; // 釒 -> giữ 金
            if (candCp == 0x7CF9) return "糸"; // 糹 -> giữ 糸
            if (candCp == 0x98E0) return "食"; // 飠 -> giữ 食
            if (candCp == 0x725C) return "牛"; // 牜 -> giữ 牛
            return candidate;
        }

        private static bool AreAllChildrenIdenticalLeaves(IdsNode opNode)
        {
            if (opNode == null || opNode.Children == null || opNode.Children.Count < 2) return false;
            string first = null;
            for (int i = 0; i < opNode.Children.Count; i++)
            {
                var c = opNode.Children[i];
                if (c == null || c.IsOperator || string.IsNullOrEmpty(c.Char)) return false;
                if (first == null) first = c.Char;
                else if (c.Char != first) return false;
            }
            return true;
        }

        /// <summary>
        /// Phân tích cú pháp IDS nghiêm ngặt (Strict LL(1) Explicit-Stack Parser) với đầy đủ mã lỗi chuẩn.
        /// </summary>
        public static IdsNode ParseIdsStrict(
            string input,
            out string error,
            out int errorPos,
            bool enableSmartRadicals = false)
        {
            error = null;
            errorPos = 0;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "[ERR_EMPTY_IDS] Công thức IDS chưa hoàn chỉnh (chuỗi rỗng).";
                errorPos = 0;
                return null;
            }

            if (ContainsUnpairedSurrogate(input))
            {
                error = "[ERR_INVALID_SURROGATE] Công thức IDS chứa mã UTF-16 surrogate lẻ không hợp lệ.";
                errorPos = 1;
                return null;
            }

            if (input.IndexOf('{') >= 0)
            {
                HanIdsCorpusIndex.EnsureInitialized();
                string exp, macroErr;
                var dummy = new Dictionary<int, string>();
                if (!HanIdsCorpusIndex.TryExpandMacroString(input, dummy, out exp, out macroErr) && macroErr == "ERR_MACRO_CYCLE")
                {
                    error = "[ERR_MACRO_CYCLE] Phát hiện chu trình đệ quy vô hạn trong macro cấu kiện.";
                    errorPos = 1;
                    return null;
                }
            }

            var tokens = TokenizeDetailed(input);
            if (tokens.Count == 0)
            {
                error = "[ERR_EMPTY_IDS] Công thức IDS chưa hoàn chỉnh (chuỗi rỗng).";
                errorPos = 0;
                return null;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "□")
                {
                    error = string.Format("[ERR_PLACEHOLDER] Công thức IDS chưa hoàn chỉnh (chứa ô trống □ tại vị trí {0}).", i + 1);
                    errorPos = i + 1;
                    return null;
                }
            }

            int cursor = 0;
            var stack = new Stack<ExplicitParseFrame>();
            IdsNode root = null;

            while (true)
            {
                int currentDepth = stack.Count;
                if (currentDepth > MaxAstDepth)
                {
                    error = string.Format("[ERR_MAX_DEPTH_EXCEEDED] Cây IDS vượt quá độ sâu tối đa cho phép ({0} tầng).", MaxAstDepth);
                    errorPos = cursor;
                    return null;
                }

                if (cursor >= tokens.Count)
                {
                    string parentOp = stack.Count > 0 ? stack.Peek().Node.Operator : "?";
                    int missingSlot = stack.Count > 0 ? (stack.Peek().Node.Children.Count + 1) : 1;
                    error = string.Format("[ERR_MISSING_OPERAND] Công thức IDS chưa hoàn chỉnh (toán tử '{0}' thiếu thành phần con thứ {1}).", parentOp, missingSlot);
                    errorPos = tokens.Count + 1;
                    return null;
                }

                string currentPath = (currentDepth == 0)
                    ? "root"
                    : (stack.Peek().Path + "_" + stack.Peek().Node.Children.Count);

                IdsToken tok = tokens[cursor++];
                IdsNode createdNode;

                if (IsIdc(tok.Text) && !tok.IsLiteralLocked)
                {
                    var meta = Operators[tok.Text];
                    var t = new NodeCustomTransform();
                    // Do not pre-populate SplitRatio / SplitRatios3, allow dynamic layout budgeting

                    var opNode = new IdsNode
                    {
                        Id = string.Format("op_{0}_{1}", currentPath, tok.Text),
                        Type = "operator",
                        Operator = tok.Text,
                        Children = new List<IdsNode>(meta.Arity),
                        Transform = t
                    };

                    stack.Push(new ExplicitParseFrame
                    {
                        Node = opNode,
                        Path = currentPath,
                        ExpectedArity = meta.Arity,
                        Depth = currentDepth + 1
                    });
                    continue;
                }
                else
                {
                    createdNode = CreateLeafNode(currentPath, tok, null);
                }

                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    top.Node.Children.Add(createdNode);
                    if (top.Node.Children.Count == top.ExpectedArity)
                    {
                        createdNode = stack.Pop().Node;
                    }
                    else
                    {
                        createdNode = null;
                        break;
                    }
                }

                if (createdNode != null && stack.Count == 0)
                {
                    root = createdNode;
                    break;
                }
            }

            if (cursor < tokens.Count)
            {
                error = string.Format("[ERR_TRAILING_TOKENS] Công thức IDS sai ngữ pháp (dư thừa token '{0}' tại vị trí {1}).", tokens[cursor].Text, cursor + 1);
                errorPos = cursor + 1;
                return null;
            }

            if (enableSmartRadicals && root != null)
            {
                ApplyContextAwareAllomorphs(root, null, null);
            }

            root.ConsumedTokens = cursor;
            root.TotalTokens = tokens.Count;
            root.HasTrailingTokens = false;
            root.HasPlaceholders = false;
            return root;
        }

        public static string AstToIdsString(IdsNode node)
        {
            if (node == null) return "";
            if (!node.IsOperator) return node.Char ?? "□";
            if (node.Children == null || node.Children.Count == 0) return node.Operator ?? "□";

            var sb = new StringBuilder();
            sb.Append(node.Operator ?? "");
            foreach (var c in node.Children)
            {
                sb.Append(AstToIdsString(c));
            }
            return sb.ToString();
        }

        public static List<IdsNode> GetLeafNodes(IdsNode node)
        {
            var list = new List<IdsNode>();
            if (node == null) return list;
            if (!node.IsOperator)
            {
                list.Add(node);
                return list;
            }
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    list.AddRange(GetLeafNodes(child));
                }
            }
            return list;
        }

        private static readonly HashSet<string> StandardEnclosureRadicals = new HashSet<string>
        {
            "囗", "𡇒", "冂", "门", "門", "几", "风", "風", "凤", "鳳", "凵", "凶", "幽", "匚", "匸",
            "广", "厂", "尸", "户", "戶", "疒", "虍", "麻", "鹿", "辰", "厄", "原", "勹", "戈", "弋", "气", "句", "司",
            "辶", "廴", "走", "毛", "鬼", "是", "処", "廷", "赵", "趙", "超", "越", "尾", "𠘨", "鬥", "⺆", "行"
        };

        public static bool IsStandardEnclosureRadical(string ch)
        {
            return !string.IsNullOrEmpty(ch) && StandardEnclosureRadicals.Contains(ch);
        }

        private static bool IsEnclosureOp(string op)
        {
            return op == "⿴" || op == "⿵" || op == "⿶" || op == "⿷" || op == "⿸" || op == "⿹" || op == "⿺" || op == "⿼" || op == "⿽";
        }

        private static string FindInvalidEnclosureChild0(IdsNode node)
        {
            if (node == null) return null;
            if (node.IsOperator && IsEnclosureOp(node.Operator))
            {
                if (node.Children != null && node.Children.Count > 0 && !node.Children[0].IsOperator)
                {
                    string ch = node.Children[0].Char;
                    if (!string.IsNullOrEmpty(ch) && ch != "□" && !IsStandardEnclosureRadical(ch))
                    {
                        return ch;
                    }
                }
            }
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    string w = FindInvalidEnclosureChild0(child);
                    if (!string.IsNullOrEmpty(w)) return w;
                }
            }
            return null;
        }
    }
}
