# CJK Playground

Ứng dụng Windows native chuyên sâu để tra cứu chữ Hán, chữ Nôm, ghép chữ theo cấu trúc IDS, biên tập nét vector và nạp trực tiếp vào phông chữ TrueType.

Mình viết phần mềm này vì nhu cầu nghiên cứu chữ Hán - Nôm cần một công cụ gom trọn quy trình: vừa duyệt được toàn diện bảng mã Unicode (kể cả các khối mở rộng ở Plane 2 và Plane 3 nơi phần lớn chữ Nôm tọa lạc), vừa tra cứu tức thì âm Hán Việt/Nôm đa nguồn, vừa tự ghép và biên tập vector những chữ chưa có trong phông, và nạp thẳng chữ tự tạo vào phông chữ `.ttf` sẵn có để gõ thực tế trên máy tính.

Được phát triển hoàn toàn bằng **C# WinForms thuần trên .NET Framework 4.0**, đóng gói thành **một file thực thi duy nhất (`bin\CJKPlayground.exe` ~10 MB)**, chạy ngay không cần cài đặt thêm phần mềm phụ trợ, không phụ thuộc môi trường ngoài, không dùng Electron hay Web View nặng nề.

---

## Mục lục

- [Tính năng](#tính-năng)
  - [1. Bảng mã ký tự (Character Map)](#1-bảng-mã-ký-tự-character-map)
  - [2. Tra cứu âm đọc & Từ điển Hán Nôm](#2-tra-cứu-âm-đọc--từ-điển-hán-nôm)
  - [3. Ghép chữ IDS & Bộ công cụ đồ họa Vector (Illustrator Tools)](#3-ghép-chữ-ids--bộ-công-cụ-đồ-họa-vector-illustrator-tools)
  - [4. Lưu trực tiếp vào Font TrueType (Font Injection)](#4-lưu-trực-tiếp-vào-font-truetype-font-injection)
  - [5. Khu Vực Test Font & Hiệu chỉnh Spacing](#5-khu-vực-test-font--hiệu-chỉnh-spacing)
  - [6. Tiện ích mở rộng](#6-tiện-ích-mở-rộng)
- [Yêu cầu hệ thống](#yêu-cầu-hệ-thống)
- [Hướng dẫn biên dịch](#hướng-dẫn-biên-dịch)
- [Dòng lệnh & Kiểm thử tự động](#dòng-lệnh--kiểm-thử-tự-động)
- [Bảng phím tắt](#bảng-phím-tắt)
- [Cấu trúc mã nguồn](#cấu-trúc-mã-nguồn)
- [Nguồn dữ liệu](#nguồn-dữ-liệu)
- [Tác giả & Đóng góp](#tác-giả--đóng-góp)

---

## Tính năng

### 1. Bảng mã ký tự (Character Map)

- **Duyệt toàn diện theo khối Unicode:** Hỗ trợ duyệt đầy đủ CJK Unified Ideographs, Extensions A đến Ext I (bao phủ trọn vẹn Plane 2 & Plane 3 của chữ Nôm), 214 bộ thủ Khang Hy, IDC (Ideographic Description Characters), nét bút CJK, Kana, Hangul, Bopomofo, PUA (Private Use Area)...
- **Thanh đo độ phủ phông chữ (Coverage Bar):** Hiển thị trực quan tỷ lệ % ký tự mà phông chữ đang chọn hỗ trợ trên từng khối, giúp phát hiện ngay các vùng thiếu chữ (hiện tượng ô vuông / tofu).
- **Thanh tra thông tin chi tiết:** Bấm chọn bất kỳ ký tự nào để kiểm tra mã điểm Hex (`U+XXXX`), tên khối Unicode, số nét bút, bộ thủ chính, Glyph ID trong file phông, và các âm đọc liên quan.
- **Xuất dữ liệu nhanh chóng:** Sao chép ký tự trực tiếp vào clipboard, sao chép mã `U+XXXX`, xuất ảnh bitmap chất lượng cao hoặc xuất file vector SVG sắc nét.
- **Nạp phông chữ linh hoạt:** Mở trực tiếp các tệp `.ttf`, `.otf`, `.ttc` bên ngoài hoặc chọn từ danh sách phông hệ thống. Hỗ trợ cơ chế **Composite Font tự động** ghép nhiều phông hệ thống lại với nhau để bù đắp các ký tự bị khuyết.

### 2. Tra cứu âm đọc & Từ điển Hán Nôm

- **Tìm kiếm đa ngôn ngữ:** Tra theo âm Hán Việt, âm Nôm, Pinyin (hỗ trợ cả thanh điệu và số), tiếng Quảng Đông (Jyutping), tiếng Nhật (On/Kun/Romaji), tiếng Hàn (Hangul/Romaja), tra theo nghĩa tiếng Việt/Anh, theo bộ thủ, theo mã Hex hoặc dán trực tiếp ký tự vào ô tìm kiếm.
- **Xử lý tiếng Việt thông minh:** Cho phép gõ có dấu (`thiên`) hoặc không dấu (`thien`), tự động chuẩn hóa vị trí dấu thanh (`hoà` / `hòa`), tích hợp tùy chọn bắt buộc khớp đúng dấu thanh khi cần độ chính xác tuyệt đối.
- **Lọc chữ Nôm thuần:** Bộ lọc chuyên biệt giúp tách riêng các chữ Nôm bản địa không trùng với chữ Hán quy chuẩn.
- **Dữ liệu ngoại tuyến & Trực tuyến:** Đóng gói sẵn cơ sở dữ liệu Unihan Database và từ điển HVDic Thi Viện ngay trong tệp thực thi, giúp tra cứu offline tốc độ cao. Khi có kết nối mạng, ứng dụng tự động truy vấn mở rộng từ Thi Viện và Wiktionary tiếng Việt.

**Bảng cú pháp tiền tố tra cứu:**

| Tiền tố | Phạm vi tra cứu | Ví dụ |
|---|---|---|
| `v:` | Âm Hán Việt | `v:thiên` |
| `nom:` | Âm Nôm thuần | `nom:trời` |
| `py:` hoặc `m:` | Phiên âm Pinyin | `py:tian` |
| `c:` hoặc `jy:` | Tiếng Quảng Đông (Jyutping) | `c:tin1` |
| `ja:` hoặc `j:` | Tiếng Nhật (On/Kun/Romaji) | `ja:ten` |
| `ko:` hoặc `k:` | Tiếng Hàn (Hangul/Romaja) | `ko:cheon` |
| `d:` | Giải nghĩa từ điển | `d:trời` |
| `r:` | Số thứ tự bộ thủ Khang Hy | `r:37` (bộ Đại) |
| `u:` | Mã điểm Unicode | `u:5929` hoặc `U+5929` |
| `ids:` | Cấu trúc IDS | `ids:⿱天上` hoặc `⿰日月` |

### 3. Ghép chữ IDS & Bộ công cụ đồ họa Vector (Illustrator Tools)

- **Ghép chữ theo cấu trúc IDS:** Hỗ trợ đầy đủ 12 toán tử IDC (`⿰ ⿱ ⿲ ⿳ ⿴ ⿵ ⿶ ⿷ ⿸ ⿹ ⿺ ⿻`), cho phép lồng ghép đa tầng không giới hạn độ sâu.
- **Biến thể bộ thủ thông minh:** Tự động hoán đổi hình thái bộ thủ phù hợp với vị trí xuất hiện (ví dụ: `水` đổi thành `氵` khi đứng bên trái, `手` thành `扌`, `心` thành `忄`...).
- **Làm đẹp bố cục tự động (Auto-Beautify):** Công cụ phân tích quang học tự động điều chỉnh tỷ lệ khung, khoảng cách cách ly và độ đậm nhạt của nét nhằm tạo ra cấu trúc chữ hài hòa nhất.
- **Bộ công cụ biên tập Vector chuyên nghiệp:**
  - **Chọn và biến đổi đa thành phần (Multi-Selection Transform):** Đè phím `Ctrl` nhấp chọn hoặc quét vùng chuột để chọn nhiều nét/ký tự đồng thời; di chuyển, xoay, hoặc phóng to/thu nhỏ cả cụm cùng lúc.
  - **Co giãn 1 hướng độc lập:** Giữ phím `Alt` khi kéo núm điều khiển để co giãn tỷ lệ 1 chiều duy nhất (thay vì scale đối xứng qua tâm).
  - **Đồng bộ độ nhạy chuột 1:1:** Thao tác kéo thả và scale khóa bám chính xác theo chuyển động con trỏ chuột, dải biên độ mở rộng `[-800, 800]` px không bị kịch trần.
  - **Dao cắt nét (Knife `C`) & Cắt Lasso tự do (`L`):** Cắt xẻ từng nét hoặc khoanh vùng bóc tách một phần nét phức tạp ra khỏi chữ cái.
  - **Ghép nối nét (`Ctrl + J`):** Hợp nhất các đường viền nét vector với thuật toán Winding Rule chuẩn xác.
  - **Bút vẽ vector (`P`) & Sửa điểm neo (`A`):** Tự do vẽ thêm nét mới hoặc nắn chỉnh từng điểm neo Bezier.
  - **Lưới căn chỉnh chuyên dụng:** Hỗ trợ lưới Mễ tự cách, Cửu cung cách và hệ thống đường gióng thông minh (Smart Guides / Snapping).
  - **Bảng điều khiển Inspector chuyên sâu:** Dịch trục X/Y, Scale X/Y, Bù độ đậm nét (Stroke Dilate), Xoay nét tự do, Cắt mặt nạ nửa trên/dưới/trái/phải (Slice Mask), và chế độ Aspect Guard bảo vệ nét mỏng.
  - **Dải kiểm thử độ nét đa kích cỡ (Legibility Strip):** Quan sát chữ tự ghép hiển thị tức thì ở các cỡ raster nhỏ: 16px, 24px, 32px, 48px, 64px.
- **Quản lý & Xuất bản:** Thư viện chữ mẫu dựng sẵn (Presets), lưu trữ vào Sổ bộ PUA cá nhân (`U+E000` trở đi), sao lưu/phục hồi qua file JSON, xuất ảnh PNG đa độ phân giải (128 đến 2048 px) hoặc vector SVG trong suốt.

### 4. Lưu trực tiếp vào Font TrueType (Font Injection)

- **Ghi trực tiếp vào file phông `.ttf` nhị phân:** Nạp ngay glyph tự ghép hoặc chỉnh sửa vào phông chữ mà không cần đến các phần mềm tạo phông bên thứ ba như FontLab, Glyphs hay FontForge.
- **Gán vị trí mã Unicode tùy ý:** Cho phép nhúng chữ vào bảng mã cơ bản BMP, vùng mở rộng Plane 2 Chữ Nôm (Ext B đến Ext I), hoặc dải Private Use Area (`U+E000+`).
- **Tái tạo cấu trúc bảng OpenType nhị phân:** Tự động phân tích, cập nhật và tái tạo các bảng `glyf`, `loca`, `cmap` (hỗ trợ cả Format 4 cho 16-bit BMP và Format 12 cho 32-bit Supplementary Planes), `hmtx`, `maxp`, `head`, đồng thời tính toán lại bảng mã kiểm tra checksum toàn file.
- **An toàn tuyệt đối:** Cơ chế tự động sao lưu bản dự phòng (`.bak`) trước khi cập nhật hoặc xuất thành một tệp phông chữ mới riêng biệt.
- **Nạp hàng loạt (Batch Injection):** Nạp toàn bộ sổ bộ chữ PUA đã tích lũy vào file phông chỉ với một cú nhấp chuột.

### 5. Khu Vực Test Font & Hiệu chỉnh Spacing

- **Kiểm thử trực tiếp trên dòng văn bản:** Không gian gõ văn bản tự do để đánh giá độ hài hòa của phông chữ vừa nạp khi xếp cạnh các chữ chuẩn.
- **Điều chỉnh khoảng cách ký tự (Tracking):** Tinh chỉnh khoảng cách giữa các chữ cái trên toàn bộ đoạn văn (`-50` đến `+100` px).
- **Bộ điều khiển Spacing chuyên biệt cho vùng PUA (`≥ U+E000`):**
  - **Lề trái (LSB - Left Side Bearing):** Điều chỉnh điểm đặt nét ban đầu của ký tự PUA (`-100` đến `+100` px).
  - **Lề phải (RSB - Right Side Bearing):** Điều chỉnh độ rộng bước tiến (Advance Width) của ký tự PUA (`-100` đến `+100` px).
  - **Căn lề dọc (Baseline Shift):** Nâng cao hoặc hạ thấp ký tự PUA so với đường chân chữ CJK (`-50` đến `+50` px), đảm bảo ký tự tự ghép không bị lệch dòng hay thụt xuống.
  - **Đặt lại nhanh:** Phím tắt đặt lại toàn bộ thông số PUA về 0 chỉ với 1 click.
- **Chế độ so sánh song song (Compare Mode):** So sánh trực tiếp văn bản giữa phông đang biên tập và phông chữ tham chiếu trên cùng màn hình.

### 6. Tiện ích mở rộng

- **Giỏ ký tự (Character Basket):** Lưu trữ tạm thời các ký tự quan tâm trong lúc duyệt bảng mã, xuất ra tệp danh sách `.txt` hoặc đóng gói toàn bộ vector SVG thành file nén `.zip`.
- **Báo cáo độ phủ phông chữ (Font Coverage Report):** Thống kê chi tiết phần trăm hỗ trợ theo từng khối Unicode, xuất định dạng báo cáo Markdown chuyên nghiệp.
- **Giao diện song ngữ:** Chuyển đổi linh hoạt giữa Tiếng Việt và Tiếng Anh.

---

## Yêu cầu hệ thống

- **Hệ điều hành:** Windows 7, 8, 8.1, 10, 11 (hỗ trợ cả bản 32-bit và 64-bit).
- **Môi trường thực thi:** Microsoft .NET Framework 4.0 trở lên (đã được tích hợp sẵn mặc định trên Windows 10 và Windows 11).
- **Phông chữ khuyến nghị:** Cài đặt thêm các phông chữ mở rộng chữ Nôm như **Nom Na Tong** hoặc **SimSun-ExtB** để hiển thị trọn vẹn các ký tự ở Plane 2 & Plane 3.

---

## Hướng dẫn biên dịch

Dự án không yêu cầu cài đặt Visual Studio cồng kềnh. Bản dựng được biên dịch trực tiếp bằng trình biên dịch `csc.exe` có sẵn trong thư mục .NET Framework của Windows:

```cmd
cd CJKPlayground
build.bat
```

Sau khi chạy xong, kết quả thu được là một file thực thi độc lập duy nhất tại `bin\CJKPlayground.exe` (dung lượng khoảng 10 MB do đã đóng gói sẵn toàn bộ dữ liệu Unihan, từ điển HVDic và tài nguyên giao diện). Bạn có thể copy file này sang bất kỳ máy tính Windows nào để sử dụng ngay.

---

## Dòng lệnh & Kiểm thử tự động

Ứng dụng hỗ trợ các tham số dòng lệnh phục vụ kiểm thử và đánh giá hiệu năng:

```cmd
# Chạy toàn bộ 122 bài kiểm thử tự động, xuất báo cáo ra test_report.txt
bin\CJKPlayground.exe --test

# Chạy chấm điểm quang học bộ ghép chữ IDS, xuất bảng contact_sheet.png
bin\CJKPlayground.exe --benchmark

---

## Bảng phím tắt

### Cửa sổ chính

| Phím tắt | Chức năng |
|---|---|
| `Ctrl + O` | Mở tệp phông chữ (`.ttf`, `.otf`, `.ttc`) |
| `Ctrl + F` | Chuyển nhanh con trỏ đến ô tìm kiếm / tra cứu |
| `Ctrl + B` | Mở hộp thoại Giỏ ký tự |
| `Ctrl + S` | Xuất file vector SVG của ký tự đang chọn |
| `Ctrl + Shift + C` | Sao chép hình ảnh bitmap của ký tự vào Clipboard |
| `PgUp` / `PgDn` | Lật trang trước / trang sau trong bảng mã |
| `F1` | Chuyển đến tab Bảng mã ký tự |
| `F2` | Chuyển đến tab Tra cứu âm đọc & Từ điển |
| `F3` | Chuyển đến tab Ghép chữ IDS |
| `F4` | Chuyển đến tab Khu vực Test Font |

### Canvas Ghép chữ & Biên tập Vector

| Phím tắt | Chức năng |
|---|---|
| `Q` | Công cụ chọn & biến đổi thành phần (Selection Tool) |
| `A` | Công cụ chọn & nắn chỉnh điểm neo vector (Direct Selection Tool) |
| `P` | Công cụ bút vẽ vector tự do (Pen Tool) |
| `C` | Công cụ dao cắt nét thẳng (Knife Tool) |
| `L` | Công cụ cắt nét vùng chọn tự do (Lasso Cut Tool) |
| `Ctrl + Chuột` | Chọn nhiều nét / ký tự cùng lúc (Multi-Selection) |
| `Alt + Kéo núm` | Co giãn tỷ lệ 1 hướng duy nhất |
| `Ctrl + J` | Ghép nối nét (Join Strokes) theo thuật toán Winding Rule |
| `V` / `H` hoặc giữ `Space` | Kéo di chuyển toàn bộ Canvas (Hand / Pan Tool) |
| `Z` | Phóng to Canvas (`Alt` + click để thu nhỏ, hoặc lăn chuột) |
| `Ctrl + C` / `Ctrl + V` | Sao chép và dán nét / thành phần |
| `Ctrl + D` | Nhân đôi nét / thành phần được chọn |
| `Delete` | Xóa nét hoặc thành phần đang chọn |
| `Ctrl + Z` / `Ctrl + Y` | Hoàn tác (Undo) / Làm lại (Redo) thao tác biên tập |

---

## Cấu trúc mã nguồn

```
CJKPlayground/
├── build.bat                 Script biên dịch tự động bằng csc.exe (.NET 4.0)
├── app.manifest              Manifest cấu hình DPI-Awareness & quyền thực thi
├── app.ico, app.png          Biểu tượng đồ họa của ứng dụng
├── README.md                 Tài liệu hướng dẫn sử dụng và phát triển
├── bin/                      Thư mục chứa bản dựng ứng dụng
│   └── CJKPlayground.exe     File thực thi duy nhất (~10 MB, Single-file)
├── data/                     Dữ liệu tĩnh nhúng trong ứng dụng
│   ├── unihan-full.json      Cơ sở dữ liệu Unicode Unihan (âm đọc, số nét, bộ thủ)
│   ├── HVDic.wrd             Từ điển Hán Việt & Chữ Nôm Thi Viện
│   └── All HAN IDS.TXT       Bảng cấu trúc phân rã IDS toàn bộ chữ Hán
└── src/                      Toàn bộ mã nguồn C# WinForms
    ├── Program.cs            Điểm khởi chạy ứng dụng, xử lý CLI test & benchmark
    ├── Forms/                Giao diện các tab, hộp thoại và khung vẽ
    │   ├── MainForm.cs       Cửa sổ điều khiển chính & điều hướng các tab
    │   ├── IdsComposerControl.cs Khung vẽ ghép chữ IDS & công cụ vector Illustrator
    │   ├── FontTestControl.cs    Khu vực thử nghiệm font & hiệu chỉnh spacing PUA
    │   ├── FontEditorDialog.cs   Hộp thoại nạp glyph vào file font TTF
    │   ├── CharacterBasketForm.cs Hộp thoại giỏ thu gom ký tự
    │   └── FontReportForm.cs     Hộp thoại báo cáo độ phủ phông chữ
    ├── Models/               Cấu trúc dữ liệu ký tự, IDS, Unicode catalog
    ├── Services/             Xử lý nhị phân OpenType, cân chỉnh quang học, dựng vector
    │   ├── TrueTypeFontWriter.cs Ghi nhị phân các bảng glyf, loca, cmap, hmtx vào TTF
    │   ├── OpenTypeBinaryReader.cs Đọc trực tiếp cấu trúc nhị phân file font
    │   ├── IdsOpticalBeautifier.cs Bộ thuật toán cân chỉnh quang học bố cục IDS
    │   ├── IdsLayoutEngine.cs    Phân tích cây cú pháp IDS và chia lưới thành phần
    │   ├── HvDicDatabase.cs      Quản lý tra cứu dữ liệu từ điển offline
    │   └── LinguisticsUtils.cs   Xử lý chuẩn hóa tiếng Việt, Kana, Hangul
    └── Tests/                Hệ thống 122 bài kiểm thử tự động toàn diện
```

---

## Nguồn dữ liệu

- [Unicode Unihan Database](https://home.unicode.org/)
- Từ điển Hán Nôm [Thi Viện](https://hvdic.thivien.net/) (dữ liệu `HVDic.wrd` và cổng tra cứu trực tuyến)
- [Wiktionary tiếng Việt](https://vi.wiktionary.org/)
- Phông chữ Nom Na Tong phát triển bởi [Hội Bảo tồn di sản chữ Nôm Việt Nam](https://nomfoundation.org/)

Toàn bộ dữ liệu bản quyền thuộc về các tác giả và tổ chức nêu trên, được đóng gói vào phần mềm nhằm mục đích nghiên cứu phi thương mại và bảo tồn di sản chữ viết Hán - Nôm.

---

## Tác giả & Đóng góp

- **Tác giả:** Nguyễn Tuấn Vũ
- **Liên hệ:** [GitHub](https://github.com/khanhvyNT) · [Facebook](https://www.facebook.com/oerlikon01)

Nếu gặp lỗi trong quá trình sử dụng hoặc có ý tưởng đóng góp nâng cấp, hoan nghênh bạn mở Issue hoặc đóng góp Pull Request trên GitHub.
