using System;
using System.Collections.Generic;
using System.IO;
using CJKPlayground.Models;
using CJKPlayground.Services;

namespace CJKPlayground.Tests
{
    public static class HvDicTests
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

            // Instantiate and synchronously load database for testing
            var db = new HvDicDatabase();
            // Candidate paths check
            string[] testPaths = new[]
            {
                "data/HVDic.wrd",
                "../data/HVDic.wrd",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "HVDic.wrd"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HVDic.wrd"),
                @"c:\Users\Admin\Documents\Tool\HVDic223\HVDic.wrd",
                @"c:\Users\Admin\Desktop\CJKPlayground\data\HVDic.wrd"
            };
            foreach (var p in testPaths)
            {
                if (File.Exists(p))
                {
                    try { db.LoadFromFile(p); break; } catch { }
                }
            }

            // Test 1: Database Loaded and Record Counts
            assert("HvDicDatabase.RecordCounts",
                db.TotalRecords >= 15000 && db.TotalHanItems >= 20000,
                string.Format("Số lượng bản ghi không đạt kỳ vọng: TotalRecords={0}, TotalHan={1}", db.TotalRecords, db.TotalHanItems));

            // Test 2: Direct lookup for '天' (Tian / Thiên)
            var tianEntries = db.GetEntriesByChar("天");
            assert("HvDicDatabase.Lookup_Tian",
                tianEntries != null && tianEntries.Count > 0 &&
                tianEntries[0].HanViet.Equals("thiên", StringComparison.OrdinalIgnoreCase) &&
                tianEntries[0].Pinyin.Equals("tian1", StringComparison.OrdinalIgnoreCase) &&
                tianEntries[0].Definitions.Count >= 2,
                "Tra cứu chữ '天' thất bại");

            // Test 3: Direct lookup for '明' (Minh)
            var minhEntries = db.GetEntriesByChar("明");
            assert("HvDicDatabase.Lookup_Ming",
                minhEntries != null && minhEntries.Count > 0 &&
                minhEntries[0].HanViet.Equals("minh", StringComparison.OrdinalIgnoreCase) &&
                minhEntries[0].Pinyin.Equals("ming2", StringComparison.OrdinalIgnoreCase),
                "Tra cứu chữ '明' thất bại");

            // Test 4: Direct lookup for '字' (Tự / Zi)
            var ziEntries = db.GetEntriesByChar("字");
            assert("HvDicDatabase.Lookup_Zi",
                ziEntries != null && ziEntries.Count > 0 &&
                ziEntries[0].HanViet.Equals("tự", StringComparison.OrdinalIgnoreCase) &&
                ziEntries[0].Pinyin.Equals("zi4", StringComparison.OrdinalIgnoreCase),
                "Tra cứu chữ '字' thất bại");

            // Test 5: Traditional vs Simplified script distinction for '愛' vs '爱'
            var aiTradEntries = db.GetEntriesByChar("愛");
            var aiSimpEntries = db.GetEntriesByChar("爱");
            bool tradOk = false;
            if (aiTradEntries != null && aiTradEntries.Count > 0)
            {
                foreach (var h in aiTradEntries[0].HanItems)
                {
                    if (h.Text == "愛" && h.IsTraditional && !h.IsSimplified) tradOk = true;
                }
            }
            bool simpOk = false;
            if (aiSimpEntries != null && aiSimpEntries.Count > 0)
            {
                foreach (var h in aiSimpEntries[0].HanItems)
                {
                    if (h.Text == "爱" && h.IsSimplified && !h.IsTraditional) simpOk = true;
                }
            }
            assert("HvDicDatabase.ScriptDistinction_Ai",
                tradOk && simpOk,
                "Phân biệt thể chữ Phồn thể/Giản thể cho '愛'/'爱' không đúng");

            // Test 6: Search by Han-Viet exact pronunciation 'thiên'
            var searchThien = db.Search("thiên", new SearchFilterOptions
            {
                TargetLanguage = "vietnamese",
                MatchExactWord = true,
                MatchExactTone = true
            });
            bool foundTianInSearch = false;
            foreach (var r in searchThien)
            {
                if (r.HanItem.Text == "天") { foundTianInSearch = true; break; }
            }
            assert("HvDicDatabase.Search_HanViet_Exact",
                searchThien.Count > 0 && foundTianInSearch,
                "Tìm kiếm theo âm Hán Việt 'thiên' không tìm thấy chữ '天'");

            // Test 7: Search by toneless Han-Viet 'thien'
            var searchThienToneless = db.Search("thien", new SearchFilterOptions
            {
                TargetLanguage = "vietnamese",
                MatchExactWord = true,
                MatchExactTone = false
            });
            bool foundTianInToneless = false;
            foreach (var r in searchThienToneless)
            {
                if (r.HanItem.Text == "天") { foundTianInToneless = true; break; }
            }
            assert("HvDicDatabase.Search_HanViet_Toneless",
                searchThienToneless.Count >= searchThien.Count && foundTianInToneless,
                "Tìm kiếm không dấu 'thien' không bao quát đủ các dấu thanh");

            // Test 8: Search with MatchExactTone strictly rejecting unaccented query against accented readings
            var searchThienStrict = db.Search("thien", new SearchFilterOptions
            {
                TargetLanguage = "vietnamese",
                MatchExactWord = true,
                MatchExactTone = true
            });
            bool hasThienInStrict = false;
            foreach (var r in searchThienStrict)
            {
                if (r.Entry.HanViet == "thiên") { hasThienInStrict = true; break; }
            }
            assert("HvDicDatabase.Search_MatchExactTone_Strict",
                !hasThienInStrict,
                "MatchExactTone=true nhưng vẫn trả về âm có dấu 'thiên' khi tra 'thien'");

            // Test 9: Search by Pinyin 'tian1'
            var searchPinyin = db.Search("tian1", new SearchFilterOptions
            {
                TargetLanguage = "mandarin",
                MatchColPinyin = true,
                MatchExactWord = true,
                MatchExactTone = true
            });
            bool foundTianInPinyin = false;
            foreach (var r in searchPinyin)
            {
                if (r.HanItem.Text == "天") { foundTianInPinyin = true; break; }
            }
            assert("HvDicDatabase.Search_Pinyin",
                searchPinyin.Count > 0 && foundTianInPinyin,
                "Tìm kiếm theo Pinyin 'tian1' không tìm thấy '天'");

            // Test 10: PureNomOnly filter strictly returns empty
            var nomOnlySearch = db.Search("thiên", new SearchFilterOptions
            {
                PureNomOnly = true
            });
            assert("HvDicDatabase.PureNomOnly_Rejection",
                nomOnlySearch.Count == 0,
                "HVDic là từ điển Hán Việt nhưng bộ lọc PureNomOnly không loại trừ");

            // Test 11: Formatted definition generation
            string defVi = db.GetFormattedDefinition("天", true);
            string defEn = db.GetFormattedDefinition("天", false);
            assert("HvDicDatabase.FormattedDefinition",
                !string.IsNullOrEmpty(defVi) && defVi.Contains("【TỪ ĐIỂN HÁN VIỆT THI VIỆN (NỘI BỘ)】") &&
                defVi.Contains("thiên") && defVi.Contains("tian1") &&
                !string.IsNullOrEmpty(defEn) && defEn.Contains("【HVDIC THI VIEN SINO-VIETNAMESE DICTIONARY (INTERNAL)】"),
                "Định dạng bài viết từ điển HVDic Thi Viện không đúng cấu trúc chuẩn");

            // Final Report formatting
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Kết quả kiểm thử HVDic Thi Viện Database (Nội bộ):");
            sb.AppendLine(string.Format("Đã chạy: {0} bài test", passed + failed));
            sb.AppendLine(string.Format("Thành công: {0}", passed));
            sb.AppendLine(string.Format("Thất bại: {0}", failed));
            sb.AppendLine();
            sb.AppendLine("Chi tiết:");
            foreach (var log in logs)
            {
                sb.AppendLine(log);
            }

            report = sb.ToString();
            return failed == 0;
        }
    }
}
