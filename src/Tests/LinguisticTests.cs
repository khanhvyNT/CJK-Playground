using System;
using System.Collections.Generic;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Tests
{
    public static class LinguisticTests
    {
        public static bool RunAllTests(out string report)
        {
            var logs = new List<string>();
            int passed = 0;
            int failed = 0;

            Action<string, bool, string> assert = (testName, condition, details) =>
            {
                if (condition)
                {
                    passed++;
                    logs.Add("[PASS] " + testName);
                }
                else
                {
                    failed++;
                    logs.Add("[FAIL] " + testName + " => " + details);
                }
            };

            // Test 1: stripVietnameseDiacritics
            assert("stripVietnameseDiacritics",
                LinguisticsUtils.StripVietnameseDiacritics("thiên") == "thien" &&
                LinguisticsUtils.StripVietnameseDiacritics("nhật") == "nhat" &&
                LinguisticsUtils.StripVietnameseDiacritics("đạo") == "dao" &&
                LinguisticsUtils.StripVietnameseDiacritics("ĐỨC") == "duc" &&
                LinguisticsUtils.StripVietnameseDiacritics("nguyệt") == "nguyet" &&
                LinguisticsUtils.StripVietnameseDiacritics("tâm") == "tam" &&
                LinguisticsUtils.StripVietnameseDiacritics("sáng") == "sang" &&
                LinguisticsUtils.StripVietnameseDiacritics("lượng") == "luong",
                "Khử dấu tiếng Việt không đúng");

            // Test 2: normalizeVietnameseTonePlacement
            assert("normalizeVietnameseTonePlacement",
                LinguisticsUtils.NormalizeVietnameseTonePlacement("hoà") == "hòa" &&
                LinguisticsUtils.NormalizeVietnameseTonePlacement("thuỷ") == "thủy" &&
                LinguisticsUtils.NormalizeVietnameseTonePlacement("qui") == "quy" &&
                LinguisticsUtils.NormalizeVietnameseTonePlacement("xoè") == "xòe",
                "Chuẩn hóa vị trí dấu thanh không đúng");

            // Test 3: hangulToRomaja
            assert("hangulToRomaja",
                LinguisticsUtils.HangulToRomaja("천") == "cheon" &&
                LinguisticsUtils.HangulToRomaja("일") == "il" &&
                LinguisticsUtils.HangulToRomaja("삼") == "sam" &&
                LinguisticsUtils.HangulToRomaja("심") == "sim" &&
                LinguisticsUtils.HangulToRomaja("복") == "bok" &&
                LinguisticsUtils.HangulToRomaja("국") == "guk",
                "Romanization tiếng Hàn không đúng");

            // Test 4: stripPinyinTones
            assert("stripPinyinTones",
                LinguisticsUtils.StripPinyinTones("tiān") == "tian" &&
                LinguisticsUtils.StripPinyinTones("tian1") == "tian" &&
                LinguisticsUtils.StripPinyinTones("zhōng") == "zhong" &&
                LinguisticsUtils.StripPinyinTones("lǚ") == "lu" &&
                LinguisticsUtils.StripPinyinTones("hǎo") == "hao",
                "Khử thanh điệu Pinyin không đúng");

            // Test 5: detectInputScript
            assert("detectInputScript",
                LinguisticsUtils.DetectInputScript("thiên") == InputScriptType.Vietnamese &&
                LinguisticsUtils.DetectInputScript("đạo") == InputScriptType.Vietnamese &&
                LinguisticsUtils.DetectInputScript("tiān") == InputScriptType.Pinyin &&
                LinguisticsUtils.DetectInputScript("ひらがな") == InputScriptType.JapaneseKana &&
                LinguisticsUtils.DetectInputScript("한글") == InputScriptType.KoreanHangul &&
                LinguisticsUtils.DetectInputScript("cheon") == InputScriptType.KoreanRomaja &&
                LinguisticsUtils.DetectInputScript("v:thien") == InputScriptType.PrefixQuery &&
                LinguisticsUtils.DetectInputScript("d:water") == InputScriptType.PrefixQuery &&
                LinguisticsUtils.DetectInputScript("U+5929") == InputScriptType.Codepoint &&
                LinguisticsUtils.DetectInputScript("thien") == InputScriptType.LatinGeneral,
                "Nhận diện ngữ hệ không đúng");

            // Test 6: kanaToRomaji
            assert("kanaToRomaji",
                LinguisticsUtils.HiraganaToKatakana("てん") == "テン" &&
                LinguisticsUtils.KanaToRomaji("テン") == "ten" &&
                LinguisticsUtils.KanaToRomaji("にほん") == "nihon" &&
                LinguisticsUtils.KanaToRomaji("きょ") == "kyo" &&
                LinguisticsUtils.KanaToRomaji("きょう") == "kyou",
                "Chuyển đổi Kana sang Romaji không đúng");

            // Test 7: parseSearchQuery
            var p1 = LinguisticsUtils.ParseSearchQuery("v:thiên");
            var p2 = LinguisticsUtils.ParseSearchQuery("d:heaven");
            var p3 = LinguisticsUtils.ParseSearchQuery("nhật nguyệt");
            assert("parseSearchQuery",
                p1.Scope == "vietnamese" && p1.CleanQuery == "thiên" &&
                p2.Scope == "semantic" && p2.CleanQuery == "heaven" &&
                p3.Scope == "all" && p3.Tokens.Count == 2,
                "Phân tích tiền tố truy vấn BabelMap không đúng");

            // Test 8: searchByPronunciation matching
            var db = new UnihanDatabase();
            var resWithAccent = db.SearchByPronunciation("thiên", new SearchFilterOptions { TargetLanguage = "vietnamese" });
            var charsAccent = new List<string>();
            foreach (var r in resWithAccent) charsAccent.Add(r.Record.Character);

            var resNoAccent = db.SearchByPronunciation("thien", new SearchFilterOptions { TargetLanguage = "vietnamese" });
            var charsNoAccent = new List<string>();
            foreach (var r in resNoAccent) charsNoAccent.Add(r.Record.Character);

            assert("searchByPronunciation ('thiên' & 'thien')",
                charsAccent.Contains("天") && charsAccent.Contains("千") && charsAccent.Contains("遷") &&
                charsNoAccent.Contains("天") && charsNoAccent.Contains("千") && charsNoAccent.Contains("遷"),
                "Tìm kiếm âm Hán Việt không khớp Thiên (天, 千, 遷)");

            // Test 9: Korean Romaja 'cheon' -> 天, 千
            var resKorean = db.SearchByPronunciation("cheon", new SearchFilterOptions { TargetLanguage = "all" });
            var charsKo = new List<string>();
            foreach (var r in resKorean) charsKo.Add(r.Record.Character);
            assert("searchByPronunciation (Korean Romaja 'cheon')",
                charsKo.Contains("天") && charsKo.Contains("千"),
                "Tìm kiếm âm Romaja Hàn 'cheon' không khớp 天, 千");

            // Test 10: Pure Nôm filtering
            var resNom = db.SearchByPronunciation("trời", new SearchFilterOptions { TargetLanguage = "all" });
            var charsNom = new List<string>();
            foreach (var r in resNom) charsNom.Add(r.Record.Character);
            assert("searchByPronunciation ('trời' finds 𡗶 and 天)",
                charsNom.Contains("𡗶") && charsNom.Contains("天"),
                "Tìm 'trời' không chứa 𡗶 (Nôm) hoặc 天 (Nghĩa)");

            var resPureNomOnly = db.SearchByPronunciation("trời", new SearchFilterOptions { TargetLanguage = "all", PureNomOnly = true });
            var charsPureNom = new List<string>();
            foreach (var r in resPureNomOnly) charsPureNom.Add(r.Record.Character);
            assert("pureNomOnly filter strictly rejects BMP",
                charsPureNom.Contains("𡗶") && !charsPureNom.Contains("天"),
                "Bộ lọc pureNomOnly chưa loại bỏ được chữ BMP (天)");

            // Test 11: matchExact rejects prefix
            var resExact = db.SearchByPronunciation("thi", new SearchFilterOptions { TargetLanguage = "vietnamese", MatchExact = true });
            var charsExact = new List<string>();
            foreach (var r in resExact) charsExact.Add(r.Record.Character);
            assert("matchExact rejects prefix",
                !charsExact.Contains("天") && !charsExact.Contains("千"),
                "Chế độ matchExact=true không từ chối tiền tố 'thi'");

            // Test 12: Vietnamese target language includes Nom
            var resVnNom = db.SearchByPronunciation("trời", new SearchFilterOptions { TargetLanguage = "vietnamese" });
            var charsVnNom = new List<string>();
            foreach (var r in resVnNom) charsVnNom.Add(r.Record.Character);
            assert("targetLanguage 'vietnamese' includes Nom readings",
                charsVnNom.Contains("𡗶"),
                "Ngữ hệ 'vietnamese' không tìm thấy chữ Nôm 'trời' (𡗶)");

            // Test 13: Strict Language Filter Isolation
            var resKoInJa = db.SearchByPronunciation("cheon", new SearchFilterOptions { TargetLanguage = "japanese" });
            var resKoInKo = db.SearchByPronunciation("cheon", new SearchFilterOptions { TargetLanguage = "korean" });
            assert("languageFilter strictly isolates Japanese from Korean",
                resKoInJa.Count == 0 && resKoInKo.Count > 0,
                "Bộ lọc ngữ hệ chưa cô lập chính xác (âm Hàn lọt vào tiếng Nhật)");

            // Test 14: Pure Nom strictly excludes BMP Hanzi with Han-Viet / Mandarin readings
            bool bmpHanziIsPure = CJKPlayground.Services.LinguisticsUtils.IsPureChuNom(0x674E, new List<string> { "lý" }, new List<string> { "lý" }, new List<string> { "lǐ" }, "");
            bool extBNomIsPure = CJKPlayground.Services.LinguisticsUtils.IsPureChuNom(0x215F6, new List<string> { "trời" }, new List<string>(), new List<string>(), "Chữ Nôm: Trời");
            assert("pureNom strictly distinguishes ExtB Nom from BMP Hanzi",
                !bmpHanziIsPure && extBNomIsPure,
                "Chữ Hán BMP (李) bị nhầm thành Chữ Nôm hoặc chữ Nôm ExtB (𡗶) bị từ chối");

            // Test 15: MatchExactTone (Exact Diacritics)
            var resToneExact = db.SearchByPronunciation("thien", new SearchFilterOptions { TargetLanguage = "vietnamese", MatchExactTone = true });
            var charsToneExact = new List<string>();
            foreach (var r in resToneExact) charsToneExact.Add(r.Record.Character);
            assert("matchExactTone strictly rejects accented 'thiên' when searching unaccented 'thien'",
                !charsToneExact.Contains("天"),
                "Chế độ MatchExactTone=true vẫn cho phép 'thien' khớp với 'thiên' (天)");

            // Test 16: Column matching isolation (MatchColHanViet vs MatchColNom)
            var resColNomOnly = db.SearchByPronunciation("thiên", new SearchFilterOptions { TargetLanguage = "vietnamese", MatchColNom = true, MatchColHanViet = false });
            var charsColNomOnly = new List<string>();
            foreach (var r in resColNomOnly) charsColNomOnly.Add(r.Record.Character);
            assert("matchCol isolation: Han-Viet only reading not returned when MatchColHanViet is false",
                !charsColNomOnly.Contains("天"),
                "Khớp cột chưa cô lập được: Thiên (天) lọt vào kết quả dù chỉ chọn cột Chữ Nôm");

            report = string.Format("Kết quả kiểm thử ngôn ngữ học:\nĐã chạy: {0} bài test\nThành công: {1}\nThất bại: {2}\n\nChi tiết:\n{3}",
                passed + failed, passed, failed, string.Join("\n", logs));

            return failed == 0;
        }
    }
}
