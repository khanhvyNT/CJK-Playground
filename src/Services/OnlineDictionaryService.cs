using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class OnlineDictionaryService
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly HttpClient HttpClientInstance;
        private static readonly Dictionary<string, DictionaryLookupResult> Cache = new Dictionary<string, DictionaryLookupResult>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new object();

        static OnlineDictionaryService()
        {
            try
            {
                ServicePointManager.DefaultConnectionLimit = 64;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            HttpClientInstance = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            HttpClientInstance.DefaultRequestHeaders.UserAgent.ParseAdd("CJKPlayground-Win32/1.0 (Windows NT 10.0; Win64; x64)");
        }

        public async Task<DictionaryLookupResult> LookupAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new DictionaryLookupResult { Query = "" };

            query = query.Trim();

            // Check in-memory cache first (0 ms response)
            lock (CacheLock)
            {
                DictionaryLookupResult cached;
                if (Cache.TryGetValue(query, out cached))
                    return cached;
            }

            var result = new DictionaryLookupResult
            {
                Query = query,
                IsSingleChar = IsSingleIdeograph(query)
            };

            if (result.IsSingleChar)
            {
                result.CodePoint = char.ConvertToUtf32(query, 0);
                result.PrimaryWebUrl = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(query);
                await LookupSingleCharacterAsync(query, result);
            }
            else
            {
                result.PrimaryWebUrl = "https://hvdic.thivien.net/hv/" + Uri.EscapeDataString(query);
                await LookupReadingOrWordAsync(query, result);
            }

            // Local fallback heuristics if needed
            ApplyLocalHeuristics(query, result);

            // Store in cache
            lock (CacheLock)
            {
                if (!Cache.ContainsKey(query))
                    Cache[query] = result;
            }

            return result;
        }

        public static bool IsSingleIdeograph(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length == 1)
            {
                int c = text[0];
                return (c >= 0x4E00 && c <= 0x9FFF) ||
                       (c >= 0x3400 && c <= 0x4DBF) ||
                       (c >= 0x2E80 && c <= 0x2FDF) ||
                       (c >= 0xF900 && c <= 0xFAFF);
            }
            if (char.IsSurrogatePair(text, 0) && text.Length == 2)
            {
                int cp = char.ConvertToUtf32(text, 0);
                return (cp >= 0x20000 && cp <= 0x2FA1F) ||
                       (cp >= 0x30000 && cp <= 0x323AF);
            }
            return false;
        }

        #region Single Character Lookup
        private async Task LookupSingleCharacterAsync(string character, DictionaryLookupResult result)
        {
            // 1. Query Thi Vien readings (Han-Viet, Nom, Pinyin)
            var t1 = QueryThiVienTranscriptAsync(character, 1); // Han-Viet
            var t2 = QueryThiVienTranscriptAsync(character, 3); // Chu Nom
            var t3 = QueryThiVienTranscriptAsync(character, 2); // Pinyin

            // 2. Query Thi Vien Dictionary HTML (Thieu Chuu, Tran Van Chanh, Nguyen Quoc Hung, etc.)
            var t4 = QueryThiVienHtmlAsync(character, result);

            // 3. Query Wiktionary
            var t5 = QueryWiktionaryAsync(character, result);

            await Task.WhenAll(t1, t2, t3, t4, t5);

            if (t1.Result != null && t1.Result.Count > 0)
            {
                foreach (var r in t1.Result)
                    if (!result.HanVietReadings.Contains(r)) result.HanVietReadings.Add(r);
                result.SourcesConsulted.Add("Thi Viện (Hán-Việt)");
            }

            if (t2.Result != null && t2.Result.Count > 0)
            {
                foreach (var r in t2.Result)
                    if (!result.NomReadings.Contains(r)) result.NomReadings.Add(r);
                result.SourcesConsulted.Add("Thi Viện (Chữ Nôm)");
            }

            if (t3.Result != null && t3.Result.Count > 0)
            {
                foreach (var r in t3.Result)
                    if (!result.PinyinReadings.Contains(r)) result.PinyinReadings.Add(r);
                result.SourcesConsulted.Add("Thi Viện (Pinyin)");
            }

            int charCp = result.CodePoint > 0 ? result.CodePoint : char.ConvertToUtf32(character, 0);
            bool isPlane2OrPua = (charCp >= 0x20000 || (charCp >= 0xE000 && charCp <= 0xF8FF));

            if (!isPlane2OrPua)
            {
                // In BMP, any Nom reading that merely duplicates the Han-Viet reading is not genuine Nom
                result.NomReadings.RemoveAll(r => result.HanVietReadings.Exists(hv => string.Equals(hv, r, StringComparison.OrdinalIgnoreCase)));
            }

            bool hasNom = result.NomReadings.Count > 0;
            string dataSource;
            if (isPlane2OrPua && hasNom && result.HanVietReadings.Count == 0)
                dataSource = "[Online API] Thi Viện (Chữ Nôm)";
            else if (isPlane2OrPua && hasNom && result.HanVietReadings.Count > 0)
                dataSource = "[Online API] Thi Viện (Hán + Nôm)";
            else
                dataSource = "[Online API] Thi Viện (Hán-Việt)";

            var primaryItem = new OnlineSearchResultItem
            {
                Character = character,
                CodePoint = result.CodePoint,
                DataSource = dataSource,
                MatchedReading = result.HanVietReadings.Count > 0 ? result.HanVietReadings[0] : (result.NomReadings.Count > 0 ? result.NomReadings[0] : ""),
                HanViet = string.Join(", ", result.HanVietReadings),
                Nom = string.Join(", ", result.NomReadings),
                Pinyin = string.Join(", ", result.PinyinReadings),
                TotalStrokes = result.StrokeCount,
                Radical = result.Radical,
                WebUrl = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(character),
                WebCitation = "Thi Viện Hán Nôm (hvdic.thivien.net)",
                Definitions = new List<DictionaryDefinition>(result.Definitions)
            };
            result.Items.Add(primaryItem);
        }
        #endregion

        #region Reading or Word Lookup (e.g. 'ly', 'thien', 'troi', 'trời', 'nguyen', 'nguyễn')
        private async Task LookupReadingOrWordAsync(string reading, DictionaryLookupResult result)
        {
            var readingVariants = new List<string> { reading };
            string lower = reading.ToLowerInvariant();
            if (lower != reading && !readingVariants.Contains(lower))
                readingVariants.Add(lower);

            // Tone expansions
            string norm = LinguisticsUtils.NormalizeVietnameseTonePlacement(lower);
            if (!readingVariants.Contains(norm)) readingVariants.Add(norm);
            string toneless = LinguisticsUtils.StripVietnameseDiacritics(norm);
            if (!readingVariants.Contains(toneless)) readingVariants.Add(toneless);

            if (lower == "troi" && !readingVariants.Contains("trời")) readingVariants.Add("trời");
            if (lower == "thien" && !readingVariants.Contains("thiên")) readingVariants.Add("thiên");
            if (lower == "nhan" && !readingVariants.Contains("nhân")) readingVariants.Add("nhân");
            if (lower == "quoc" && !readingVariants.Contains("quốc")) readingVariants.Add("quốc");
            if (lower == "tam" && !readingVariants.Contains("tâm")) readingVariants.Add("tâm");
            if (lower == "nguyen" && !readingVariants.Contains("nguyễn")) readingVariants.Add("nguyễn");

            var tasks = new List<Task>();
            var hanCharLists = new List<Task<List<string>>>();
            var nomCharLists = new List<Task<List<string>>>();

            foreach (var r in readingVariants)
            {
                var tHan = QueryThiVienHanByReadingAsync(r, 1);
                var tNom = QueryThiVienHanByReadingAsync(r, 3);
                hanCharLists.Add(tHan);
                nomCharLists.Add(tNom);
                tasks.Add(tHan);
                tasks.Add(tNom);
            }

            var tWik = QueryWiktionaryAsync(reading, result);
            var tHvHtml = QueryThiVienReadingHtmlAsync(reading, result);
            tasks.Add(tWik);
            tasks.Add(tHvHtml);

            await Task.WhenAll(tasks.ToArray());

            // Process Han characters from Thi Vien
            for (int i = 0; i < hanCharLists.Count; i++)
            {
                var list = hanCharLists[i].Result;
                string queryUsed = readingVariants[i];
                if (list != null && list.Count > 0)
                {
                    result.SourcesConsulted.Add(string.Format("Thi Viện (Chữ Hán theo âm '{0}')", queryUsed));
                    foreach (var c in list)
                    {
                        if (!result.MatchingChars.Contains(c))
                        {
                            result.MatchingChars.Add(c);
                            int cp = char.ConvertToUtf32(c, 0);
                            result.Items.Add(new OnlineSearchResultItem
                            {
                                Character = c,
                                CodePoint = cp,
                                DataSource = "[Online API] Thi Viện (Hán-Việt)",
                                MatchedReading = queryUsed,
                                HanViet = queryUsed,
                                WebUrl = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(c),
                                WebCitation = "Thi Viện Hán Việt (hvdic.thivien.net)"
                            });
                        }
                    }
                }
            }

            // Process Nom characters from Thi Vien
            for (int i = 0; i < nomCharLists.Count; i++)
            {
                var list = nomCharLists[i].Result;
                string queryUsed = readingVariants[i];
                if (list != null && list.Count > 0)
                {
                    result.SourcesConsulted.Add(string.Format("Thi Viện (Chữ Nôm theo âm '{0}')", queryUsed));
                    foreach (var c in list)
                    {
                        if (string.IsNullOrEmpty(c)) continue;
                        int cp = char.ConvertToUtf32(c, 0);

                        // LINGUISTIC RULE: Chữ Nôm thuần túy xuất hiện từ Plane 2 (Ext B..F: U+20000+) hoặc vùng PUA.
                        // Các chữ trong BMP (U+4E00 - U+9FFF) là chữ Hán kinh điển mượn đọc âm Việt, KHÔNG phải Chữ Nôm thuần túy!
                        bool isPlane2OrPua = (cp >= 0x20000 || (cp >= 0xE000 && cp <= 0xF8FF));

                        if (!isPlane2OrPua)
                        {
                            // Treat as Chinese character (Chữ Hán) read with Sino-Vietnamese sound!
                            if (!result.MatchingChars.Contains(c))
                                result.MatchingChars.Add(c);

                            var existing = result.Items.Find(item => item.CodePoint == cp);
                            if (existing != null)
                            {
                                if (string.IsNullOrEmpty(existing.HanViet))
                                    existing.HanViet = queryUsed;
                            }
                            else
                            {
                                result.Items.Add(new OnlineSearchResultItem
                                {
                                    Character = c,
                                    CodePoint = cp,
                                    DataSource = "[Online API] Thi Viện (Hán-Việt)",
                                    MatchedReading = queryUsed,
                                    HanViet = queryUsed,
                                    Nom = "",
                                    WebUrl = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(c),
                                    WebCitation = "Thi Viện Hán Việt (hvdic.thivien.net)"
                                });
                            }
                        }
                        else
                        {
                            // True Chữ Nôm in Extension B..F or PUA!
                            if (!result.MatchingNomChars.Contains(c))
                                result.MatchingNomChars.Add(c);
                            if (!result.MatchingChars.Contains(c))
                                result.MatchingChars.Add(c);

                            var existing = result.Items.Find(item => item.CodePoint == cp);
                            if (existing != null)
                            {
                                existing.Nom = queryUsed;
                                existing.DataSource = "[Online API] Thi Viện (Chữ Nôm)";
                            }
                            else
                            {
                                result.Items.Add(new OnlineSearchResultItem
                                {
                                    Character = c,
                                    CodePoint = cp,
                                    DataSource = "[Online API] Thi Viện (Chữ Nôm)",
                                    MatchedReading = queryUsed,
                                    Nom = queryUsed,
                                    WebUrl = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(c),
                                    WebCitation = "Thi Viện Chữ Nôm (hvdic.thivien.net)"
                                });
                            }
                        }
                    }
                }
            }

            // If Wiktionary has definition for word/phrase, add word entry
            if (result.Definitions.Count > 0 && result.Items.Count == 0)
            {
                var wordItem = new OnlineSearchResultItem
                {
                    Character = reading,
                    CodePoint = 0,
                    DataSource = "[Online API] Wiktionary & Thi Viện",
                    MatchedReading = reading,
                    WebUrl = "https://vi.wiktionary.org/wiki/" + Uri.EscapeDataString(reading),
                    WebCitation = "Wiktionary Tiếng Việt & Thi Viện",
                    Definitions = new List<DictionaryDefinition>(result.Definitions)
                };
                result.Items.Add(wordItem);
            }
        }
        #endregion

        #region Thi Vien JSON API helpers
        private async Task<List<string>> QueryThiVienTranscriptAsync(string input, int lang)
        {
            var readings = new List<string>();
            try
            {
                string url = "https://hvdic.thivien.net/transcript-query.json.php";
                string postData = string.Format("mode=trans&lang={0}&input={1}", lang, Uri.EscapeDataString(input));
                string json = await PostAsync(url, postData, 2500);
                if (string.IsNullOrEmpty(json)) return readings;

                var root = Json.Deserialize<Dictionary<string, object>>(json);
                if (root != null && root.ContainsKey("result"))
                {
                    var resList = root["result"] as ArrayList;
                    if (resList != null && resList.Count > 0)
                    {
                        var first = resList[0] as Dictionary<string, object>;
                        if (first != null && first.ContainsKey("o"))
                        {
                            var o = first["o"];
                            if (o is ArrayList)
                            {
                                foreach (var item in (ArrayList)o)
                                    if (item != null) readings.Add(item.ToString().Trim());
                            }
                            else if (o != null)
                            {
                                readings.Add(o.ToString().Trim());
                            }
                        }
                    }
                }
            }
            catch { }
            return readings;
        }

        private async Task<List<string>> QueryThiVienHanByReadingAsync(string reading, int lang)
        {
            var characters = new List<string>();
            try
            {
                string url = "https://hvdic.thivien.net/transcript-query.json.php";
                string postData = string.Format("mode=han&lang={0}&input={1}", lang, Uri.EscapeDataString(reading));
                string json = await PostAsync(url, postData, 2500);
                if (string.IsNullOrEmpty(json)) return characters;

                var root = Json.Deserialize<Dictionary<string, object>>(json);
                if (root != null && root.ContainsKey("result"))
                {
                    var resList = root["result"] as ArrayList;
                    if (resList != null && resList.Count > 0)
                    {
                        var first = resList[0] as Dictionary<string, object>;
                        if (first != null && first.ContainsKey("o"))
                        {
                            var oObj = first["o"];
                            if (oObj is Dictionary<string, object>)
                            {
                                var oDict = (Dictionary<string, object>)oObj;
                                ArrayList list = null;
                                if (oDict.ContainsKey("by_views") && oDict["by_views"] is ArrayList)
                                    list = (ArrayList)oDict["by_views"];
                                else if (oDict.ContainsKey("by_strokes") && oDict["by_strokes"] is ArrayList)
                                    list = (ArrayList)oDict["by_strokes"];
                                else if (oDict.ContainsKey("by_radical") && oDict["by_radical"] is ArrayList)
                                    list = (ArrayList)oDict["by_radical"];

                                if (list != null)
                                {
                                    foreach (var c in list)
                                        if (c != null && !characters.Contains(c.ToString()))
                                            characters.Add(c.ToString());
                                }
                            }
                            else if (oObj is ArrayList)
                            {
                                foreach (var c in (ArrayList)oObj)
                                    if (c != null && !characters.Contains(c.ToString()))
                                        characters.Add(c.ToString());
                            }
                        }
                    }
                }
            }
            catch { }
            return characters;
        }
        #endregion

        #region Thi Vien HTML Parser
        private async Task QueryThiVienHtmlAsync(string character, DictionaryLookupResult result)
        {
            try
            {
                string url = "https://hvdic.thivien.net/whv/" + Uri.EscapeDataString(character);
                string html = await GetAsync(url, 2500);
                if (string.IsNullOrEmpty(html)) return;

                result.SourcesConsulted.Add("Thi Viện Hán Nôm");

                var mStrokes = Regex.Match(html, @"Tổng nét:\s*(\d+)", RegexOptions.IgnoreCase);
                if (mStrokes.Success)
                {
                    int s;
                    if (int.TryParse(mStrokes.Groups[1].Value, out s)) result.StrokeCount = s;
                }

                var mRad = Regex.Match(html, @"Bộ:\s*<a[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase);
                if (mRad.Success)
                {
                    result.Radical = mRad.Groups[1].Value.Trim();
                }

                string pattern = @"<p[^>]*class=""hvres-source""[^>]*>(.*?)</p>\s*<div[^>]*class=""hvres-meaning[^>]*>(.*?)</div>";
                var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);
                foreach (Match m in matches)
                {
                    string src = StripTags(m.Groups[1].Value);
                    string text = StripTags(m.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(src) && !string.IsNullOrWhiteSpace(text))
                    {
                        if (src.IndexOf("Âm đọc khác", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var nomMatch = Regex.Match(text, @"Âm Nôm:\s*([^Â\n\r]+)", RegexOptions.IgnoreCase);
                            if (nomMatch.Success)
                            {
                                var parts = nomMatch.Groups[1].Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var p in parts)
                                {
                                    string nom = p.Trim().ToLowerInvariant();
                                    if (!string.IsNullOrEmpty(nom) && !result.NomReadings.Contains(nom))
                                        result.NomReadings.Add(nom);
                                }
                            }
                        }

                        if (src.IndexOf("Thiều Chửu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("Trần Văn Chánh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("Nguyễn Quốc Hùng", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("phổ thông", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("trích dẫn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("Từ ghép", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Definitions.Add(new DictionaryDefinition(src, text));
                        }
                    }
                }
            }
            catch { }
        }

        private async Task QueryThiVienReadingHtmlAsync(string reading, DictionaryLookupResult result)
        {
            try
            {
                string url = "https://hvdic.thivien.net/hv/" + Uri.EscapeDataString(reading);
                string html = await GetAsync(url, 2500);
                if (string.IsNullOrEmpty(html)) return;

                string pattern = @"<p[^>]*class=""hvres-source""[^>]*>(.*?)</p>\s*<div[^>]*class=""hvres-meaning[^>]*>(.*?)</div>";
                var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);
                int count = 0;
                foreach (Match m in matches)
                {
                    string src = StripTags(m.Groups[1].Value);
                    string text = StripTags(m.Groups[2].Value);
                    if (!string.IsNullOrWhiteSpace(src) && !string.IsNullOrWhiteSpace(text))
                    {
                        if (src.IndexOf("Thiều Chửu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("Trần Văn Chánh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("Nguyễn Quốc Hùng", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            src.IndexOf("phổ thông", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Definitions.Add(new DictionaryDefinition("Thi Viện (" + src + ")", text));
                            count++;
                            if (count >= 5) break;
                        }
                    }
                }
            }
            catch { }
        }
        #endregion

        #region Wiktionary Parser
        private async Task QueryWiktionaryAsync(string title, DictionaryLookupResult result)
        {
            try
            {
                string url = "https://vi.wiktionary.org/w/api.php?action=query&format=json&prop=extracts&explaintext=1&origin=*&titles=" + Uri.EscapeDataString(title);
                string json = await GetAsync(url, 2500);
                if (string.IsNullOrEmpty(json)) return;

                var dict = Json.Deserialize<Dictionary<string, object>>(json);
                if (dict != null && dict.ContainsKey("query"))
                {
                    var qObj = dict["query"] as Dictionary<string, object>;
                    if (qObj != null && qObj.ContainsKey("pages"))
                    {
                        var pages = qObj["pages"] as Dictionary<string, object>;
                        if (pages != null)
                        {
                            foreach (var page in pages.Values)
                            {
                                var p = page as Dictionary<string, object>;
                                if (p != null && p.ContainsKey("extract") && p["extract"] != null)
                                {
                                    string extract = p["extract"].ToString().Trim();
                                    if (!string.IsNullOrWhiteSpace(extract))
                                    {
                                        result.SourcesConsulted.Add("Wiktionary Tiếng Việt");
                                        ParseWiktionaryExtract(extract, result);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void ParseWiktionaryExtract(string extract, DictionaryLookupResult result)
        {
            var mHv = Regex.Match(extract, @"Phiên âm Hán[–-]Việt:\s*([^\n\r]+)", RegexOptions.IgnoreCase);
            if (mHv.Success)
            {
                var parts = mHv.Groups[1].Value.Split(new[] { ',', ';', '、', '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var s in parts)
                {
                    string hv = s.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(hv) && !result.HanVietReadings.Contains(hv))
                        result.HanVietReadings.Add(hv);
                }
            }

            string[] sections = extract.Split(new[] { "\n=== " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sec in sections)
            {
                var mHeader = Regex.Match(sec, @"^([^=\n]+)\s*===([\s\S]*)$");
                if (mHeader.Success)
                {
                    string header = mHeader.Groups[1].Value.Trim();
                    string content = mHeader.Groups[2].Value.Trim();
                    if (header == "Danh từ" || header == "Động từ" || header == "Tính từ" ||
                        header == "Phó từ" || header == "Nghĩa" || header == "Giải nghĩa")
                    {
                        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var filtered = new List<string>();
                        foreach (var l in lines)
                        {
                            string trimmed = l.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("=="))
                                filtered.Add(trimmed);
                        }
                        if (filtered.Count > 0)
                        {
                            result.Definitions.Add(new DictionaryDefinition("Wiktionary (" + header + ")", string.Join(Environment.NewLine, filtered.ToArray())));
                        }
                    }
                }
            }

            if (result.Definitions.Count == 0 && extract.Length > 0)
            {
                string text = extract.Length > 800 ? extract.Substring(0, 800) + "..." : extract;
                result.Definitions.Add(new DictionaryDefinition("Wiktionary Tiếng Việt", text));
            }
        }
        #endregion

        #region HTTP Client Utilities
        private static async Task<string> GetAsync(string url, int timeoutMs = 2500)
        {
            try
            {
                using (var cts = new CancellationTokenSource(timeoutMs))
                using (var response = await HttpClientInstance.GetAsync(url, cts.Token))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<string> PostAsync(string url, string postData, int timeoutMs = 2500)
        {
            try
            {
                using (var cts = new CancellationTokenSource(timeoutMs))
                using (var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded"))
                using (var response = await HttpClientInstance.PostAsync(url, content, cts.Token))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch { }
            return null;
        }

        private static string StripTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string s = Regex.Replace(input, @"<br\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</p>", Environment.NewLine, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", "");
            return WebUtility.HtmlDecode(s).Trim();
        }
        #endregion

        #region Local Heuristics
        private void ApplyLocalHeuristics(string query, DictionaryLookupResult result)
        {
            var builtin = new Dictionary<string, string[]>
            {
                { "天", new[] { "thiên", "tiān", "trời", "37.1", "Trời, bầu trời; ngày; tự nhiên" } },
                { "人", new[] { "nhân", "rén", "người", "9.0", "Con người, nhân loại" } },
                { "水", new[] { "thủy", "shuǐ", "nước", "85.0", "Nước; chất lỏng" } },
                { "山", new[] { "sơn", "shān", "núi", "46.0", "Núi non; vùng cao" } },
                { "日", new[] { "nhật", "rì", "ngày, mặt trời", "72.0", "Mặt trời; ngày; ban ngày" } },
                { "月", new[] { "nguyệt", "yuè", "tháng, mặt trăng", "74.0", "Mặt trăng; tháng trong năm" } },
                { "木", new[] { "mộc", "mù", "cây, gỗ", "75.0", "Cây cối; đồ gỗ; mộc mạc" } },
                { "火", new[] { "hỏa", "huǒ", "lửa", "86.0", "Ngọn lửa; tính nóng; hỏa hoạn" } },
                { "土", new[] { "thổ", "tǔ", "đất", "32.0", "Đất đai; lãnh thổ; màu vàng đất" } },
                { "金", new[] { "kim", "jīn", "vàng, kim loại", "167.0", "Kim loại quý; vàng; tiền bạc" } },
                { "心", new[] { "tâm", "xīn", "lòng, tim", "61.0", "Trái tim; tâm trí; tấm lòng" } },
                { "國", new[] { "quốc", "guó", "nước", "31.8", "Quốc gia, đất nước" } },
                { "国", new[] { "quốc", "guó", "nước", "31.5", "Quốc gia, đất nước (giản thể)" } },
                { "字", new[] { "tự", "zì", "chữ", "39.3", "Văn tự, chữ viết; tên tự" } },
                { "𡗶", new[] { "trời", "tiān", "trời", "37.5", "Chữ Nôm: Trời đất; ông trời" } },
                { "喃", new[] { "nôm, nam", "nán", "nôm", "30.9", "Chữ Nôm; tiếng nói bản địa Việt Nam" } }
            };

            if (builtin.ContainsKey(query))
            {
                var row = builtin[query];
                if (!result.HanVietReadings.Contains(row[0])) result.HanVietReadings.Add(row[0]);
                if (!result.PinyinReadings.Contains(row[1])) result.PinyinReadings.Add(row[1]);
                if (!result.NomReadings.Contains(row[2])) result.NomReadings.Add(row[2]);
                result.Radical = row[3];
                if (result.Definitions.Count == 0)
                {
                    bool isVi = LocalizationManager.CurrentLanguage == SupportedLanguage.Vietnamese;
                    result.Definitions.Add(new DictionaryDefinition(isVi ? "Kho dữ liệu cơ sở" : "Core Vocabulary DB", row[4]));
                }
            }
        }
        #endregion
    }
}
