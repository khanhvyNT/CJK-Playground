using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class UnihanDatabase
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private readonly object _syncLock = new object();

        private readonly Dictionary<int, UnihanRecord> _recordsByCp = new Dictionary<int, UnihanRecord>();

        // Inverted indexes
        private readonly Dictionary<string, List<int>> _indexVnExact = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> _indexVnToneless = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<int>> _indexNomExact = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> _indexNomToneless = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<int>> _indexMandarinExact = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> _indexMandarinToneless = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<int>> _indexCantonese = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<int>> _indexJapaneseKana = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> _indexJapaneseRomaji = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<int>> _indexKoreanHangul = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> _indexKoreanRomaja = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<int>> _indexSemantic = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        public int TotalRecords
        {
            get { return _recordsByCp.Count; }
        }

        public event Action DatabaseLoaded;
        public bool IsFullDatabaseLoaded { get; private set; }

        public UnihanDatabase()
        {
            LoadSeedData();
            Task.Factory.StartNew(() =>
            {
                try
                {
                    TryLoadJsonDatabase();
                    IsFullDatabaseLoaded = true;
                    if (DatabaseLoaded != null) DatabaseLoaded();
                }
                catch { }
            });
        }

        public UnihanRecord GetByCodePoint(int cp)
        {
            lock (_syncLock)
            {
                UnihanRecord rec;
                if (_recordsByCp.TryGetValue(cp, out rec))
                    return rec;
                return null;
            }
        }

        private void TryLoadJsonDatabase()
        {
            // 1. First check embedded manifest resource (Single-File Portable)
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string[] resNames = assembly.GetManifestResourceNames();
                foreach (var name in resNames)
                {
                    if (name.EndsWith("unihan-full.json", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = assembly.GetManifestResourceStream(name))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string json = reader.ReadToEnd();
                            LoadFromJsonString(json);
                            return;
                        }
                    }
                }
            }
            catch { }

            // 2. Check local disk candidate paths
            string[] candidatePaths = new[]
            {
                "data/unihan-full.json",
                "../data/unihan-full.json",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "unihan-full.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unihan-full.json")
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        LoadFromJsonFile(path);
                        break;
                    }
                    catch
                    {
                        // Continue to next path
                    }
                }
            }
        }

        public void LoadFromJsonFile(string filePath)
        {
            string jsonText = File.ReadAllText(filePath, Encoding.UTF8);
            LoadFromJsonString(jsonText);
        }

        public void LoadFromJsonString(string jsonText)
        {
            Json.MaxJsonLength = int.MaxValue;
            var list = Json.Deserialize<List<Dictionary<string, object>>>(jsonText);

            if (list == null) return;

            lock (_syncLock)
            {
                foreach (var item in list)
                {
                    if (!item.ContainsKey("cp")) continue;
                    int cp = Convert.ToInt32(item["cp"]);

                    UnihanRecord record;
                    if (!_recordsByCp.TryGetValue(cp, out record))
                    {
                        record = new UnihanRecord(cp);
                        _recordsByCp[cp] = record;
                    }

                    if (item.ContainsKey("v") && item["v"] is System.Collections.ArrayList)
                    {
                        foreach (var v in (System.Collections.ArrayList)item["v"])
                            if (v != null && !record.Vietnamese.Contains(v.ToString()))
                                record.Vietnamese.Add(v.ToString());
                    }

                    if (item.ContainsKey("nom") && item["nom"] is System.Collections.ArrayList)
                    {
                        foreach (var n in (System.Collections.ArrayList)item["nom"])
                            if (n != null && !record.Nom.Contains(n.ToString()))
                                record.Nom.Add(n.ToString());
                    }

                    if (item.ContainsKey("m") && item["m"] is System.Collections.ArrayList)
                    {
                        foreach (var m in (System.Collections.ArrayList)item["m"])
                            if (m != null && !record.Mandarin.Contains(m.ToString()))
                                record.Mandarin.Add(m.ToString());
                    }

                    if (item.ContainsKey("ca") && item["ca"] is System.Collections.ArrayList)
                    {
                        foreach (var ca in (System.Collections.ArrayList)item["ca"])
                            if (ca != null && !record.Cantonese.Contains(ca.ToString()))
                                record.Cantonese.Add(ca.ToString());
                    }

                    if (item.ContainsKey("j") && item["j"] is System.Collections.ArrayList)
                    {
                        foreach (var j in (System.Collections.ArrayList)item["j"])
                            if (j != null && !record.Japanese.Contains(j.ToString()))
                                record.Japanese.Add(j.ToString());
                    }

                    if (item.ContainsKey("k") && item["k"] is System.Collections.ArrayList)
                    {
                        foreach (var k in (System.Collections.ArrayList)item["k"])
                            if (k != null && !record.Korean.Contains(k.ToString()))
                                record.Korean.Add(k.ToString());
                    }

                    if (item.ContainsKey("d") && item["d"] != null && string.IsNullOrEmpty(record.Definition))
                        record.Definition = item["d"].ToString();

                    if (item.ContainsKey("vd") && item["vd"] != null && string.IsNullOrEmpty(record.ViDefinition))
                        record.ViDefinition = item["vd"].ToString();

                    if (item.ContainsKey("r") && item["r"] != null)
                        record.Radical = item["r"].ToString();

                    if (item.ContainsKey("st") && item["st"] != null)
                        record.TotalStrokes = Convert.ToInt32(item["st"]);

                    IndexRecord(record);
                }
            }
        }

        public void RegisterRecord(UnihanRecord record)
        {
            if (record == null) return;
            lock (_syncLock)
            {
                _recordsByCp[record.CodePoint] = record;
                IndexRecord(record);
            }
        }

        private void IndexRecord(UnihanRecord record)
        {
            int cp = record.CodePoint;

            // Index Vietnamese
            foreach (var v in record.Vietnamese)
            {
                string norm = LinguisticsUtils.NormalizeVietnameseTonePlacement(v);
                AddToIndex(_indexVnExact, norm, cp);
                string toneless = LinguisticsUtils.StripVietnameseDiacritics(norm);
                AddToIndex(_indexVnToneless, toneless, cp);
            }

            // Index Nom
            foreach (var n in record.Nom)
            {
                string norm = LinguisticsUtils.NormalizeVietnameseTonePlacement(n);
                AddToIndex(_indexNomExact, norm, cp);
                string toneless = LinguisticsUtils.StripVietnameseDiacritics(norm);
                AddToIndex(_indexNomToneless, toneless, cp);
            }

            // Index Mandarin & Pinyin
            foreach (var m in record.Mandarin)
            {
                AddToIndex(_indexMandarinExact, m, cp);
                string toneless = LinguisticsUtils.StripPinyinTones(m);
                AddToIndex(_indexMandarinToneless, toneless, cp);
            }

            // Index Cantonese
            foreach (var ca in record.Cantonese)
            {
                AddToIndex(_indexCantonese, ca, cp);
                string toneless = LinguisticsUtils.StripCantoneseTones(ca);
                AddToIndex(_indexCantonese, toneless, cp);
            }

            // Index Japanese
            foreach (var j in record.Japanese)
            {
                AddToIndex(_indexJapaneseKana, j, cp);
                string romaji = LinguisticsUtils.KanaToRomaji(j);
                AddToIndex(_indexJapaneseRomaji, romaji, cp);
            }

            // Index Korean
            foreach (var k in record.Korean)
            {
                AddToIndex(_indexKoreanHangul, k, cp);
                string romaja = LinguisticsUtils.HangulToRomaja(k);
                if (!record.KoreanRomaja.Contains(romaja)) record.KoreanRomaja.Add(romaja);
                AddToIndex(_indexKoreanRomaja, romaja, cp);
            }

            // Index Semantic Definition keywords
            string fullDef = (record.Definition + " " + record.ViDefinition).ToLowerInvariant();
            var words = Regex.Split(fullDef, @"[^\p{L}\p{N}]+");
            foreach (var w in words)
            {
                if (w.Length >= 2)
                {
                    AddToIndex(_indexSemantic, w, cp);
                    string toneless = LinguisticsUtils.StripVietnameseDiacritics(w);
                    if (toneless != w) AddToIndex(_indexSemantic, toneless, cp);
                }
            }
        }

        private static void AddToIndex(Dictionary<string, List<int>> index, string key, int cp)
        {
            if (string.IsNullOrEmpty(key)) return;
            key = key.Trim().ToLowerInvariant();
            List<int> list;
            if (!index.TryGetValue(key, out list))
            {
                list = new List<int>();
                index[key] = list;
            }
            if (!list.Contains(cp))
            {
                list.Add(cp);
            }
        }

        public List<PhoneticMatchResult> SearchByPronunciation(string query, SearchFilterOptions options = null)
        {
            if (options == null) options = new SearchFilterOptions();
            var results = new List<PhoneticMatchResult>();
            var seenCp = new HashSet<int>();

            string raw = (query ?? "").Trim();

            lock (_syncLock)
            {
                // If empty and PureNomOnly is requested, return all pure Nôm characters
            if (string.IsNullOrEmpty(raw))
            {
                if (options.PureNomOnly)
                {
                    foreach (var kvp in _recordsByCp)
                    {
                        if (kvp.Value.IsPureNom)
                        {
                            results.Add(new PhoneticMatchResult
                            {
                                Record = kvp.Value,
                                MatchedReading = kvp.Value.Nom.Count > 0 ? kvp.Value.Nom[0] : (kvp.Value.Vietnamese.Count > 0 ? kvp.Value.Vietnamese[0] : ""),
                                MatchLanguage = "nom",
                                MatchType = "exact",
                                Score = 100
                            });
                        }
                    }
                    results.Sort((a, b) => a.Record.TotalStrokes.CompareTo(b.Record.TotalStrokes));
                    if (options.Limit > 0 && results.Count > options.Limit)
                        return results.GetRange(0, options.Limit);
                    return results;
                }
                return results;
            }

            var parsed = LinguisticsUtils.ParseSearchQuery(raw);
            string scope = parsed.Scope != "all" ? parsed.Scope : options.TargetLanguage;
            string cleanQ = parsed.CleanQuery.ToLowerInvariant();
            string qVnNorm = LinguisticsUtils.NormalizeVietnameseTonePlacement(cleanQ);
            string qVnToneless = LinguisticsUtils.StripVietnameseDiacritics(qVnNorm);
            string qPinyinToneless = LinguisticsUtils.StripPinyinTones(cleanQ);
            string qKanaRomaji = LinguisticsUtils.KanaToRomaji(cleanQ);

            // 1. Check direct codepoint query (U+XXXX, 0xXXXX, or single char)
            int directCp = -1;
            if (Regex.IsMatch(cleanQ, @"^(u\+|0x)[0-9a-f]{4,6}$", RegexOptions.IgnoreCase))
            {
                string hex = cleanQ.Replace("u+", "").Replace("0x", "");
                int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out directCp);
            }
            else if (cleanQ.Length == 1 || (cleanQ.Length == 2 && char.IsSurrogatePair(cleanQ, 0)))
            {
                int cp = char.ConvertToUtf32(cleanQ, 0);
                if (cp >= 0x3400) directCp = cp;
            }

            if (directCp > 0 && _recordsByCp.ContainsKey(directCp))
            {
                var r = _recordsByCp[directCp];
                if (PassesFilters(r, options))
                {
                    results.Add(new PhoneticMatchResult
                    {
                        Record = r,
                        MatchedReading = r.HexCode,
                        MatchLanguage = "codepoint",
                        MatchType = "exact",
                        Score = 150
                    });
                    return results;
                }
            }

            if (scope == "ids")
            {
                string idsQuery = !string.IsNullOrEmpty(parsed.CleanQuery) ? parsed.CleanQuery : cleanQ;
                var idMatchedCps = HanIdsCorpusIndex.SearchCodePointsByIds(idsQuery, options.Limit > 0 ? options.Limit : 150);
                foreach (int cp in idMatchedCps)
                {
                    UnihanRecord r;
                    if (!_recordsByCp.TryGetValue(cp, out r))
                    {
                        r = new UnihanRecord(cp);
                    }
                    if (PassesFilters(r, options))
                    {
                        results.Add(new PhoneticMatchResult
                        {
                            Record = r,
                            MatchedReading = HanIdsCorpusIndex.GetExpandedIdsForCodePoint(cp) ?? idsQuery,
                            MatchLanguage = "ids",
                            MatchType = "exact",
                            Score = 140
                        });
                    }
                }
                return results;
            }

            // Helper to collect matches from an index
            Action<Dictionary<string, List<int>>, string, string, string, int> collectMatches = (index, searchKey, lang, matchType, score) =>
            {
                if (string.IsNullOrEmpty(searchKey)) return;

                // Exact key lookup
                List<int> exactList;
                if (index.TryGetValue(searchKey, out exactList))
                {
                    foreach (int cp in exactList)
                    {
                        if (seenCp.Add(cp))
                        {
                            var rec = _recordsByCp[cp];
                            if (PassesFilters(rec, options))
                            {
                                results.Add(new PhoneticMatchResult
                                {
                                    Record = rec,
                                    MatchedReading = searchKey,
                                    MatchLanguage = lang,
                                    MatchType = matchType,
                                    Score = score
                                });
                            }
                        }
                    }
                }

                // Prefix lookup (unless MatchExact is true)
                if (!options.MatchExact && searchKey.Length >= 2)
                {
                    foreach (var kvp in index)
                    {
                        if (kvp.Key.StartsWith(searchKey, StringComparison.OrdinalIgnoreCase) && !kvp.Key.Equals(searchKey, StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (int cp in kvp.Value)
                            {
                                if (seenCp.Add(cp))
                                {
                                    var rec = _recordsByCp[cp];
                                    if (PassesFilters(rec, options))
                                    {
                                        results.Add(new PhoneticMatchResult
                                        {
                                            Record = rec,
                                            MatchedReading = kvp.Key,
                                            MatchLanguage = lang,
                                            MatchType = "prefix",
                                            Score = score - 20
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            };

            // 2. Execute scope-aware matching
            bool checkAll = scope == "all";

            bool anyCol = options.MatchColHanViet || options.MatchColNom || options.MatchColPinyin || options.MatchColMeaning;
            bool searchHv = !anyCol || options.MatchColHanViet;
            bool searchNom = !anyCol || options.MatchColNom;
            bool searchPy = !anyCol || options.MatchColPinyin;
            bool searchDef = (anyCol && options.MatchColMeaning);

            if ((checkAll || scope == "vietnamese" || scope == "hanviet") && searchHv)
            {
                collectMatches(_indexVnExact, qVnNorm, "vietnamese", "exact", 100);
                if (!options.MatchExactTone)
                    collectMatches(_indexVnToneless, qVnToneless, "vietnamese", "diacritic_insensitive", 80);
            }

            if ((checkAll || scope == "vietnamese" || scope == "nom") && searchNom)
            {
                collectMatches(_indexNomExact, qVnNorm, "nom", "exact", 105);
                if (!options.MatchExactTone)
                    collectMatches(_indexNomToneless, qVnToneless, "nom", "diacritic_insensitive", 85);
            }

            if ((checkAll || scope == "mandarin") && searchPy)
            {
                collectMatches(_indexMandarinExact, cleanQ, "mandarin", "exact", 95);
                if (!options.MatchExactTone)
                    collectMatches(_indexMandarinToneless, qPinyinToneless, "mandarin", "diacritic_insensitive", 75);
            }

            if (checkAll || scope == "cantonese")
            {
                collectMatches(_indexCantonese, cleanQ, "cantonese", "exact", 90);
            }

            if (checkAll || scope == "japanese")
            {
                collectMatches(_indexJapaneseKana, cleanQ, "japanese", "exact", 90);
                collectMatches(_indexJapaneseRomaji, qKanaRomaji, "japanese", "romaji", 80);
            }

            if (checkAll || scope == "korean")
            {
                collectMatches(_indexKoreanHangul, cleanQ, "korean", "exact", 90);
                collectMatches(_indexKoreanRomaja, cleanQ, "korean", "romaja", 85);
            }

            if (scope == "semantic" || searchDef)
            {
                collectMatches(_indexSemantic, cleanQ, "semantic", "keyword", 60);
                if (!options.MatchExactTone)
                    collectMatches(_indexSemantic, qVnToneless, "semantic", "keyword_toneless", 50);
            }
            else if (checkAll && cleanQ.Length >= 3)
            {
                // In "all" mode, only exact semantic keyword match (do not spam 2-letter prefixes like 'ly' -> 'lynx')
                List<int> exactSem;
                if (_indexSemantic.TryGetValue(cleanQ, out exactSem))
                {
                    foreach (int cp in exactSem)
                    {
                        if (seenCp.Add(cp))
                        {
                            var rec = _recordsByCp[cp];
                            if (PassesFilters(rec, options))
                            {
                                results.Add(new PhoneticMatchResult
                                {
                                    Record = rec,
                                    MatchedReading = cleanQ,
                                    MatchLanguage = "semantic",
                                    MatchType = "exact",
                                    Score = 60
                                });
                            }
                        }
                    }
                }
            }

            // Sort: Score descending, then TotalStrokes ascending
            results.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0) return cmp;
                return a.Record.TotalStrokes.CompareTo(b.Record.TotalStrokes);
            });

            if (options.Limit > 0 && results.Count > options.Limit)
                return results.GetRange(0, options.Limit);

            return results;
            }
        }

        private static bool PassesFilters(UnihanRecord record, SearchFilterOptions options)
        {
            if (options.PureNomOnly && !record.IsPureNom)
                return false;

            if (options.StrokeFilter.HasValue && record.TotalStrokes != options.StrokeFilter.Value)
                return false;

            if (!string.IsNullOrEmpty(options.RadicalFilter) && record.Radical != options.RadicalFilter)
                return false;

            return true;
        }

        private void LoadSeedData()
        {
            // Core standard entries for testing and immediate availability
            RegisterRecord(new UnihanRecord(0x5929)
            {
                Vietnamese = new List<string> { "thiên" },
                Nom = new List<string> { "trời" },
                Mandarin = new List<string> { "tiān", "tian1" },
                Cantonese = new List<string> { "tin1" },
                Japanese = new List<string> { "テン", "あめ", "あま" },
                Korean = new List<string> { "천" },
                TotalStrokes = 4,
                Radical = "大",
                Definition = "sky, heaven; god, celestial",
                ViDefinition = "Trời, bầu trời; tự nhiên, thần trời"
            });

            RegisterRecord(new UnihanRecord(0x5343)
            {
                Vietnamese = new List<string> { "thiên" },
                Nom = new List<string> { "nghìn" },
                Mandarin = new List<string> { "qiān", "qian1" },
                Cantonese = new List<string> { "cin1" },
                Japanese = new List<string> { "セン", "ち" },
                Korean = new List<string> { "천" },
                TotalStrokes = 3,
                Radical = "十",
                Definition = "thousand; many, numerous",
                ViDefinition = "Số một nghìn (1000); số đếm; nhiều"
            });

            RegisterRecord(new UnihanRecord(0x9077)
            {
                Vietnamese = new List<string> { "遷", "thiên" },
                Mandarin = new List<string> { "qiān", "qian1" },
                Japanese = new List<string> { "セン" },
                Korean = new List<string> { "천" },
                TotalStrokes = 15,
                Radical = "辵",
                Definition = "move, shift, change; transfer",
                ViDefinition = "Dời đi, dời đổi; biến thiên"
            });

            RegisterRecord(new UnihanRecord(0x215F6) // 𡗶 (Nom "trời")
            {
                Vietnamese = new List<string> { "trời" },
                Nom = new List<string> { "trời" },
                TotalStrokes = 9,
                Radical = "天",
                Definition = "Vietnamese Chu Nom: sky, heaven, God",
                ViDefinition = "Chữ Nôm: Trời, ông trời"
            });

            RegisterRecord(new UnihanRecord(0x4EBA) // 人
            {
                Vietnamese = new List<string> { "nhân" },
                Nom = new List<string> { "người" },
                Mandarin = new List<string> { "rén", "ren2" },
                Cantonese = new List<string> { "jan4" },
                Japanese = new List<string> { "ジン", "ニン", "ひと" },
                Korean = new List<string> { "인" },
                TotalStrokes = 2,
                Radical = "人",
                Definition = "man; people; mankind; someone else",
                ViDefinition = "Con người, nhân loại, người ta"
            });

            RegisterRecord(new UnihanRecord(0x65E5) // 日
            {
                Vietnamese = new List<string> { "nhật" },
                Nom = new List<string> { "ngày" },
                Mandarin = new List<string> { "rì", "ri4" },
                Cantonese = new List<string> { "jat6" },
                Japanese = new List<string> { "ニチ", "ジツ", "ひ", "か" },
                Korean = new List<string> { "일" },
                TotalStrokes = 4,
                Radical = "日",
                Definition = "sun; day; daytime",
                ViDefinition = "Mặt trời; ngày; ban ngày"
            });

            RegisterRecord(new UnihanRecord(0x6708) // 月
            {
                Vietnamese = new List<string> { "nguyệt" },
                Nom = new List<string> { "tháng" },
                Mandarin = new List<string> { "yuè", "yue4" },
                Cantonese = new List<string> { "jyut6" },
                Japanese = new List<string> { "ゲツ", "ガツ", "つき" },
                Korean = new List<string> { "월" },
                TotalStrokes = 4,
                Radical = "月",
                Definition = "moon; month",
                ViDefinition = "Mặt trăng; tháng trong năm"
            });

            RegisterRecord(new UnihanRecord(0x6C34) // 水
            {
                Vietnamese = new List<string> { "thủy" },
                Nom = new List<string> { "nước" },
                Mandarin = new List<string> { "shuǐ", "shui3" },
                Cantonese = new List<string> { "seoi2" },
                Japanese = new List<string> { "スイ", "みず" },
                Korean = new List<string> { "수" },
                TotalStrokes = 4,
                Radical = "水",
                Definition = "water, liquid, lotion, juice",
                ViDefinition = "Nước; chất lỏng; sông ngòi"
            });

            RegisterRecord(new UnihanRecord(0x570B) // 國
            {
                Vietnamese = new List<string> { "quốc" },
                Nom = new List<string> { "nước" },
                Mandarin = new List<string> { "guó", "guo2" },
                Cantonese = new List<string> { "gwok3" },
                Japanese = new List<string> { "コク", "くに" },
                Korean = new List<string> { "국" },
                TotalStrokes = 11,
                Radical = "囗",
                Definition = "nation, country, nation-state",
                ViDefinition = "Quốc gia, đất nước"
            });
        }
    }
}
