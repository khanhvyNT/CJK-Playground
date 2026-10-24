using System;
using System.Collections.Generic;

namespace CJKPlayground.Services
{
    public enum SupportedLanguage
    {
        Vietnamese,
        English
    }

    public static class LocalizationManager
    {
        public static SupportedLanguage CurrentLanguage { get; private set; }

        public static event Action LanguageChanged;

        private static readonly Dictionary<SupportedLanguage, Dictionary<string, string>> Strings =
            new Dictionary<SupportedLanguage, Dictionary<string, string>>();

        static LocalizationManager()
        {
            CurrentLanguage = SupportedLanguage.Vietnamese;
            InitVietnamese();
            InitEnglish();
        }

        public static void SetLanguage(SupportedLanguage lang)
        {
            CurrentLanguage = lang;
            if (LanguageChanged != null)
            {
                LanguageChanged();
            }
        }

        public static string Get(string key, string fallback = "")
        {
            Dictionary<string, string> dict;
            if (Strings.TryGetValue(CurrentLanguage, out dict))
            {
                string val;
                if (dict.TryGetValue(key, out val))
                    return val;
            }
            return !string.IsNullOrEmpty(fallback) ? fallback : key;
        }

        private static void InitVietnamese()
        {
            var d = new Dictionary<string, string>
            {
                // App & Menus
                { "AppTitle", "CJK Playground" },
                { "MenuFile", "Tập tin" },
                { "MenuOpenFont", "Mở font từ máy tính (.ttf, .otf)..." },
                { "MenuSelectSystem", "Chọn Font CJK hệ thống..." },
                { "MenuExit", "Thoát" },
                { "MenuTools", "Công cụ" },
                { "MenuBasket", "Giỏ ký tự đã lưu" },
                { "MenuBasketCountFmt", "🧺 Giỏ ký tự ({0})" },
                { "MenuReport", "Báo cáo độ phủ font (Font Coverage Report)..." },
                { "MenuAutoComposite", "Tự động chọn Font hệ thống theo khối (Composite System Font)" },
                { "MenuLinguisticTests", "Chạy kiểm thử ngôn ngữ học (Linguistic Tests)..." },
                { "MenuOpenTypeTests", "Chạy kiểm thử OpenType Engine (Phase 2 Tests)..." },
                { "MenuLanguage", "Ngôn ngữ" },
                { "MenuHelp", "Trợ giúp" },
                { "MenuAbout", "Giới thiệu CJK Playground" },

                // Tabs
                { "TabCharMap", "Bảng mã ký tự (Character Map)" },
                { "TabSearch", "Tra cứu âm đọc & Từ điển Online" },
                { "TabIds", "Ghép chữ IDS" },

                // Tab 1: Character Map
                { "BlockTreeHeader", "Khối Unicode" },
                { "Plane0Node", "📁 Plane 0: BMP (Đa ngữ cơ bản)" },
                { "Plane2Node", "📁 Plane 2: SIP (Chữ Nôm & CJK mở rộng)" },
                { "Plane3Node", "📁 Plane 3: TIP (Chữ Hán mở rộng G/H)" },
                { "PagePrev", "◄ Trước" },
                { "PageNext", "Sau ►" },
                { "PageInfoFmt", "Trang {0} / {1} ({2:N0} ký tự)" },
                { "JumpToHex", "Nhảy tới Hex:" },
                { "JumpBtn", "Đi" },
                { "InspectorTitle", "CHI TIẾT KÝ TỰ" },
                { "InspectorHexFmt", "Mã điểm: {0} ({1})" },
                { "InspectorBlockFmt", "Khối: {0}{1}" },
                { "InspectorStrokesFmt", "Số nét: {0} | Bộ thủ: {1}" },
                { "InspectorStrokesEmpty", "Số nét / Bộ thủ: --" },
                { "InspectorGlyphIdFmt", "Glyph ID: #{0} | Độ rộng: {1}" },
                { "InspectorGlyphIdEmpty", "Glyph ID: -- | Độ rộng: --" },
                { "ClassNomBadge", "   [Chữ Nôm]" },
                { "ClassHanziBadge", "   [Chữ Hán]" },
                { "ClassNomFull", "Chữ Nôm (Việt Nam)" },
                { "ClassHanziFull", "Chữ Hán (CJK Ideograph)" },

                // Inspector Action Buttons
                { "CopyChar", "📋 Sao chép ký tự" },
                { "CopyHex", "🔢 Sao chép U+XXXX" },
                { "CopyImage", "🖼️ Sao chép ảnh (Bitmap)" },
                { "LookupChar", "🔍 Tra từ điển chữ này" },
                { "AddToBasket", "🧺 Thêm vào giỏ" },
                { "ExportSvg", "💾 Xuất vector SVG" },

                // Tab 2: Phonetic & Online Search
                { "SearchPrompt", "Nhập phiên âm (thiên, tiān, trời), cấu trúc IDS (⿰日月), tiền tố (ids:, v:, nom:, py:, d:), mã Hex hoặc dán chữ Hán/Nôm:" },
                { "SearchBtn", "🔍 Tra cứu" },
                { "PureNomCheck", "Chỉ Chữ Nôm (Unihan/Online)" },
                { "FilterColsLabel", "Khớp cột:" },
                { "FilterColHanViet", "Hán Việt" },
                { "FilterColNom", "IDS" },
                { "FilterColPinyin", "Pinyin" },
                { "FilterColMeaning", "Nghĩa từ điển" },
                { "FilterRulesLabel", "| Quy tắc:" },
                { "MatchExactWord", "Khớp toàn từ" },
                { "MatchExactTone", "Đúng dấu thanh" },

                // Search Lang Combo
                { "LangAll", "Tất cả ngữ hệ" },
                { "LangVn", "Tiếng Việt (v:)" },
                { "LangPy", "Pinyin / Quan thoại (py:)" },
                { "LangCantonese", "Quảng Đông (c:)" },
                { "LangJa", "Tiếng Nhật (ja:)" },
                { "LangKo", "Tiếng Hàn (ko:)" },
                { "LangMeaning", "Nghĩa từ điển (d:)" },

                // Search Table Columns
                { "ColChar", "Chữ / Từ" },
                { "ColHex", "Mã Hex" },
                { "ColSource", "Nguồn dữ liệu" },
                { "ColMatched", "Đọc khớp" },
                { "ColHanViet", "Hán Việt" },
                { "ColNom", "IDS" },
                { "ColPinyin", "Pinyin" },
                { "ColStrokes", "Số nét" },
                { "ColWebSource", "Nguồn Web (URL / Trích dẫn)" },

                // Search Result Source Tags
                { "TagUnihanNom", "[Unihan] Chữ Nôm" },
                { "TagUnihanHanzi", "[Unihan] Chữ Hán" },
                { "TagHvDic", "[HVDic Thi viện]" },
                { "TagCombinedHvDic", "[Unihan + HVDic]" },
                { "TagOnlineNom", "[Online API] Chữ Nôm" },
                { "TagOnlineHanzi", "[Online API] Chữ Hán" },
                { "TagCombinedNom", "[Unihan + Online] Chữ Nôm" },
                { "TagCombinedHanzi", "[Unihan + Online] Chữ Hán" },
                { "TagLocalUnihan", "Nội bộ (Embedded Unihan DB)" },
                { "TagHvDicSource", "Nội bộ (HVDic Thi viện .wrd)" },

                // Search Details Panel
                { "SearchDetailsPrompt", "Chọn một chữ trong danh sách kết quả để xem định nghĩa chi tiết từ điển..." },
                { "SearchDetailsSearching", "Đang tra cứu cơ sở dữ liệu Unihan & Online API..." },
                { "OpenWebSource", "🌐 Mở trang Web nguồn" },
                { "CopyWebUrl", "📋 Sao chép URL" },
                { "WebSourceCitationPrefix", "Nguồn: " },
                { "SearchHeaderSummaryFmt", "Mã điểm: {0} | Số nét: {1} | Bộ thủ: {2}\nNguồn dữ liệu: {3} | Phân loại: {4}\nHán Việt: {5} | Chữ Nôm: {6} | Pinyin: {7}" },
                { "SectionTitleChar", "KÝ TỰ / TỪ: {0} ({1})" },
                { "SectionClassification", "PHÂN LOẠI: {0}" },
                { "SectionDataSource", "NGUỒN DỮ LIỆU: {0}" },
                { "SectionWebUrl", "TRANG WEB NGUỒN: {0}" },
                { "SectionWebCitation", "BẢN QUYỀN TRÍCH DẪN: {0}" },
                { "SectionReadingsHeader", "【PHIÊN ÂM TOÀN DIỆN】" },
                { "ReadingHanViet", "• Hán Việt: {0}" },
                { "ReadingNom", "• Chữ Nôm: {0}" },
                { "ReadingPinyin", "• Pinyin / Quan thoại: {0}" },
                { "ReadingCantonese", "• Quảng Đông: {0}" },
                { "ReadingJapanese", "• Tiếng Nhật: {0}" },
                { "ReadingKorean", "• Hàn Quốc (Romaja): {0}" },
                { "SectionLocalUnihanHeader", "【CƠ SỞ DỮ LIỆU UNIHAN NỘI BỘ】" },
                { "DefVi", "Nghĩa Hán Việt: {0}" },
                { "DefEn", "English Definition: {0}" },
                { "SectionHvDicHeader", "【TỪ ĐIỂN HÁN VIỆT THI VIỆN (NỘI BỘ)】" },
                { "SectionOnlineDictHeader", "【TỪ ĐIỂN TRỰC TUYẾN (THI VIỆN & WIKTIONARY)】" },

                // Tab 3: IDS Composer
                { "IdsTitle", "Ghép chữ IDS" },
                { "IdsBadge", "(Coming soon)" },
                { "IdsDesc", "Khu vực đang trong trạng thái trống.\nTính năng ghép chữ IDS đang được tái thiết kế theo quy chuẩn Typography Đông Á." },

                // Character Basket Form
                { "BasketTitle", "Giỏ ký tự đã lưu (Character Basket)" },
                { "BasketCountFmt", "Số ký tự trong giỏ: {0}" },
                { "BasketColChar", "Chữ" },
                { "BasketColHex", "Mã Hex" },
                { "BasketColBlock", "Khối Unicode" },
                { "BasketColReading", "Hán Việt" },
                { "BtnClose", "Đóng" },
                { "ClearBasket", "🧹 Xóa toàn bộ" },
                { "CopyAllChars", "📋 Sao chép chữ" },
                { "CopyAllHex", "🔢 Sao chép mã Hex" },
                { "ExportZip", "💾 Tải gói ZIP (.zip)" },
                { "ExportTxt", "📄 Xuất file .txt" },

                // Font Coverage Report Form
                { "ReportTitle", "Báo cáo độ phủ font (Font Coverage Report)" },
                { "ReportModeLabel", "Chế độ phân tích độ phủ:" },
                { "ReportModeSingleFmt", "1. Font hiện tại ({0})" },
                { "ReportModeSystem", "2. Toàn bộ Font hệ thống Windows (Composite System)" },
                { "ReportColBlock", "Khối Unicode" },
                { "ReportColRange", "Dải mã" },
                { "ReportColChars", "Ký tự có / Tổng" },
                { "ReportColCoverage", "Độ phủ (%)" },
                { "ReportColPlane", "Mặt phẳng" },
                { "ReportCopyBtn", "📋 Sao chép báo cáo" },
                { "ReportSaveBtn", "💾 Lưu báo cáo (.md)" },

                // System Font Selector Dialog
                { "SysFontTitle", "Chọn font CJK hệ thống" },
                { "SysFontSelectBtn", "Chọn font này" },
                { "SysFontNotFound", "Không tìm thấy font CJK hệ thống phổ biến." },

                // Status Bar & Tooltips
                { "StatusReady", "Sẵn sàng" },
                { "StatusUnihanTotalFmt", "Cơ sở dữ liệu Unihan: {0:N0} ký tự" },
                { "StatusSelectedCharFmt", "Ký tự: {0} ({1})" },
                { "StatusSearching", "Đang tra cứu cơ sở dữ liệu Unihan & Online API..." },
                { "StatusFoundOnlineFmt", "Tìm thấy {0} kết quả (Unihan: {1} | Online API: {2})" },
                { "StatusFoundLocalFmt", "Tìm thấy {0} kết quả (Unihan nội bộ) [Online API: Đã hoàn tất]" },
                { "StatusSearchCompleteFmt", "Tra cứu hoàn tất ({0} kết quả)" },
                { "StatusCopiedCharFmt", "Đã sao chép vào Clipboard: {0}" },
                { "StatusCopiedHexFmt", "Đã sao chép mã hex: {0}" },
                { "StatusCopiedBitmapFmt", "Đã sao chép ảnh bitmap 512x512 của chữ {0} vào Clipboard!" },
                { "StatusAddedBasketFmt", "Đã thêm {0} vào giỏ ký tự!" },
                { "StatusAlreadyInBasketFmt", "{0} đã có sẵn trong giỏ." },
                { "StatusExportedSvgFmt", "Đã xuất vector SVG: {0}" },
                { "StatusCopiedUrlFmt", "Đã sao chép link nguồn vào bộ nhớ tạm: {0}" },
                { "StatusFontLoading", "Font: Đang tải..." },
                { "StatusFontCustom", "(Tệp riêng)" },
                { "StatusFontSystem", "(Hệ thống)" },
                { "StatusCompositeAuto", "[Composite Tự động]" },
                { "StatusCompositeFixed", "[Cố định]" },
                { "StatusFontExtraFmt", " | {0} glyphs | {1} ký tự | em={2}" },

                // Restart Messages
                { "RestartMsgVi", "Đã thay đổi ngôn ngữ sang Tiếng Việt. Ứng dụng sẽ khởi động lại để áp dụng giao diện mới." },
                { "RestartTitleVi", "Đổi ngôn ngữ" },
                { "RestartMsgEn", "Language changed to English. The application will restart to apply the new interface." },
                { "RestartTitleEn", "Language Changed" },

                // Common Titles & Notifications
                { "SuccessTitle", "Thành công" },
                { "ErrorTitle", "Lỗi" },
                { "WarningTitle", "Cảnh báo" },
                { "NoticeTitle", "Thông báo" },
                { "FontDragDropLoaded", "Đã nạp thành công font: {0}" },
                { "FontDragDropError", "Lỗi nạp font: {0}" },
                { "InvalidHexError", "Mã Hex không hợp lệ." },
                { "TipBlock", "Khối:" },
                { "TipSinoVietnamese", "Hán Việt:" },
                { "TipNom", "Chữ Nôm:" },
                { "TipRadicalStrokesFmt", "Bộ: {0} ({1} nét)" },
                { "StatusUnihanReadyFmt", "Cơ sở dữ liệu Unihan: {0:N0} ký tự đã sẵn sàng" },
                { "DiagnosticsSuccessTitle", "Chẩn đoán thành công (19/19 Tests Passed)" },
                { "DiagnosticsErrorTitle", "Chẩn đoán có lỗi" },
                { "DiagnosticsReportHeader", "=== KẾT QUẢ CHẨN ĐOÁN HỆ THỐNG CJK PLAYGROUND ===\n\n" },

                // About Box Text
                { "AboutTitle", "Giới thiệu CJK Playground" },
                { "AboutMessageFmt", "CJK Playground (Phiên bản 1.0)\n\n" +
                    "Bộ công cụ khám phá, tra cứu ngữ học và thiết kế kiểu chữ CJK & Chữ Nôm chuyên nghiệp.\n\n" +
                    "• Tác giả & Nguồn:\n" +
                    "  ✦ Tác giả & Nhà phát triển: Nguyễn Tuấn Vũ\n" +
                    "  ✦ GitHub: https://github.com/khanhvyNT | Facebook: https://www.facebook.com/oerlikon01\n" +
                    "  ✦ Nguồn dữ liệu:\n" +
                    "    - Từ điển Hán Nôm: https://hvdic.thivien.net/\n" +
                    "    - Phông chữ Nôm Na Tống (Nom Na Tong): https://nomfoundation.org/ (Hội Bảo tồn di sản chữ Nôm Việt Nam)\n" +
                    "    - Unicode Consortium (Unihan): https://home.unicode.org/\n\n" +
                    "• Khám phá ký tự Unicode: Duyệt toàn diện kho chữ Hán & Chữ Nôm trên cả ba mặt phẳng BMP, SIP (Plane 2) và TIP (Plane 3).\n" +
                    "• Tra cứu âm đọc & Từ điển: Đối chiếu âm đọc Hán Việt, Nôm, Pinyin, Tiếng Nhật, Hàn Quốc cùng từ điển trực tuyến.\n" +
                    "• Xưởng ghép chữ IDS: Ghép chữ trực quan với thuật toán quang học tự động cân bằng bố cục chuẩn ấn loát.\n" +
                    "• Quản lý & Xuất bản: Quản lý giỏ ký tự, sổ bộ ký tự tự tạo (PUA) và xuất đồ họa vector SVG sắc nét.\n" +
                    "• Giao diện tối ưu & Song ngữ: Trải nghiệm mượt mà với hỗ trợ đầy đủ Tiếng Việt và English." }
            };
            Strings[SupportedLanguage.Vietnamese] = d;
        }

        private static void InitEnglish()
        {
            var d = new Dictionary<string, string>
            {
                // App & Menus
                { "AppTitle", "CJK Playground" },
                { "MenuFile", "File" },
                { "MenuOpenFont", "Open Font File (.ttf, .otf)..." },
                { "MenuSelectSystem", "Select System CJK Font..." },
                { "MenuExit", "Exit" },
                { "MenuTools", "Tools" },
                { "MenuBasket", "Saved Character Basket" },
                { "MenuBasketCountFmt", "🧺 Character Basket ({0})" },
                { "MenuReport", "Font Coverage Report..." },
                { "MenuAutoComposite", "Auto Composite System Font by Block" },
                { "MenuLinguisticTests", "Run Linguistic Tests..." },
                { "MenuOpenTypeTests", "Run OpenType Engine Tests..." },
                { "MenuLanguage", "Language" },
                { "MenuHelp", "Help" },
                { "MenuAbout", "About CJK Playground" },

                // Tabs
                { "TabCharMap", "Character Map" },
                { "TabSearch", "Phonetic & Online Search" },
                { "TabIds", "IDS Composer" },

                // Tab 1: Character Map
                { "BlockTreeHeader", "Unicode Blocks" },
                { "Plane0Node", "📁 Plane 0: BMP (Basic Multilingual Plane)" },
                { "Plane2Node", "📁 Plane 2: SIP (Nom & CJK Ext)" },
                { "Plane3Node", "📁 Plane 3: TIP (CJK Ext G/H)" },
                { "PagePrev", "◄ Prev" },
                { "PageNext", "Next ►" },
                { "PageInfoFmt", "Page {0} / {1} ({2:N0} chars)" },
                { "JumpToHex", "Jump to Hex:" },
                { "JumpBtn", "Go" },
                { "InspectorTitle", "CHARACTER INSPECTOR" },
                { "InspectorHexFmt", "Hex Code: {0} ({1})" },
                { "InspectorBlockFmt", "Block: {0}{1}" },
                { "InspectorStrokesFmt", "Strokes: {0} | Radical: {1}" },
                { "InspectorStrokesEmpty", "Strokes / Radical: --" },
                { "InspectorGlyphIdFmt", "Glyph ID: #{0} | Advance: {1}" },
                { "InspectorGlyphIdEmpty", "Glyph ID: -- | Advance: --" },
                { "ClassNomBadge", "   [Nom Character]" },
                { "ClassHanziBadge", "   [Hanzi]" },
                { "ClassNomFull", "Nom Character (Vietnam)" },
                { "ClassHanziFull", "Hanzi (CJK Ideograph)" },

                // Inspector Action Buttons
                { "CopyChar", "📋 Copy Character" },
                { "CopyHex", "🔢 Copy U+XXXX" },
                { "CopyImage", "🖼️ Copy Image (Bitmap)" },
                { "LookupChar", "🔍 Lookup in Dictionary" },
                { "AddToBasket", "🧺 Add to Basket" },
                { "ExportSvg", "💾 Export SVG Vector" },

                // Tab 2: Phonetic & Online Search
                { "SearchPrompt", "Enter reading (thien, tian, troi), IDS structure (⿰日月), prefix (ids:, v:, nom:, py:, d:), Hex, or paste Han/Nom character:" },
                { "SearchBtn", "🔍 Search" },
                { "PureNomCheck", "Nom Only (Unihan/Online)" },
                { "FilterColsLabel", "Match columns:" },
                { "FilterColHanViet", "Sino-Vietnamese" },
                { "FilterColNom", "IDS" },
                { "FilterColPinyin", "Pinyin" },
                { "FilterColMeaning", "Definition" },
                { "FilterRulesLabel", "| Rules:" },
                { "MatchExactWord", "Exact Word" },
                { "MatchExactTone", "Exact Tone" },

                // Search Lang Combo
                { "LangAll", "All Languages" },
                { "LangVn", "Vietnamese (v:)" },
                { "LangPy", "Pinyin / Mandarin (py:)" },
                { "LangCantonese", "Cantonese (c:)" },
                { "LangJa", "Japanese (ja:)" },
                { "LangKo", "Korean (ko:)" },
                { "LangMeaning", "Dictionary Definition (d:)" },

                // Search Table Columns
                { "ColChar", "Character" },
                { "ColHex", "Hex Code" },
                { "ColSource", "Data Source" },
                { "ColMatched", "Matched Reading" },
                { "ColHanViet", "Sino-Vietnamese" },
                { "ColNom", "IDS" },
                { "ColPinyin", "Pinyin" },
                { "ColStrokes", "Strokes" },
                { "ColWebSource", "Web Source (URL / Citation)" },

                // Search Result Source Tags
                { "TagUnihanNom", "[Unihan] Nom Character" },
                { "TagUnihanHanzi", "[Unihan] Hanzi" },
                { "TagHvDic", "[HVDic Thi Vien]" },
                { "TagCombinedHvDic", "[Unihan + HVDic]" },
                { "TagOnlineNom", "[Online API] Nom Character" },
                { "TagOnlineHanzi", "[Online API] Hanzi" },
                { "TagCombinedNom", "[Unihan + Online] Nom Character" },
                { "TagCombinedHanzi", "[Unihan + Online] Hanzi" },
                { "TagLocalUnihan", "Local (Embedded Unihan DB)" },
                { "TagHvDicSource", "Internal (HVDic Thi Vien .wrd)" },

                // Search Details Panel
                { "SearchDetailsPrompt", "Select a character in the search results to view detailed dictionary definition..." },
                { "SearchDetailsSearching", "Searching Unihan database & Online API..." },
                { "OpenWebSource", "🌐 Open Web Source" },
                { "CopyWebUrl", "📋 Copy URL" },
                { "WebSourceCitationPrefix", "Source: " },
                { "SearchHeaderSummaryFmt", "Hex Code: {0} | Strokes: {1} | Radical: {2}\nData Source: {3} | Classification: {4}\nSino-Vietnamese: {5} | Nom: {6} | Pinyin: {7}" },
                { "SectionTitleChar", "CHARACTER: {0} ({1})" },
                { "SectionClassification", "CLASSIFICATION: {0}" },
                { "SectionDataSource", "DATA SOURCE: {0}" },
                { "SectionWebUrl", "SOURCE WEB PAGE: {0}" },
                { "SectionWebCitation", "CITATION / COPYRIGHT: {0}" },
                { "SectionReadingsHeader", "【COMPREHENSIVE READINGS】" },
                { "ReadingHanViet", "• Sino-Vietnamese: {0}" },
                { "ReadingNom", "• Nom: {0}" },
                { "ReadingPinyin", "• Pinyin / Mandarin: {0}" },
                { "ReadingCantonese", "• Cantonese: {0}" },
                { "ReadingJapanese", "• Japanese: {0}" },
                { "ReadingKorean", "• Korean (Romaja): {0}" },
                { "SectionLocalUnihanHeader", "【LOCAL UNIHAN DATABASE】" },
                { "DefVi", "Sino-Vietnamese Definition: {0}" },
                { "DefEn", "English Definition: {0}" },
                { "SectionHvDicHeader", "【HVDIC THI VIEN SINO-VIETNAMESE DICTIONARY (INTERNAL)】" },
                { "SectionOnlineDictHeader", "【ONLINE DICTIONARIES (THI VIEN & WIKTIONARY)】" },

                // Tab 3: IDS Composer
                { "IdsTitle", "IDS Character Composition" },
                { "IdsBadge", "(Coming soon)" },
                { "IdsDesc", "This workspace is currently empty.\nIDS character composition is being redesigned according to East Asian typography standards." },

                // Character Basket Form
                { "BasketTitle", "Saved Character Basket" },
                { "BasketCountFmt", "Characters in basket: {0}" },
                { "BasketColChar", "Character" },
                { "BasketColHex", "Hex Code" },
                { "BasketColBlock", "Unicode Block" },
                { "BasketColReading", "Sino-Vietnamese" },
                { "BtnClose", "Close" },
                { "ClearBasket", "🧹 Clear All" },
                { "CopyAllChars", "📋 Copy Characters" },
                { "CopyAllHex", "🔢 Copy Hex List" },
                { "ExportZip", "💾 Export ZIP (.zip)" },
                { "ExportTxt", "📄 Export .txt File" },

                // Font Coverage Report Form
                { "ReportTitle", "Font Coverage Report" },
                { "ReportModeLabel", "Coverage Analysis Mode:" },
                { "ReportModeSingleFmt", "1. Current Active Font ({0})" },
                { "ReportModeSystem", "2. All Windows System Fonts (Composite System)" },
                { "ReportColBlock", "Unicode Block" },
                { "ReportColRange", "Code Range" },
                { "ReportColChars", "Supported / Total" },
                { "ReportColCoverage", "Coverage (%)" },
                { "ReportColPlane", "Plane" },
                { "ReportCopyBtn", "📋 Copy Report" },
                { "ReportSaveBtn", "💾 Save Report (.md)" },

                // System Font Selector Dialog
                { "SysFontTitle", "Select System CJK Font" },
                { "SysFontSelectBtn", "Select This Font" },
                { "SysFontNotFound", "No common system CJK fonts found." },

                // Status Bar & Tooltips
                { "StatusReady", "Ready" },
                { "StatusUnihanTotalFmt", "Unihan Database: {0:N0} characters" },
                { "StatusSelectedCharFmt", "Character: {0} ({1})" },
                { "StatusSearching", "Searching Unihan database & Online API..." },
                { "StatusFoundOnlineFmt", "Found {0} results (Unihan: {1} | Online API: {2})" },
                { "StatusFoundLocalFmt", "Found {0} results (Local Unihan) [Online API: Completed]" },
                { "StatusSearchCompleteFmt", "Search complete ({0} results)" },
                { "StatusCopiedCharFmt", "Copied to clipboard: {0}" },
                { "StatusCopiedHexFmt", "Copied hex code: {0}" },
                { "StatusCopiedBitmapFmt", "Copied 512x512 bitmap of {0} to clipboard!" },
                { "StatusAddedBasketFmt", "Added {0} to character basket!" },
                { "StatusAlreadyInBasketFmt", "{0} is already in the basket." },
                { "StatusExportedSvgFmt", "Exported SVG vector: {0}" },
                { "StatusCopiedUrlFmt", "Source URL copied to clipboard: {0}" },
                { "StatusFontLoading", "Font: Loading..." },
                { "StatusFontCustom", "(Custom File)" },
                { "StatusFontSystem", "(System)" },
                { "StatusCompositeAuto", "[Auto Composite]" },
                { "StatusCompositeFixed", "[Fixed]" },
                { "StatusFontExtraFmt", " | {0} glyphs | {1} characters | em={2}" },

                // Restart Messages
                { "RestartMsgVi", "Đã thay đổi ngôn ngữ sang Tiếng Việt. Ứng dụng sẽ khởi động lại để áp dụng giao diện mới." },
                { "RestartTitleVi", "Đổi ngôn ngữ" },
                { "RestartMsgEn", "Language changed to English. The application will restart to apply the new interface." },
                { "RestartTitleEn", "Language Changed" },

                // Common Titles & Notifications
                { "SuccessTitle", "Success" },
                { "ErrorTitle", "Error" },
                { "WarningTitle", "Warning" },
                { "NoticeTitle", "Notice" },
                { "FontDragDropLoaded", "Successfully loaded font: {0}" },
                { "FontDragDropError", "Error loading font: {0}" },
                { "InvalidHexError", "Invalid Hex codepoint." },
                { "TipBlock", "Block:" },
                { "TipSinoVietnamese", "Sino-Vietnamese:" },
                { "TipNom", "Nom:" },
                { "TipRadicalStrokesFmt", "Radical: {0} ({1} strokes)" },
                { "StatusUnihanReadyFmt", "Unihan Database: {0:N0} characters ready" },
                { "DiagnosticsSuccessTitle", "Diagnostics Passed (19/19 Tests Passed)" },
                { "DiagnosticsErrorTitle", "Diagnostics Failed" },
                { "DiagnosticsReportHeader", "=== CJK PLAYGROUND SYSTEM DIAGNOSTICS REPORT ===\n\n" },

                // About Box Text
                { "AboutTitle", "About CJK Playground" },
                { "AboutMessageFmt", "CJK Playground (Version 1.0)\n\n" +
                    "A comprehensive toolkit for CJK & Vietnamese Nom character exploration, linguistic research, and typography design.\n\n" +
                    "• Authors & Sources:\n" +
                    "  ✦ Author & Developer: Nguyễn Tuấn Vũ\n" +
                    "  ✦ GitHub: https://github.com/khanhvyNT | Facebook: https://www.facebook.com/oerlikon01\n" +
                    "  ✦ Data Sources:\n" +
                    "    - Han-Nom Dictionary: https://hvdic.thivien.net/\n" +
                    "    - Nom Na Tong Font: https://nomfoundation.org/ (Vietnamese Nôm Preservation Foundation)\n" +
                    "    - Unicode Consortium (Unihan): https://home.unicode.org/\n\n" +
                    "• Comprehensive Unicode Explorer: Browse extensive CJK and Nom ideographs across BMP, SIP (Plane 2), and TIP (Plane 3).\n" +
                    "• Multilingual Phonetic & Dictionary Search: Cross-reference readings in Sino-Vietnamese, Nom, Pinyin, Japanese, and Korean.\n" +
                    "• IDS Composition Studio: Visually assemble and refine composite ideographs with offline optical layout beautification.\n" +
                    "• Collection & Vector Export: Manage character baskets, private glyph registries (PUA), and export clean SVG vector graphics.\n" +
                    "• Modern & Bilingual Experience: Responsive native performance with complete Vietnamese and English interface support." }
            };
            Strings[SupportedLanguage.English] = d;
        }
    }
}
