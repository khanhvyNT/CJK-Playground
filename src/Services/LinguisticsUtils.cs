using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CJKPlayground.Services
{
    public enum InputScriptType
    {
        Vietnamese,
        Pinyin,
        CantoneseJyutping,
        JapaneseKana,
        KoreanHangul,
        KoreanRomaja,
        Codepoint,
        PrefixQuery,
        LatinGeneral
    }

    public class ParsedSearchQuery
    {
        public string Raw { get; set; }
        public string Prefix { get; set; }
        public string CleanQuery { get; set; }
        public string Scope { get; set; } // "all", "vietnamese", "nom", "mandarin", "cantonese", "japanese", "korean", "semantic", "radical", "codepoint"
        public List<string> Tokens { get; set; }

        public ParsedSearchQuery()
        {
            Tokens = new List<string>();
        }
    }

    public static class LinguisticsUtils
    {
        public static string NormalizeNfc(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
        }

        public static string NormalizeVietnameseTonePlacement(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();

            // Qui -> quy
            s = Regex.Replace(s, @"\bqui\b", "quy");

            // Convert old-style diphthong tone marks to new style
            var toneMap = new Dictionary<string, string>
            {
                { "oà", "òa" }, { "oá", "óa" }, { "oả", "ỏa" }, { "oã", "õa" }, { "oạ", "ọa" },
                { "oè", "òe" }, { "oé", "óe" }, { "oẻ", "ỏe" }, { "oẽ", "õe" }, { "oẹ", "ọe" },
                { "uỳ", "ủy" }, { "uý", "úy" }, { "uỷ", "ủy" }, { "uỹ", "ũy" }, { "uỵ", "ụy" },
                { "uơ", "ươ" }, { "uờ", "ườ" }, { "uớ", "ướ" }, { "uở", "ưở" }, { "uỡ", "ưỡ" }, { "uợ", "ượ" }
            };

            foreach (var kvp in toneMap)
            {
                s = s.Replace(kvp.Key, kvp.Value);
            }

            return s.Normalize(NormalizationForm.FormC);
        }

        public static string StripVietnameseDiacritics(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            string s = str.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder();
            foreach (char c in s)
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            s = sb.ToString();

            // Replace d-bar
            s = s.Replace("đ", "d").Replace("Đ", "d");

            // Precomposed residual replacements
            s = Regex.Replace(s, "[àáảãạâầấẩẫậăằắẳẵặ]", "a", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "[èéẻẽẹêềếểễệ]", "e", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "[ìíỉĩị]", "i", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "[òóỏõọôồốổỗộơờớởỡợ]", "o", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "[ùúủũụưừứửữự]", "u", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "[ỳýỷỹỵ]", "y", RegexOptions.IgnoreCase);

            return s.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        public static string StripPinyinTones(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            string s = str.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            s = sb.ToString();

            s = Regex.Replace(s, "[üǖǘǚǜvV]", "u", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "[1-5]", "");
            return s.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        public static string StripCantoneseTones(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return Regex.Replace(str, "[1-6]", "").Trim().ToLowerInvariant();
        }

        #region Korean Hangul Romanization
        private const int HangulBase = 0xAC00;
        private const int HangulEnd = 0xD7A3;

        private static readonly string[] ChoseongRomaja = new[]
        {
            "g", "kk", "n", "d", "tt",
            "r", "m", "b", "pp", "s",
            "ss", "", "j", "jj", "ch",
            "k", "t", "p", "h"
        };

        private static readonly string[] JungseongRomaja = new[]
        {
            "a", "ae", "ya", "yae", "eo",
            "e", "yeo", "ye", "o", "wa",
            "wae", "oe", "yo", "u", "wo",
            "we", "wi", "yu", "eu", "ui",
            "i"
        };

        private static readonly string[] JongseongRomaja = new[]
        {
            "", "k", "k", "ks", "n",
            "nj", "nh", "t", "l", "lg",
            "lm", "lb", "ls", "lt", "lp",
            "lh", "m", "p", "ps", "t",
            "t", "ng", "t", "t", "k",
            "t", "p", "h"
        };

        public static string HangulToRomaja(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (char.IsSurrogatePair(text, i)) i++;

                if (cp >= HangulBase && cp <= HangulEnd)
                {
                    int syllableIndex = cp - HangulBase;
                    int choseong = syllableIndex / 588;
                    int jungseong = (syllableIndex % 588) / 28;
                    int jongseong = syllableIndex % 28;

                    sb.Append(ChoseongRomaja[choseong]);
                    sb.Append(JungseongRomaja[jungseong]);
                    sb.Append(JongseongRomaja[jongseong]);
                }
                else
                {
                    sb.Append(char.ConvertFromUtf32(cp));
                }
            }
            return sb.ToString().ToLowerInvariant();
        }
        #endregion

        #region Japanese Kana to Romaji
        private static readonly Dictionary<string, string> KanaDigraphs = new Dictionary<string, string>
        {
            { "キャ", "kya" }, { "キュ", "kyu" }, { "キョ", "kyo" },
            { "シャ", "sha" }, { "シュ", "shu" }, { "ショ", "sho" },
            { "チャ", "cha" }, { "チュ", "chu" }, { "チョ", "cho" },
            { "ニャ", "nya" }, { "ニュ", "nyu" }, { "ニョ", "nyo" },
            { "ヒャ", "hya" }, { "ヒュ", "hyu" }, { "ヒョ", "hyo" },
            { "ミャ", "mya" }, { "ミュ", "myu" }, { "ミョ", "myo" },
            { "リャ", "rya" }, { "リュ", "ryu" }, { "リョ", "ryo" },
            { "ギャ", "gya" }, { "ギュ", "gyu" }, { "ギョ", "gyo" },
            { "ジャ", "ja" },  { "ジュ", "ju" },  { "ジョ", "jo" },
            { "ビャ", "bya" }, { "ビュ", "byu" }, { "ビョ", "byo" },
            { "ピャ", "pya" }, { "ピュ", "pyu" }, { "ピョ", "pyo" }
        };

        private static readonly Dictionary<char, string> KanaRomajiMap = new Dictionary<char, string>
        {
            { 'ア', "a" },   { 'イ', "i" },   { 'ウ', "u" },   { 'エ', "e" },   { 'オ', "o" },
            { 'カ', "ka" },  { 'キ', "ki" },  { 'ク', "ku" },  { 'ケ', "ke" },  { 'コ', "ko" },
            { 'サ', "sa" },  { 'シ', "shi" }, { 'ス', "su" },  { 'セ', "se" },  { 'ソ', "so" },
            { 'タ', "ta" },  { 'チ', "chi" }, { 'ツ', "tsu" }, { 'テ', "te" },  { 'ト', "to" },
            { 'ナ', "na" },  { 'ニ', "ni" },  { 'ヌ', "nu" },  { 'ネ', "ne" },  { 'ノ', "no" },
            { 'ハ', "ha" },  { 'ヒ', "hi" },  { 'フ', "fu" },  { 'ヘ', "he" },  { 'ホ', "ho" },
            { 'マ', "ma" },  { 'ミ', "mi" },  { 'ム', "mu" },  { 'メ', "me" },  { 'モ', "mo" },
            { 'ヤ', "ya" },  { 'ユ', "yu" },  { 'ヨ', "yo" },
            { 'ラ', "ra" },  { 'リ', "ri" },  { 'ル', "ru" },  { 'レ', "re" },  { 'ロ', "ro" },
            { 'ワ', "wa" },  { 'ヲ', "wo" },  { 'ン', "n" },
            { 'ガ', "ga" },  { 'ギ', "gi" },  { 'グ', "gu" },  { 'ゲ', "ge" },  { 'ゴ', "go" },
            { 'ザ', "za" },  { 'ジ', "ji" },  { 'ズ', "zu" },  { 'ゼ', "ze" },  { 'ゾ', "zo" },
            { 'ダ', "da" },  { 'ヂ', "ji" },  { 'ヅ', "zu" },  { 'デ', "de" },  { 'ド', "do" },
            { 'バ', "ba" },  { 'ビ', "bi" },  { 'ブ', "bu" },  { 'ベ', "be" },  { 'ボ', "bo" },
            { 'パ', "pa" },  { 'ピ', "pi" },  { 'プ', "pu" },  { 'ペ', "pe" },  { 'ポ', "po" }
        };

        public static string HiraganaToKatakana(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            var sb = new StringBuilder();
            foreach (char c in str)
            {
                if (c >= '\u3041' && c <= '\u3096')
                    sb.Append((char)(c + 0x60));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string KanaToRomaji(string kana)
        {
            if (string.IsNullOrEmpty(kana)) return "";
            string kata = HiraganaToKatakana(kana).Replace(".", "").Replace("-", "");
            var res = new StringBuilder();
            int i = 0;

            while (i < kata.Length)
            {
                // Sokuon (ッ / っ)
                if (kata[i] == 'ッ' || kata[i] == 'っ')
                {
                    if (i + 1 < kata.Length)
                    {
                        char next = kata[i + 1];
                        string nextRomaji = KanaRomajiMap.ContainsKey(next) ? KanaRomajiMap[next] : "";
                        if (!string.IsNullOrEmpty(nextRomaji))
                        {
                            res.Append(nextRomaji[0]); // double consonant
                        }
                    }
                    i++;
                    continue;
                }

                // Digraph (キャ, etc.)
                if (i + 1 < kata.Length)
                {
                    string pair = kata.Substring(i, 2);
                    if (KanaDigraphs.ContainsKey(pair))
                    {
                        res.Append(KanaDigraphs[pair]);
                        i += 2;
                        continue;
                    }
                }

                // Choonpu (ー)
                if (kata[i] == 'ー')
                {
                    if (res.Length > 0)
                    {
                        char last = res[res.Length - 1];
                        if ("aiueo".IndexOf(last) >= 0)
                            res.Append(last);
                    }
                    i++;
                    continue;
                }

                if (KanaRomajiMap.ContainsKey(kata[i]))
                    res.Append(KanaRomajiMap[kata[i]]);
                else
                    res.Append(kata[i]);

                i++;
            }

            return res.ToString().ToLowerInvariant();
        }
        #endregion

        #region Script Detection & Query Parsing
        public static InputScriptType DetectInputScript(string input)
        {
            string trimmed = (input ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed)) return InputScriptType.LatinGeneral;

            // Prefix query (v:, nom:, py:, jy:, ja:, ko:, def:, r:, u:)
            if (Regex.IsMatch(trimmed, @"^(v|kv|nom|m|py|c|jy|j|ja|k|ko|d|def|meaning|r|u):", RegexOptions.IgnoreCase))
            {
                return InputScriptType.PrefixQuery;
            }

            // Codepoint
            if (Regex.IsMatch(trimmed, @"^(u\+|0x)[0-9a-f]{4,6}$", RegexOptions.IgnoreCase))
            {
                return InputScriptType.Codepoint;
            }

            // Single ideograph directly pasted
            if (trimmed.Length == 1 || (trimmed.Length == 2 && char.IsSurrogatePair(trimmed, 0)))
            {
                int cp = char.ConvertToUtf32(trimmed, 0);
                if ((cp >= 0x4E00 && cp <= 0x9FFF) ||
                    (cp >= 0x3400 && cp <= 0x4DBF) ||
                    (cp >= 0x20000 && cp <= 0x2A6DF) ||
                    (cp >= 0x2F00 && cp <= 0x2FDF))
                {
                    return InputScriptType.Codepoint;
                }
            }

            // Korean Hangul
            if (Regex.IsMatch(trimmed, @"[\uac00-\ud7af\u1100-\u11ff]"))
                return InputScriptType.KoreanHangul;

            // Japanese Kana
            if (Regex.IsMatch(trimmed, @"[\u3040-\u309f\u30a0-\u30ff]"))
                return InputScriptType.JapaneseKana;

            // Vietnamese distinct marks
            if (Regex.IsMatch(trimmed, @"[đĐươưỡợởớờứừửữựấầẩẫậắằẳẵặêếềểễệôốồổỗộ]", RegexOptions.IgnoreCase))
                return InputScriptType.Vietnamese;

            // Distinct Pinyin tone marks
            if (Regex.IsMatch(trimmed, @"[āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜ]"))
                return InputScriptType.Pinyin;

            // Vietnamese tones
            if (Regex.IsMatch(trimmed, @"[àáảãạèéẻẽẹìíỉĩịòóỏõọùúủũụỳýỷỹỵ]", RegexOptions.IgnoreCase))
                return InputScriptType.Vietnamese;

            // Jyutping tone numbers
            if (Regex.IsMatch(trimmed, @"^[a-z]+[1-6]$", RegexOptions.IgnoreCase))
                return InputScriptType.CantoneseJyutping;

            // Korean Romaja keywords
            if (Regex.IsMatch(trimmed, @"^(cheon|myeong|seong|hyeon|gyeong|ryong|kwon|baek|shin|yeom|chul|jong)$", RegexOptions.IgnoreCase))
                return InputScriptType.KoreanRomaja;

            return InputScriptType.LatinGeneral;
        }

        public static ParsedSearchQuery ParseSearchQuery(string input)
        {
            string raw = (input ?? "").Trim();
            var result = new ParsedSearchQuery { Raw = raw };

            Match match = Regex.Match(raw, @"^([a-z0-9]+):(.*)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string p = match.Groups[1].Value.ToLowerInvariant();
                string clean = match.Groups[2].Value.Trim();
                result.Prefix = p;
                result.CleanQuery = clean;

                if (p == "v" || p == "kv") result.Scope = "vietnamese";
                else if (p == "nom") result.Scope = "nom";
                else if (p == "ids") result.Scope = "ids";
                else if (p == "m" || p == "py") result.Scope = "mandarin";
                else if (p == "c" || p == "jy") result.Scope = "cantonese";
                else if (p == "j" || p == "ja") result.Scope = "japanese";
                else if (p == "k" || p == "ko") result.Scope = "korean";
                else if (p == "d" || p == "def" || p == "meaning") result.Scope = "semantic";
                else if (p == "r") result.Scope = "radical";
                else if (p == "u") result.Scope = "codepoint";
                else result.Scope = "all";

                string[] tokens = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                result.Tokens.AddRange(tokens);
                return result;
            }

            if (Regex.IsMatch(raw, @"^(u\+|0x)[0-9a-f]{4,6}$", RegexOptions.IgnoreCase))
            {
                result.Prefix = "u";
                result.CleanQuery = raw;
                result.Scope = "codepoint";
                result.Tokens.Add(raw);
                return result;
            }

            if (raw.IndexOfAny(new[] { '⿰', '⿱', '⿲', '⿳', '⿴', '⿵', '⿶', '⿷', '⿸', '⿹', '⿺', '⿻' }) >= 0)
            {
                result.Prefix = "ids";
                result.CleanQuery = raw;
                result.Scope = "ids";
                result.Tokens.Add(raw);
                return result;
            }

            result.CleanQuery = raw;
            result.Scope = "all";
            string[] parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            result.Tokens.AddRange(parts);
            return result;
        }

        public static bool IsPureChuNom(int codePoint, List<string> nomReadings, List<string> vnReadings, List<string> mandarinReadings, string definition)
        {
            // 1. Chữ Nôm chuẩn xác xuất hiện từ Plane 2 (Ext B trở đi: U+20000+) hoặc vùng PUA (U+E000 - U+F8FF)
            if (codePoint >= 0x20000 || (codePoint >= 0xE000 && codePoint <= 0xF8FF))
            {
                bool hasNom = (nomReadings != null && nomReadings.Count > 0);
                string defLower = (definition ?? "").ToLowerInvariant();
                bool hasNomDef = defLower.Contains("chữ nôm") || defLower.Contains("chu nom") || Regex.IsMatch(defLower, @"\bchữ nôm\b|\bâm nôm\b");
                bool hasVn = (vnReadings != null && vnReadings.Count > 0);

                return hasNom || hasNomDef || hasVn;
            }

            // 2. Với các chữ trong BMP (U+4E00 - U+9FFF) hoặc Ext A (U+3400 - U+4DBF):
            // Đây là các Chữ Hán kinh điển (Hán tự) đọc theo âm Hán Việt (như 李, 理, 里, 天, 人...).
            // Tuyệt đối KHÔNG coi chữ Hán đọc âm Hán Việt là Chữ Nôm thuần túy!
            bool hasMandarin = mandarinReadings != null && mandarinReadings.Count > 0;
            bool hasVnReading = vnReadings != null && vnReadings.Count > 0;
            string def = (definition ?? "").ToLowerInvariant();
            bool explicitNomOnly = (def.Contains("chữ nôm thuần túy") || def.Contains("vần nôm") || (def.Contains("chữ nôm") && !def.Contains("chữ hán")));

            if (explicitNomOnly && !hasMandarin && !hasVnReading)
            {
                return true;
            }

            return false;
        }
        #endregion
    }
}
