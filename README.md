# CJK Playground

Ứng dụng Windows để tra cứu chữ Hán, chữ Nôm và ghép chữ theo cấu trúc IDS.

Mình viết cái này vì cần một chỗ vừa xem được bảng mã Unicode (kể cả các khối mở rộng ở Plane 2, Plane 3 nơi phần lớn chữ Nôm nằm), vừa tra được âm Hán Việt/Nôm, vừa tự ghép thử những chữ chưa có trong font. Các công cụ sẵn có thì mỗi cái làm được một phần, nên gom lại thành một chương trình.

Viết bằng C# WinForms trên .NET Framework 4. Không cần cài thêm gì, không dùng web view.

## Tính năng

**Bảng mã ký tự**

- Duyệt theo khối Unicode: CJK Unified Ideographs, Ext A đến Ext I, bộ thủ Khang Hy, IDC, nét bút, Kana, Hangul, Bopomofo, PUA...
- Mỗi khối hiện tỉ lệ phủ của font đang chọn, để biết font nào thiếu chữ nào.
- Bấm vào một chữ để xem mã điểm, khối, số nét, bộ thủ, glyph ID, âm đọc.
- Sao chép ký tự, mã `U+XXXX`, ảnh bitmap, hoặc xuất SVG.
- Mở được file `.ttf`, `.otf`, `.ttc`, hoặc chọn font hệ thống. Có chế độ tự ghép nhiều font hệ thống lại để lấp chỗ thiếu.

**Tra cứu**

- Tìm theo âm Hán Việt, Nôm, Pinyin, Quảng Đông, Nhật, Hàn, theo nghĩa, theo mã hex, hoặc dán thẳng chữ vào.
- Gõ có dấu hay không dấu đều được (`thiên` và `thien`). Có tuỳ chọn bắt buộc đúng dấu thanh.
- Lọc riêng chữ Nôm thuần.
- Dữ liệu Unihan và từ điển HVDic được nhúng sẵn nên tra offline được. Khi có mạng thì lấy thêm từ Internet.

Cú pháp tiền tố:

| Tiền tố | Tìm trong |
|---|---|
| `v:` | âm Hán Việt |
| `nom:` | âm Nôm |
| `py:` hoặc `m:` | Pinyin |
| `c:` hoặc `jy:` | tiếng Quảng Đông |
| `ja:` hoặc `j:` | tiếng Nhật |
| `ko:` hoặc `k:` | tiếng Hàn |
| `d:` | nghĩa |
| `r:` | bộ thủ |
| `u:` | mã điểm (cũng nhận `U+5929`, `0x5929`) |
| `ids:` | cấu trúc IDS (hoặc gõ thẳng chuỗi có `⿰`, `⿱`...) |

Ví dụ: `v:thiên`, `nom:trời`, `py:tian`, `⿰日月`.

**Ghép chữ IDS**

- Nhập công thức với 12 toán tử IDC (`⿰ ⿱ ⿲ ⿳ ⿴ ⿵ ⿶ ⿷ ⿸ ⿹ ⿺ ⿻`), lồng nhau bao nhiêu tầng cũng được.
- Tự đổi bộ thủ sang dạng biến thể khi đứng ở vị trí phù hợp (ví dụ `水` thành `氵` ở bên trái).
- Nút "Làm đẹp bố cục" tự canh tỉ lệ, khoảng cách và độ dày nét giữa các thành phần. Kết quả không phải lúc nào cũng đúng như chữ in thật, nhất là với các kiểu bao bọc, nên vẫn có công cụ chỉnh tay.
- Chỉnh tay trên canvas: kéo, co giãn, xoay từng thành phần, sửa điểm neo, vẽ thêm nét bằng bút. Có lưới Mễ tự cách / Cửu cung cách và đường gióng.
- Thư viện chữ mẫu để thử nhanh.
- Lưu chữ tự ghép vào sổ riêng, gán mã PUA (`U+E000` trở đi), xuất/nhập file JSON.
- Xuất SVG hoặc PNG (128 đến 2048 px, nền trong suốt hoặc có màu).

**Khác**

- Giỏ ký tự: gom chữ trong lúc duyệt, xuất ra `.txt` hoặc nén toàn bộ SVG thành `.zip`.
- Báo cáo độ phủ font theo từng khối, lưu ra Markdown.
- Giao diện tiếng Việt và tiếng Anh.

## Yêu cầu

- Windows 7 trở lên
- .NET Framework 4.x (Windows 10/11 có sẵn)

Để hiển thị đầy đủ chữ Nôm ở Plane 2 nên cài thêm một font có phủ Ext B trở lên, ví dụ Nom Na Tong hoặc SimSun-ExtB.

## Build

Không cần Visual Studio. Script dùng trình biên dịch `csc.exe` có sẵn trong .NET Framework:

```cmd
cd CJKPlayground
build.bat
```

Kết quả là một file duy nhất `bin\CJKPlayground.exe` (khoảng 10 MB vì đã nhúng dữ liệu). Chạy file đó là xong.

## Dòng lệnh

```cmd
bin\CJKPlayground.exe --test        chạy toàn bộ test, ghi kết quả ra test_report.txt
bin\CJKPlayground.exe --benchmark   chấm điểm bộ ghép chữ, xuất contact_sheet.png
```

Hiện có 56 test chia làm bốn nhóm: xử lý ngôn ngữ, đọc font OpenType, từ điển HVDic, và bộ làm đẹp IDS.

## Phím tắt

Cửa sổ chính:

| Phím | Việc |
|---|---|
| `Ctrl+O` | mở file font |
| `Ctrl+F` | chuyển sang ô tìm kiếm |
| `Ctrl+B` | mở giỏ ký tự |
| `Ctrl+S` | xuất SVG chữ đang chọn |
| `Ctrl+Shift+C` | sao chép ảnh chữ đang chọn |
| `PgUp` / `PgDn` | lật trang bảng mã |
| `F1` | về tab bảng mã |

Canvas ghép chữ:

| Phím | Việc |
|---|---|
| `Q` | chọn và biến đổi (co giãn, xoay) |
| `A` | chọn điểm neo |
| `P` | bút vẽ (Enter hoặc nhấp đúp để kết thúc, Esc để huỷ) |
| `V` / `H` | kéo canvas (giữ `Space` cũng được) |
| `Z` | phóng to, `Alt` + nhấp để thu nhỏ |
| `Ctrl+C` / `Ctrl+V` / `Ctrl+D` | chép, dán, nhân đôi thành phần |
| `Delete` | xoá thành phần |

## Cấu trúc thư mục

```
CJKPlayground/
├── build.bat
├── app.manifest, app.ico, app.png
├── data/
│   ├── unihan-full.json     dữ liệu Unihan
│   ├── HVDic.wrd            từ điển Hán Việt
│   └── All HAN IDS.TXT      bảng phân rã IDS
└── src/
    ├── Program.cs
    ├── Forms/               giao diện
    ├── Models/
    ├── Services/            đọc font, tra cứu, phân tích và dựng IDS
    └── Tests/
```

Mấy file đáng xem nếu muốn đọc code:

- `Services/OpenTypeBinaryReader.cs`: đọc trực tiếp bảng `cmap` (format 4 và 12), `glyf`, `head`... từ file font, không qua GDI.
- `Services/IdsParserService.cs`, `IdsLayoutEngine.cs`: phân tích công thức IDS thành cây rồi chia khung cho từng thành phần.
- `Services/IdsOpticalBeautifier.cs`: phần canh chỉnh quang học, cũng là phần phức tạp nhất.
- `Services/LinguisticsUtils.cs`: bỏ dấu tiếng Việt, chuẩn hoá vị trí dấu (`hoà`/`hòa`), Hangul sang Romaja, Kana sang Romaji.

Cài đặt người dùng và sổ chữ tự ghép được lưu ở `%APPDATA%\CJKPlayground`.

## Nguồn dữ liệu

- [Unicode Unihan Database](https://home.unicode.org/)
- Từ điển Hán Nôm [Thi Viện](https://hvdic.thivien.net/) (file HVDic.wrd và tra cứu trực tuyến)
- [Wiktionary tiếng Việt](https://vi.wiktionary.org/)
- Font Nom Na Tong của [Hội Bảo tồn di sản chữ Nôm Việt Nam](https://nomfoundation.org/)

Dữ liệu thuộc về các nguồn trên, mình chỉ đóng gói lại để dùng offline.

## Tác giả

Nguyễn Tuấn Vũ. [GitHub](https://github.com/khanhvyNT) · [Facebook](https://www.facebook.com/oerlikon01)

Gặp lỗi hay có góp ý thì cứ mở issue.
