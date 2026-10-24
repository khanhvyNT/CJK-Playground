using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public static class HvDicScript
    {
        public const byte Traditional = 1;
        public const byte Simplified = 2;
        public const byte Both = 3;
    }

    public class HvDicHanItem
    {
        public string Text { get; set; }
        public byte Script { get; set; }
        public int CodePoint { get; set; }

        public bool IsTraditional
        {
            get { return (Script & HvDicScript.Traditional) != 0; }
        }

        public bool IsSimplified
        {
            get { return (Script & HvDicScript.Simplified) != 0; }
        }

        public HvDicHanItem()
        {
            Text = "";
            Script = HvDicScript.Both;
        }

        public HvDicHanItem(string text, byte script, int codePoint = 0)
        {
            Text = text ?? "";
            Script = script;
            CodePoint = codePoint;
        }

        public string GetScriptLabel(bool isVi)
        {
            if (Script == HvDicScript.Traditional)
                return isVi ? "Phồn thể" : "Traditional";
            if (Script == HvDicScript.Simplified)
                return isVi ? "Giản thể" : "Simplified";
            return isVi ? "Cả hai (Phồn & Giản)" : "Both (Trad & Simp)";
        }
    }

    public class HvDicEntry
    {
        public string HanViet { get; set; }
        public string Pinyin { get; set; }
        public string Meaning { get; set; }
        public List<string> Definitions { get; set; }
        public List<HvDicHanItem> HanItems { get; set; }
        public List<string> AltHanViet { get; set; }

        public HvDicEntry()
        {
            HanViet = "";
            Pinyin = "";
            Meaning = "";
            Definitions = new List<string>();
            HanItems = new List<HvDicHanItem>();
            AltHanViet = new List<string>();
        }

        public void ParseDefinitions()
        {
            Definitions.Clear();
            if (string.IsNullOrEmpty(Meaning)) return;

            string[] rawLines = Meaning.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in rawLines)
            {
                string t = (line ?? "").Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    Definitions.Add(t);
                }
            }
        }
    }

    public class HvDicSearchResult
    {
        public HvDicEntry Entry { get; set; }
        public HvDicHanItem HanItem { get; set; }
        public string MatchedReading { get; set; }
        public string MatchSource { get; set; } // "character", "hv_exact", "hv_toneless", "pinyin_exact", "pinyin_toneless"
        public int Score { get; set; }
    }

    /// <summary>
    /// Bộ máy tra cứu Từ điển Hán-Việt Thi Viện Offline (HVDic.wrd)
    /// Đọc nhị phân theo đặc tả đảo ngược từ hvdic.h, không phụ thuộc API bên ngoài.
    /// </summary>
    public class HvDicDatabase
    {
        private readonly object _syncLock = new object();

        private readonly List<HvDicEntry> _allEntries = new List<HvDicEntry>();
        private readonly Dictionary<string, List<HvDicEntry>> _byChar = new Dictionary<string, List<HvDicEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<int, List<HvDicEntry>> _byCodePoint = new Dictionary<int, List<HvDicEntry>>();

        private readonly Dictionary<string, List<HvDicEntry>> _byHvExact = new Dictionary<string, List<HvDicEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<HvDicEntry>> _byHvToneless = new Dictionary<string, List<HvDicEntry>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<HvDicEntry>> _byPinyinExact = new Dictionary<string, List<HvDicEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<HvDicEntry>> _byPinyinToneless = new Dictionary<string, List<HvDicEntry>>(StringComparer.OrdinalIgnoreCase);

        public int TotalRecords
        {
            get
            {
                lock (_syncLock) { return _allEntries.Count; }
            }
        }

        public int TotalHanItems { get; private set; }
        public bool IsLoaded { get; private set; }
        public event Action DatabaseLoaded;

        public HvDicDatabase()
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    TryLoadDatabase();
                    IsLoaded = true;
                    if (DatabaseLoaded != null) DatabaseLoaded();
                }
                catch { }
            });
        }

        public void TryLoadDatabase()
        {
            // 1. Check embedded manifest resource (Single-File Portable)
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string[] resNames = assembly.GetManifestResourceNames();
                foreach (var name in resNames)
                {
                    if (name.EndsWith("HVDic.wrd", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = assembly.GetManifestResourceStream(name))
                        {
                            if (stream != null)
                            {
                                byte[] bytes = new byte[stream.Length];
                                stream.Read(bytes, 0, bytes.Length);
                                ParseFromBytes(bytes);
                                return;
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Check local disk candidate paths
            string[] candidatePaths = new[]
            {
                "data/HVDic.wrd",
                "../data/HVDic.wrd",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "HVDic.wrd"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HVDic.wrd"),
                @"c:\Users\Admin\Documents\Tool\HVDic223\HVDic.wrd"
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        LoadFromFile(path);
                        return;
                    }
                    catch
                    {
                        // Try next path
                    }
                }
            }
        }

        public void LoadFromFile(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            ParseFromBytes(bytes);
        }

        public void ParseFromBytes(byte[] data)
        {
            if (data == null || data.Length < 12) return;

            int pos = 0;
            uint ver = BitConverter.ToUInt32(data, pos); pos += 4;
            uint totalSlots = BitConverter.ToUInt32(data, pos); pos += 4;
            uint zero = BitConverter.ToUInt32(data, pos); pos += 4;

            var entries = new List<HvDicEntry>((int)Math.Min(totalSlots, 20000));
            int hanCount = 0;

            while (pos < data.Length)
            {
                if (pos >= data.Length) break;
                int hvLen = data[pos++];
                if (pos + hvLen > data.Length) break;
                string hv = Encoding.UTF8.GetString(data, pos, hvLen); pos += hvLen;

                if (pos >= data.Length) break;
                int pyLen = data[pos++];
                if (pos + pyLen > data.Length) break;
                string pinyin = Encoding.ASCII.GetString(data, pos, pyLen); pos += pyLen;

                if (pos >= data.Length) break;
                int ml = data[pos++];
                if (ml == 0xFF)
                {
                    if (pos + 2 > data.Length) break;
                    ml = BitConverter.ToUInt16(data, pos); pos += 2;
                }
                if (pos + ml > data.Length) break;
                string meaning = Encoding.UTF8.GetString(data, pos, ml); pos += ml;

                if (pos >= data.Length) break;
                int cnt = data[pos++];
                var hanList = new List<HvDicHanItem>();
                for (int i = 0; i < cnt; i++)
                {
                    if (pos + 4 > data.Length) break;
                    uint n = BitConverter.ToUInt32(data, pos); pos += 4;
                    if (pos + (int)n * 2 > data.Length) break;
                    string ch = Encoding.Unicode.GetString(data, pos, (int)n * 2); pos += (int)n * 2;
                    if (pos >= data.Length) break;
                    byte script = data[pos++];

                    int cp = 0;
                    if (!string.IsNullOrEmpty(ch))
                    {
                        cp = char.ConvertToUtf32(ch, 0);
                    }
                    hanList.Add(new HvDicHanItem(ch, script, cp));
                    hanCount++;
                }

                if (pos + 2 > data.Length) break;
                if (data[pos] != 0x30 || data[pos + 1] != 0x21) break;
                pos += 2;

                if (pos + 4 > data.Length) break;
                uint k = BitConverter.ToUInt32(data, pos); pos += 4;
                var altHvList = new List<string>();
                while (k != 0 && k < 0x100)
                {
                    if (pos + 4 > data.Length) break;
                    uint refSlot = BitConverter.ToUInt32(data, pos); pos += 4;
                    if (pos >= data.Length) break;
                    int altLen = data[pos++];
                    if (pos + altLen > data.Length) break;
                    string altHv = Encoding.UTF8.GetString(data, pos, altLen); pos += altLen;
                    altHvList.Add(altHv);

                    if (pos + 2 > data.Length) break;
                    if (data[pos] != 0x30 || data[pos + 1] != 0x21) break;
                    pos += 2;
                    if (pos + 4 > data.Length) break;
                    k = BitConverter.ToUInt32(data, pos); pos += 4;
                }

                var entry = new HvDicEntry
                {
                    HanViet = hv,
                    Pinyin = pinyin,
                    Meaning = meaning,
                    HanItems = hanList,
                    AltHanViet = altHvList
                };
                entry.ParseDefinitions();
                entries.Add(entry);

                if (k >= 0x100)
                {
                    break;
                }
            }

            lock (_syncLock)
            {
                _allEntries.Clear();
                _byChar.Clear();
                _byCodePoint.Clear();
                _byHvExact.Clear();
                _byHvToneless.Clear();
                _byPinyinExact.Clear();
                _byPinyinToneless.Clear();

                TotalHanItems = hanCount;

                foreach (var entry in entries)
                {
                    _allEntries.Add(entry);

                    // Index by Character / Word and CodePoint
                    foreach (var han in entry.HanItems)
                    {
                        if (!string.IsNullOrEmpty(han.Text))
                        {
                            AddToIndex(_byChar, han.Text, entry);
                            if (han.CodePoint > 0)
                            {
                                AddToIndex(_byCodePoint, han.CodePoint, entry);
                            }
                        }
                    }

                    // Index by Primary Han-Viet reading
                    IndexReading(entry.HanViet, entry);

                    // Index by Alternate Han-Viet readings
                    foreach (var alt in entry.AltHanViet)
                    {
                        IndexReading(alt, entry);
                    }

                    // Index by Pinyin
                    if (!string.IsNullOrEmpty(entry.Pinyin))
                    {
                        string py = entry.Pinyin.Trim().ToLowerInvariant();
                        AddToIndex(_byPinyinExact, py, entry);
                        string pyToneless = LinguisticsUtils.StripPinyinTones(py);
                        if (!string.IsNullOrEmpty(pyToneless) && pyToneless != py)
                        {
                            AddToIndex(_byPinyinToneless, pyToneless, entry);
                        }
                    }
                }
            }
        }

        private void IndexReading(string rawReading, HvDicEntry entry)
        {
            if (string.IsNullOrWhiteSpace(rawReading)) return;
            string clean = rawReading.Trim().ToLowerInvariant();
            string norm = LinguisticsUtils.NormalizeVietnameseTonePlacement(clean);
            AddToIndex(_byHvExact, norm, entry);

            string toneless = LinguisticsUtils.StripVietnameseDiacritics(norm);
            if (!string.IsNullOrEmpty(toneless) && toneless != norm)
            {
                AddToIndex(_byHvToneless, toneless, entry);
            }
        }

        private static void AddToIndex<TKey, TVal>(Dictionary<TKey, List<TVal>> dict, TKey key, TVal val)
        {
            List<TVal> list;
            if (!dict.TryGetValue(key, out list))
            {
                list = new List<TVal>();
                dict[key] = list;
            }
            if (!list.Contains(val))
            {
                list.Add(val);
            }
        }

        public List<HvDicEntry> GetEntriesByChar(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<HvDicEntry>();
            lock (_syncLock)
            {
                List<HvDicEntry> list;
                if (_byChar.TryGetValue(text, out list))
                    return new List<HvDicEntry>(list);
                return new List<HvDicEntry>();
            }
        }

        public List<HvDicEntry> GetEntriesByCodePoint(int cp)
        {
            if (cp <= 0) return new List<HvDicEntry>();
            lock (_syncLock)
            {
                List<HvDicEntry> list;
                if (_byCodePoint.TryGetValue(cp, out list))
                    return new List<HvDicEntry>(list);
                return new List<HvDicEntry>();
            }
        }

        public List<HvDicSearchResult> Search(string rawQuery, SearchFilterOptions options)
        {
            var results = new List<HvDicSearchResult>();
            if (string.IsNullOrWhiteSpace(rawQuery)) return results;
            if (options != null && options.PureNomOnly) return results; // HVDic is Sino-Vietnamese Hanzi dictionary

            string cleanQ = rawQuery.Trim();
            if (cleanQ.Length == 0) return results;

            int limit = (options != null && options.Limit > 0) ? options.Limit : 150;
            string targetLang = (options != null && !string.IsNullOrEmpty(options.TargetLanguage)) ? options.TargetLanguage.ToLowerInvariant() : "all";
            bool matchExactWord = options != null && options.MatchExactWord;
            bool matchExactTone = options != null && options.MatchExactTone;

            lock (_syncLock)
            {
                // 1. Direct Character / Word Lookup (if query has Chinese characters or single character)
                List<HvDicEntry> directEntries;
                if (_byChar.TryGetValue(cleanQ, out directEntries))
                {
                    foreach (var e in directEntries)
                    {
                        foreach (var han in e.HanItems)
                        {
                            if (han.Text == cleanQ)
                            {
                                results.Add(new HvDicSearchResult
                                {
                                    Entry = e,
                                    HanItem = han,
                                    MatchedReading = e.HanViet,
                                    MatchSource = "character",
                                    Score = 100
                                });
                            }
                        }
                    }
                }

                // If target language restricts away from Vietnamese or Chinese, skip phonetic search
                if (targetLang == "japanese" || targetLang == "korean" || targetLang == "nom")
                {
                    return results;
                }

                // 2. Phonetic Search (Hán Việt & Pinyin)
                string qVnNorm = LinguisticsUtils.NormalizeVietnameseTonePlacement(cleanQ.ToLowerInvariant());
                string qVnToneless = LinguisticsUtils.StripVietnameseDiacritics(qVnNorm);
                string qPinyinToneless = LinguisticsUtils.StripPinyinTones(cleanQ.ToLowerInvariant());

                bool searchHv = (targetLang == "all" || targetLang == "vietnamese") && (options == null || options.MatchColHanViet);
                bool searchPy = (targetLang == "all" || targetLang == "mandarin") && (options == null || options.MatchColPinyin);

                var seenItemKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in results)
                {
                    seenItemKeys.Add(r.HanItem.Text + "|" + r.Entry.HanViet);
                }

                Action<Dictionary<string, List<HvDicEntry>>, string, string, int> collectMatches = (dict, key, matchSource, baseScore) =>
                {
                    if (string.IsNullOrEmpty(key)) return;

                    List<HvDicEntry> exactMatches;
                    if (dict.TryGetValue(key, out exactMatches))
                    {
                        foreach (var e in exactMatches)
                        {
                            foreach (var han in e.HanItems)
                            {
                                string itemKey = han.Text + "|" + e.HanViet;
                                if (!seenItemKeys.Contains(itemKey))
                                {
                                    seenItemKeys.Add(itemKey);
                                    results.Add(new HvDicSearchResult
                                    {
                                        Entry = e,
                                        HanItem = han,
                                        MatchedReading = (matchSource.StartsWith("pinyin") ? e.Pinyin : e.HanViet),
                                        MatchSource = matchSource,
                                        Score = baseScore
                                    });
                                }
                            }
                        }
                    }

                    // Prefix matches if not matching exact word
                    if (!matchExactWord && key.Length >= 2)
                    {
                        foreach (var kvp in dict)
                        {
                            if (kvp.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase) && kvp.Key.Length > key.Length)
                            {
                                foreach (var e in kvp.Value)
                                {
                                    foreach (var han in e.HanItems)
                                    {
                                        string itemKey = han.Text + "|" + e.HanViet;
                                        if (!seenItemKeys.Contains(itemKey))
                                        {
                                            seenItemKeys.Add(itemKey);
                                            results.Add(new HvDicSearchResult
                                            {
                                                Entry = e,
                                                HanItem = han,
                                                MatchedReading = (matchSource.StartsWith("pinyin") ? e.Pinyin : e.HanViet),
                                                MatchSource = matchSource + "_prefix",
                                                Score = baseScore - 15
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                if (searchHv)
                {
                    collectMatches(_byHvExact, qVnNorm, "hv_exact", 95);
                    if (!matchExactTone)
                    {
                        collectMatches(_byHvToneless, qVnToneless, "hv_toneless", 85);
                    }
                }

                if (searchPy)
                {
                    collectMatches(_byPinyinExact, cleanQ.ToLowerInvariant(), "pinyin_exact", 90);
                    if (!matchExactTone)
                    {
                        collectMatches(_byPinyinToneless, qPinyinToneless, "pinyin_toneless", 80);
                    }
                }
            }

            // Sort results: highest score first, then shorter word first
            results.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                if (c != 0) return c;
                return a.HanItem.Text.Length.CompareTo(b.HanItem.Text.Length);
            });

            if (results.Count > limit)
            {
                results.RemoveRange(limit, results.Count - limit);
            }

            return results;
        }

        public string GetFormattedDefinition(string ch, bool isVi)
        {
            var entries = GetEntriesByChar(ch);
            if (entries.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine(isVi ? "【TỪ ĐIỂN HÁN VIỆT THI VIỆN (NỘI BỘ)】" : "【HVDIC THI VIEN SINO-VIETNAMESE DICTIONARY (INTERNAL)】");

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (entries.Count > 1)
                {
                    sb.AppendLine(string.Format("--- [Mục {0}/{1}: {2} / {3}] ---", i + 1, entries.Count, e.HanViet, e.Pinyin));
                }

                sb.AppendLine(string.Format(isVi ? "• Âm Hán Việt: {0}" : "• Sino-Vietnamese: {0}", e.HanViet));

                if (e.AltHanViet != null && e.AltHanViet.Count > 0)
                {
                    sb.AppendLine(string.Format(isVi ? "• Các âm đọc khác: {0}" : "• Other readings: {0}", string.Join(", ", e.AltHanViet.ToArray())));
                }

                if (!string.IsNullOrEmpty(e.Pinyin))
                {
                    sb.AppendLine(string.Format(isVi ? "• Phiên âm Pinyin: {0}" : "• Pinyin: {0}", e.Pinyin));
                }

                // Script of this specific character in this entry
                HvDicHanItem matchingHan = null;
                foreach (var h in e.HanItems)
                {
                    if (h.Text == ch) { matchingHan = h; break; }
                }
                if (matchingHan != null)
                {
                    sb.AppendLine(string.Format(isVi ? "• Thể chữ: {0}" : "• Script: {0}", matchingHan.GetScriptLabel(isVi)));
                }

                sb.AppendLine(isVi ? "• Giải nghĩa:" : "• Definitions:");
                if (e.Definitions.Count > 0)
                {
                    foreach (var def in e.Definitions)
                    {
                        sb.AppendLine("  " + def);
                    }
                }
                else if (!string.IsNullOrEmpty(e.Meaning))
                {
                    sb.AppendLine("  " + e.Meaning.Replace("\r", "\n  "));
                }

                sb.AppendLine();
            }

            sb.AppendLine(isVi ? "[Nguồn dữ liệu: Thi Viện HVDic (Cơ sở dữ liệu HVDic.wrd nội bộ)]" : "[Data source: Thi Vien HVDic (Internal HVDic.wrd database)]");
            return sb.ToString().TrimEnd();
        }
    }
}
