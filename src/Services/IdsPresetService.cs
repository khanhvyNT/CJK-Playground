using System.Collections.Generic;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public static class IdsPresetService
    {
        public static readonly List<IdsPreset> Presets = new List<IdsPreset>
        {
            // Invented & Vietnamese Chữ Nôm
            new IdsPreset(
                "nom_troi",
                "Chữ Trời (𡗶)",
                "vietnamese_nom",
                "⿱天上",
                "𡗶",
                "U+215F6",
                "Trời",
                "Bầu trời, thượng đế trong quan niệm vũ trụ quan người Việt cổ (Thiên hợp Thượng).",
                "Heaven (𡗶)",
                "Sky, Heaven in ancient Vietnamese cosmology (Thien over Thuong)."
            ),
            new IdsPreset(
                "nom_chu",
                "Chữ 'Chữ' (𡨸)",
                "vietnamese_nom",
                "⿰宁字",
                "𡨸",
                "U+21A38",
                "Chữ",
                "Từ chỉ chữ viết trong cụm 'Chữ Nôm', ghép từ Ninh (âm) và Tự (nghĩa).",
                "Nom Word 'Chu' (𡨸)",
                "Denotes 'word' or 'script' in Chu Nom, composed of Ning (sound) and Zi (meaning)."
            ),
            new IdsPreset(
                "nom_me",
                "Chữ Mẹ (𡢲)",
                "vietnamese_nom",
                "⿰女⿱夭土",
                "𡢲",
                "U+218B2",
                "Mẹ",
                "Người mẹ sinh thành, ghép bộ Nữ và chữ Yểu/Thổ tạo âm Mẹ.",
                "Mother (𡢲)",
                "Mother in Chu Nom, combining radical Woman (Nu) with phonetic sound elements."
            ),
            new IdsPreset(
                "nom_loi",
                "Chữ Lời (𠳒)",
                "vietnamese_nom",
                "⿰口⿱爫⿰月夫",
                "𠳒",
                "U+20CD2",
                "Lời",
                "Lời nói, ngôn từ trong Chữ Nôm.",
                "Words / Speech (𠳒)",
                "Spoken words or speech in Chu Nom."
            ),
            new IdsPreset(
                "nom_nuoc",
                "Chữ Nước (渃)",
                "vietnamese_nom",
                "⿰氵若",
                "渃",
                "U+6E03",
                "Nước",
                "Sông nước, giang sơn đất nước Việt Nam.",
                "Water / Homeland (渃)",
                "Water, rivers, and the country of Vietnam."
            ),
            new IdsPreset(
                "nom_yeu_nuoc",
                "Chữ Yên Nước (𣷭)",
                "vietnamese_nom",
                "⿰氵安",
                "𣷭",
                "U+23DFD",
                "Yên / Nước lặng",
                "Vùng nước phẳng lặng, an bình.",
                "Calm Waters (𣷭)",
                "Still, tranquil water surface."
            ),
            new IdsPreset(
                "nom_ba",
                "Chữ Ba (吧)",
                "vietnamese_nom",
                "⿰口巴",
                "吧",
                "U+5427",
                "Ba",
                "Số ba hoặc tiếng gọi cha mẹ trong phương ngữ Nôm cổ.",
                "Three / Father (吧)",
                "Number three or colloquial address in ancient Nom."
            ),
            new IdsPreset(
                "syn_dien_ngon",
                "Chữ Điện Ngôn (⿰電言)",
                "vietnamese_nom",
                "⿰電言",
                "⿰電言",
                "U+E100",
                "Điện Ngôn",
                "Chữ tự chế hợp thể từ Điện (電) và Ngôn (言) tượng trưng cho thông tin liên lạc kỹ thuật số.",
                "Electric Speech (⿰電言)",
                "Synthetic ideograph of Electricity (Dian) and Speech (Yan) symbolizing digital communications."
            ),

            // CJK Classics
            new IdsPreset(
                "cjk_minh",
                "Chữ Minh (明 - Sáng tỏ)",
                "cjk_classic",
                "⿰日月",
                "明",
                "U+660E",
                "Minh",
                "Ánh sáng rực rỡ hợp bởi Mặt trời (Nhật) và Mặt trăng (Nguyệt).",
                "Bright (明)",
                "Brilliant illumination composed of Sun (Ri) and Moon (Yue)."
            ),
            new IdsPreset(
                "cjk_phan",
                "Chữ Phần (焚 - Thiêu đốt)",
                "cjk_classic",
                "⿱林火",
                "焚",
                "U+711A",
                "Phần",
                "Lửa cháy rừng thiêu rụi (Lâm trên Hỏa).",
                "Burn (焚)",
                "Forest fire burning (Lin over Huo)."
            ),
            new IdsPreset(
                "cjk_quoc",
                "Chữ Quốc (国 - Đất nước)",
                "cjk_classic",
                "⿴囗玉",
                "国",
                "U+56FD",
                "Quốc",
                "Ngọc quý được bao bọc cẩn mật bên trong thành trì kiên cố.",
                "Country (国)",
                "Precious jade safely enclosed within fortified city walls."
            ),
            new IdsPreset(
                "cjk_gian",
                "Chữ Gian (間 - Gian phòng)",
                "cjk_classic",
                "⿵門日",
                "間",
                "U+9593",
                "Gian",
                "Ánh mặt trời lọt qua khe cửa sổ.",
                "Room / Interval (間)",
                "Sunlight filtering through door gap."
            ),
            new IdsPreset(
                "cjk_ma",
                "Chữ Ma (麻 - Cây gai)",
                "cjk_classic",
                "⿸广林",
                "麻",
                "U+9EBB",
                "Ma",
                "Cây gai phơi dưới mái hiên nhà.",
                "Hemp (麻)",
                "Hemp stalks drying under house eaves."
            ),
            new IdsPreset(
                "cjk_khi",
                "Chữ Khí (気 - Khí sắc)",
                "cjk_classic",
                "⿹气米",
                "気",
                "U+6C17",
                "Khí",
                "Hơi bốc lên nuôi dưỡng sinh mệnh cơ thể.",
                "Qi / Vital Energy (気)",
                "Vapor rising to sustain life energy."
            ),
            new IdsPreset(
                "cjk_tien",
                "Chữ Tiến (進 - Tiến lên)",
                "cjk_classic",
                "⿺辶隹",
                "進",
                "U+9032",
                "Tiến",
                "Chim sẻ chỉ biết bay tiến về phía trước chứ không lùi.",
                "Advance (進)",
                "Birds fly forward and never retreat."
            ),
            new IdsPreset(
                "cjk_su",
                "Chữ Thụ (樹 - Cây cối)",
                "cjk_classic",
                "⿲木壴寸",
                "樹",
                "U+6A39",
                "Thụ",
                "Cấu trúc ba phần ngang: Mộc + Cổ + Thốn.",
                "Tree (樹)",
                "Tri-column structure: Mu + Gu + Cun."
            ),

            // Complex Monuments
            new IdsPreset(
                "monument_biang",
                "Chữ Biáng (𰻞 - 56 Nét)",
                "complex_monuments",
                "⿺辶⿳穴⿰月⿰⿲幺言幺長刂心",
                "𰻞",
                "U+30EDE",
                "Biáng",
                "Chữ Hán phức tạp bậc nhất đại diện cho món mì Biangbiang truyền thống Thiểm Tây.",
                "Biang (𰻞 - 56 Strokes)",
                "One of the most complex Chinese characters, representing traditional Shaanxi Biangbiang noodles."
            ),
            new IdsPreset(
                "monument_long",
                "Chữ Long Tứ Hợp (𪚥 - 64 Nét)",
                "complex_monuments",
                "⿱⿰龍龍⿰龍龍",
                "𪚥",
                "U+2A6A5",
                "Chiết",
                "Bốn con Rồng hội tụ, chữ nhiều nét nhất trong Khang Hy Tự Điển.",
                "Quad-Dragon (𪚥 - 64 Strokes)",
                "Four clustered Dragons, the character with the most strokes in the Kangxi Dictionary."
            )
        };
    }
}
