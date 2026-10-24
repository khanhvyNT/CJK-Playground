using System;
using System.Collections.Generic;
using System.Linq;

namespace CJKPlayground.Services
{
    public enum UnicodeTier
    {
        Bmp,
        ExtA,
        ExtB
    }

    public class ExemplarSeed
    {
        public int CodePoint { get; set; }
        public string Character { get; set; }
        public string IdsDecomp { get; set; }
        public string Operator { get; set; }
        public string MorphologicalRole { get; set; }
        public UnicodeTier Tier { get; set; }
        public string[] Components { get; set; }
        public float MatchScore { get; set; }

        public ExemplarSeed(int cp, string ids, string op, string role, UnicodeTier tier = UnicodeTier.Bmp, string[] components = null)
        {
            CodePoint = cp;
            Character = char.ConvertFromUtf32(cp);
            IdsDecomp = ids;
            Operator = op;
            MorphologicalRole = role;
            Tier = tier;
            if (components != null)
            {
                Components = components;
            }
            else
            {
                var list = new List<string>();
                var tee = System.Globalization.StringInfo.GetTextElementEnumerator(ids);
                int idx = 0;
                while (tee.MoveNext())
                {
                    string el = tee.GetTextElement();
                    if (idx > 0) list.Add(el);
                    idx++;
                }
                Components = list.ToArray();
            }
        }

        public ExemplarSeed Clone()
        {
            return new ExemplarSeed(CodePoint, IdsDecomp, Operator, MorphologicalRole, Tier, Components)
            {
                MatchScore = this.MatchScore
            };
        }
    }

    /// <summary>
    /// Bảng hạt giống nguyên mẫu tham chiếu đa tầng (BMP -> Ext-A -> Ext-B)
    /// Hỗ trợ tìm kiếm chuyên sâu theo vị trí và giải phẫu thực nghiệm.
    /// </summary>
    public static class ExemplarSeeds
    {
        private static readonly object SyncLock = new object();
        private static List<ExemplarSeed> _currentPool;

        static ExemplarSeeds()
        {
            ResetToDefaults();
        }

        public static void ClearForAblation()
        {
            lock (SyncLock)
            {
                _currentPool = new List<ExemplarSeed>();
                HanIdsCorpusIndex.IsAblationMode = true;
            }
        }

        public static void ResetToDefaults()
        {
            lock (SyncLock)
            {
                HanIdsCorpusIndex.IsAblationMode = false;
                _currentPool = new List<ExemplarSeed>
                {
                    // =========================================================================
                    // 1. TOÁN TỬ ⿱ (Trên - Dưới) — TẦNG 1: BMP
                    // =========================================================================
                    // Hốc lõm đáy + Đế ngang rộng
                    new ExemplarSeed(0x541E, "⿱天口", "⿱", "cavity_base", UnicodeTier.Bmp, new[] { "天", "口" }),     // 吞
                    new ExemplarSeed(0x8695, "⿱天虫", "⿱", "cavity_base", UnicodeTier.Bmp, new[] { "天", "虫" }),     // 蚕
                    new ExemplarSeed(0x606D, "⿱天小", "⿱", "cavity_base", UnicodeTier.Bmp, new[] { "天", "小" }),     // 忝
                    new ExemplarSeed(0x660A, "⿱日天", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "日", "天" }),    // 昊
                    new ExemplarSeed(0x5168, "⿱人王", "⿱", "cavity_base", UnicodeTier.Bmp, new[] { "人", "王" }),     // 全
                    new ExemplarSeed(0x5802, "⿱尚土", "⿱", "wide_base", UnicodeTier.Bmp, new[] { "尚", "土" }),       // 堂
                    new ExemplarSeed(0x81F3, "⿱一土", "⿱", "wide_base", UnicodeTier.Bmp, new[] { "一", "土" }),       // 至
                    new ExemplarSeed(0x575A, "⿱臤土", "⿱", "wide_base", UnicodeTier.Bmp, new[] { "臤", "土" }),       // 坚
                    new ExemplarSeed(0x91D1, "⿱亼王", "⿱", "wide_base", UnicodeTier.Bmp, new[] { "亼", "王" }),       // 金

                    // Mái che rộng (Roof shelter)
                    new ExemplarSeed(0x5B57, "⿱宀子", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "宀", "子" }),    // 字
                    new ExemplarSeed(0x5BB6, "⿱宀豕", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "宀", "豕" }),    // 家
                    new ExemplarSeed(0x5B89, "⿱宀女", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "宀", "女" }),    // 安
                    new ExemplarSeed(0x5B9A, "⿱宀疋", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "宀", "疋" }),    // 定
                    new ExemplarSeed(0x82B1, "⿱艹化", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "艹", "化" }),    // 花
                    new ExemplarSeed(0x82D7, "⿱艹田", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "艹", "田" }),    // 苗
                    new ExemplarSeed(0x8349, "⿱艹早", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "艹", "早" }),    // 草
                    new ExemplarSeed(0x96F2, "⿱雨云", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "雨", "云" }),    // 雲
                    new ExemplarSeed(0x96F7, "⿱雨田", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "雨", "田" }),    // 雷
                    new ExemplarSeed(0x96EA, "⿱雨彐", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "雨", "彐" }),    // 雪
                    new ExemplarSeed(0x96FB, "⿱雨申", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "雨", "申" }),    // 電
                    new ExemplarSeed(0x9732, "⿱雨路", "⿱", "roof_shelter", UnicodeTier.Bmp, new[] { "雨", "路" }),    // 露

                    // Đối xứng / Tương đương mật độ
                    new ExemplarSeed(0x660C, "⿱日日", "⿱", "balanced_density", UnicodeTier.Bmp, new[] { "日", "日" }),// 昌
                    new ExemplarSeed(0x708E, "⿱火火", "⿱", "balanced_density", UnicodeTier.Bmp, new[] { "火", "火" }),// 炎
                    new ExemplarSeed(0x601D, "⿱田心", "⿱", "balanced_density", UnicodeTier.Bmp, new[] { "田", "心" }),// 思
                    new ExemplarSeed(0x60F3, "⿱相心", "⿱", "balanced_density", UnicodeTier.Bmp, new[] { "相", "心" }),// 想
                    new ExemplarSeed(0x666F, "⿱日京", "⿱", "balanced_density", UnicodeTier.Bmp, new[] { "日", "京" }),// 景
                    new ExemplarSeed(0x661F, "⿱日生", "⿱", "balanced_density", UnicodeTier.Bmp, new[] { "日", "生" }),// 星
                    new ExemplarSeed(0x65E9, "⿱日十", "⿱", "balanced_density", UnicodeTier.Bmp, new[] { "日", "十" }),// 早
                    new ExemplarSeed(0x97F3, "⿱立日", "⿱", "wide_base", UnicodeTier.Bmp, new[] { "立", "日" }),       // 音
                    new ExemplarSeed(0x7AE0, "⿱立早", "⿱", "wide_base", UnicodeTier.Bmp, new[] { "立", "早" }),       // 章
                    new ExemplarSeed(0x7AE5, "⿱立里", "⿱", "wide_base", UnicodeTier.Bmp, new[] { "立", "里" }),       // 童

                    // =========================================================================
                    // 2. TOÁN TỬ ⿱ (Trên - Dưới) — TẦNG 2 & 3: Ext-A & Ext-B (Plane 2 / Chữ Nôm)
                    // =========================================================================
                    new ExemplarSeed(0x342E, "⿱亠衣", "⿱", "roof_shelter", UnicodeTier.ExtA, new[] { "亠", "衣" }),    // 㐮
                    new ExemplarSeed(0x346D, "⿱夶月", "⿱", "balanced_density", UnicodeTier.ExtA, new[] { "夶", "月" }),// 㑭
                    new ExemplarSeed(0x3551, "⿱厂壴", "⿱", "roof_shelter", UnicodeTier.ExtA, new[] { "厂", "壴" }),    // 㕑
                    new ExemplarSeed(0x215F6, "⿱天上", "⿱", "cavity_base", UnicodeTier.ExtB, new[] { "天", "上" }),   // 𡗶 (Chữ Nôm: Trời)
                    new ExemplarSeed(0x215F8, "⿱天王", "⿱", "cavity_base", UnicodeTier.ExtB, new[] { "天", "王" }),   // 𡗸 (Chữ Nôm: Vua)
                    new ExemplarSeed(0x215F7, "⿱天父", "⿱", "cavity_base", UnicodeTier.ExtB, new[] { "天", "父" }),   // 𡗷 (Chữ Nôm: Chúa Cha)
                    new ExemplarSeed(0x2162F, "⿱大山", "⿱", "wide_base", UnicodeTier.ExtB, new[] { "大", "山" }),     // 𡘯 (Chữ Nôm: Lớn)
                    new ExemplarSeed(0x20B20, "⿱一末", "⿱", "wide_base", UnicodeTier.ExtB, new[] { "一", "末" }),     // 𠬠 (Chữ Nôm: Một)
                    new ExemplarSeed(0x20000, "⿱一丨", "⿱", "balanced_density", UnicodeTier.ExtB, new[] { "一", "丨" }),// 𠀀
                    new ExemplarSeed(0x20001, "⿱丨一", "⿱", "balanced_density", UnicodeTier.ExtB, new[] { "丨", "一" }),// 𠀁

                    // =========================================================================
                    // 3. TOÁN TỬ ⿰ (Trái - Phải) — TẦNG 1: BMP
                    // =========================================================================
                    // Trái bộ hẹp + Phải phức tạp
                    new ExemplarSeed(0x6E05, "⿰氵青", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "氵", "青" }),     // 清
                    new ExemplarSeed(0x6CB3, "⿰氵可", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "氵", "可" }),     // 河
                    new ExemplarSeed(0x6D77, "⿰氵每", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "氵", "每" }),     // 海
                    new ExemplarSeed(0x6C5F, "⿰氵工", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "氵", "工" }),     // 江
                    new ExemplarSeed(0x6D0B, "⿰氵羊", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "氵", "羊" }),     // 洋
                    new ExemplarSeed(0x6E56, "⿰氵胡", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "氵", "胡" }),     // 湖
                    new ExemplarSeed(0x8A9E, "⿰言吾", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "言", "吾" }),     // 語
                    new ExemplarSeed(0x8A71, "⿰言舌", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "言", "舌" }),     // 話
                    new ExemplarSeed(0x8AAA, "⿰言兌", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "言", "兌" }),     // 說
                    new ExemplarSeed(0x6A39, "⿰木尌", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "木", "尌" }),     // 樹
                    new ExemplarSeed(0x4F11, "⿰亻木", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "亻", "木" }),     // 休
                    new ExemplarSeed(0x4F53, "⿰亻本", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "亻", "本" }),     // 体
                    new ExemplarSeed(0x4FE1, "⿰亻言", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "亻", "言" }),     // 信
                    new ExemplarSeed(0x4F55, "⿰亻可", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "亻", "可" }),     // 何
                    new ExemplarSeed(0x4ED6, "⿰亻也", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "亻", "也" }),     // 他
                    new ExemplarSeed(0x4F5C, "⿰亻乍", "⿰", "narrow_left", UnicodeTier.Bmp, new[] { "亻", "乍" }),     // 作

                    // Cả hai khối phức tạp (Sx >= 3.5, Dual complex)
                    new ExemplarSeed(0x8B70, "⿰言義", "⿰", "dual_complex", UnicodeTier.Bmp, new[] { "言", "義" }),    // 議
                    new ExemplarSeed(0x8B80, "⿰言賣", "⿰", "dual_complex", UnicodeTier.Bmp, new[] { "言", "賣" }),    // 讀
                    new ExemplarSeed(0x9DB4, "⿰隺鳥", "⿰", "dual_complex", UnicodeTier.Bmp, new[] { "隺", "鳥" }),    // 鶴
                    new ExemplarSeed(0x660E, "⿰日月", "⿰", "balanced_density", UnicodeTier.Bmp, new[] { "日", "月" }),// 明
                    new ExemplarSeed(0x6797, "⿰木木", "⿰", "balanced_density", UnicodeTier.Bmp, new[] { "木", "木" }),// 林
                    new ExemplarSeed(0x76F8, "⿰木目", "⿰", "balanced_density", UnicodeTier.Bmp, new[] { "木", "目" }),// 相

                    // Phải bộ hẹp (Narrow right)
                    new ExemplarSeed(0x5229, "⿰禾刂", "⿰", "narrow_right", UnicodeTier.Bmp, new[] { "禾", "刂" }),    // 利
                    new ExemplarSeed(0x90E8, "⿰咅阝", "⿰", "narrow_right", UnicodeTier.Bmp, new[] { "咅", "阝" }),    // 部
                    new ExemplarSeed(0x982D, "⿰豆頁", "⿰", "narrow_right", UnicodeTier.Bmp, new[] { "豆", "頁" }),    // 頭

                    // =========================================================================
                    // 4. TOÁN TỬ ⿰ (Trái - Phải) — TẦNG 2 & 3: Ext-A & Ext-B (Plane 2 / Chữ Nôm)
                    // =========================================================================
                    new ExemplarSeed(0x3400, "⿰丘一", "⿰", "narrow_right", UnicodeTier.ExtA, new[] { "丘", "一" }),    // 㐀
                    new ExemplarSeed(0x342C, "⿰亻也", "⿰", "narrow_left", UnicodeTier.ExtA, new[] { "亻", "也" }),     // 㐌
                    new ExemplarSeed(0x3464, "⿰亻苗", "⿰", "narrow_left", UnicodeTier.ExtA, new[] { "亻", "苗" }),     // 㑤
                    new ExemplarSeed(0x3465, "⿰亻易", "⿰", "narrow_left", UnicodeTier.ExtA, new[] { "亻", "易" }),     // 㑥
                    new ExemplarSeed(0x34E4, "⿰吉刂", "⿰", "narrow_right", UnicodeTier.ExtA, new[] { "吉", "刂" }),    // 㓤
                    new ExemplarSeed(0x3565, "⿰以口", "⿰", "narrow_right", UnicodeTier.ExtA, new[] { "以", "口" }),    // 㕥
                    new ExemplarSeed(0x21A38, "⿰宁字", "⿰", "narrow_left", UnicodeTier.ExtB, new[] { "宁", "字" }),   // 𡨸 (Chữ Nôm: Chữ)
                    new ExemplarSeed(0x21982, "⿰字宁", "⿰", "narrow_right", UnicodeTier.ExtB, new[] { "字", "宁" }),  // 𡦂 (Chữ Nôm: Chữ dị thể)
                    new ExemplarSeed(0x27D51, "⿰貝巨", "⿰", "dual_complex", UnicodeTier.ExtB, new[] { "貝", "巨" }),  // 𧵑 (Chữ Nôm: Của)
                    new ExemplarSeed(0x236E7, "⿰木牢", "⿰", "narrow_left", UnicodeTier.ExtB, new[] { "木", "牢" }),   // 𣛧 (Chữ Nôm: Trầu)
                    new ExemplarSeed(0x21883, "⿰女母", "⿰", "narrow_left", UnicodeTier.ExtB, new[] { "女", "母" }),   // 𡢃 (Chữ Nôm: Mẹ)
                    new ExemplarSeed(0x210C4, "⿰口伯", "⿰", "narrow_left", UnicodeTier.ExtB, new[] { "口", "伯" }),   // 𡃄 (Chữ Nôm: Bác)
                    new ExemplarSeed(0x20325, "⿰亻龍", "⿰", "narrow_left", UnicodeTier.ExtB, new[] { "亻", "龍" }),   // 𠌥
                    new ExemplarSeed(0x20340, "⿰亻國", "⿰", "narrow_left", UnicodeTier.ExtB, new[] { "亻", "國" }),   // 𠍀
                    new ExemplarSeed(0x20345, "⿰亻壽", "⿰", "narrow_left", UnicodeTier.ExtB, new[] { "亻", "壽" }),   // 𠍅

                    // =========================================================================
                    // 5. TOÁN TỬ 3 NGÔI (⿲ / ⿳)
                    // =========================================================================
                    new ExemplarSeed(0x8857, "⿲彳圭亍", "⿲", "ternary_horiz", UnicodeTier.Bmp, new[] { "彳", "圭", "亍" }), // 街
                    new ExemplarSeed(0x8B1D, "⿲言身寸", "⿲", "ternary_horiz", UnicodeTier.Bmp, new[] { "言", "身", "寸" }), // 謝
                    new ExemplarSeed(0x73ED, "⿲王刂王", "⿲", "ternary_horiz", UnicodeTier.Bmp, new[] { "王", "刂", "王" }), // 班
                    new ExemplarSeed(0x8FA8, "⿲辛刂辛", "⿲", "ternary_horiz", UnicodeTier.Bmp, new[] { "辛", "刂", "辛" }), // 辨
                    new ExemplarSeed(0x8FAF, "⿲辛言辛", "⿲", "ternary_horiz", UnicodeTier.Bmp, new[] { "辛", "言", "辛" }), // 辯
                    new ExemplarSeed(0x610F, "⿳立日心", "⿳", "ternary_vert", UnicodeTier.Bmp, new[] { "立", "日", "心" }),  // 意
                    new ExemplarSeed(0x7FFC, "⿳羽田共", "⿳", "ternary_vert", UnicodeTier.Bmp, new[] { "羽", "田", "共" }),  // 翼
                    new ExemplarSeed(0x6025, "⿳⺈彐心", "⿳", "ternary_vert", UnicodeTier.Bmp, new[] { "⺈", "彐", "心" }),  // 急
                    new ExemplarSeed(0x7231, "⿳爫冖友", "⿳", "ternary_vert", UnicodeTier.Bmp, new[] { "爫", "冖", "友" }),  // 爱

                    // =========================================================================
                    // 6. CÁC TOÁN TỬ BAO QUANH (Enclosure)
                    // =========================================================================
                    new ExemplarSeed(0x570B, "⿴囗或", "⿴", "enclosure_full", UnicodeTier.Bmp, new[] { "囗", "或" }),  // 國
                    new ExemplarSeed(0x56FA, "⿴囗古", "⿴", "enclosure_full", UnicodeTier.Bmp, new[] { "囗", "古" }),  // 固
                    new ExemplarSeed(0x5712, "⿴囗袁", "⿴", "enclosure_full", UnicodeTier.Bmp, new[] { "囗", "袁" }),  // 園
                    new ExemplarSeed(0x5713, "⿴囗員", "⿴", "enclosure_full", UnicodeTier.Bmp, new[] { "囗", "員" }),  // 圓
                    new ExemplarSeed(0x56FD, "⿴囗玉", "⿴", "enclosure_full", UnicodeTier.Bmp, new[] { "囗", "玉" }),  // 国
                    new ExemplarSeed(0x9593, "⿵門日", "⿵", "enclosure_top", UnicodeTier.Bmp, new[] { "門", "日" }),   // 間
                    new ExemplarSeed(0x554F, "⿵門口", "⿵", "enclosure_top", UnicodeTier.Bmp, new[] { "門", "口" }),   // 問
                    new ExemplarSeed(0x805E, "⿵門耳", "⿵", "enclosure_top", UnicodeTier.Bmp, new[] { "門", "耳" }),   // 聞
                    new ExemplarSeed(0x958B, "⿵門幵", "⿵", "enclosure_top", UnicodeTier.Bmp, new[] { "門", "幵" }),   // 開
                    new ExemplarSeed(0x9589, "⿵門才", "⿵", "enclosure_top", UnicodeTier.Bmp, new[] { "門", "才" }),   // 閉
                    new ExemplarSeed(0x540C, "⿵冂一口", "⿵", "enclosure_top", UnicodeTier.Bmp, new[] { "冂", "一口" }),// 同
                    new ExemplarSeed(0x51F6, "⿶凵乂", "⿶", "enclosure_bottom", UnicodeTier.Bmp, new[] { "凵", "乂" }),// 凶
                    new ExemplarSeed(0x51FD, "⿶凵水", "⿶", "enclosure_bottom", UnicodeTier.Bmp, new[] { "凵", "水" }),// 函
                    new ExemplarSeed(0x5340, "⿷匚品", "⿷", "enclosure_left", UnicodeTier.Bmp, new[] { "匚", "品" }),  // 區
                    new ExemplarSeed(0x5320, "⿷匚斤", "⿷", "enclosure_left", UnicodeTier.Bmp, new[] { "匚", "斤" }),  // 匠
                    new ExemplarSeed(0x533B, "⿷匚矢", "⿷", "enclosure_left", UnicodeTier.Bmp, new[] { "匚", "矢" }),  // 医
                    new ExemplarSeed(0x5EE3, "⿸广黄", "⿸", "enclosure_top_left", UnicodeTier.Bmp, new[] { "广", "黄" }),// 廣
                    new ExemplarSeed(0x5C45, "⿸尸古", "⿸", "enclosure_top_left", UnicodeTier.Bmp, new[] { "尸", "古" }),// 居
                    new ExemplarSeed(0x5C4B, "⿸尸至", "⿸", "enclosure_top_left", UnicodeTier.Bmp, new[] { "尸", "至" }),// 屋
                    new ExemplarSeed(0x5C3E, "⿸尸毛", "⿸", "enclosure_top_left", UnicodeTier.Bmp, new[] { "尸", "毛" }),// 尾
                    new ExemplarSeed(0x539F, "⿸厂泉", "⿸", "enclosure_top_left", UnicodeTier.Bmp, new[] { "厂", "泉" }),// 原
                    new ExemplarSeed(0x6C23, "⿹气米", "⿹", "enclosure_top_right", UnicodeTier.Bmp, new[] { "气", "米" }),// 氣
                    new ExemplarSeed(0x6C17, "⿹气乂", "⿹", "enclosure_top_right", UnicodeTier.Bmp, new[] { "气", "乂" }),// 気
                    new ExemplarSeed(0x53E5, "⿹勹口", "⿹", "enclosure_top_right", UnicodeTier.Bmp, new[] { "勹", "口" }),// 句
                    new ExemplarSeed(0x5305, "⿹勹巳", "⿹", "enclosure_top_right", UnicodeTier.Bmp, new[] { "勹", "巳" }),// 包
                    new ExemplarSeed(0x65EC, "⿹勹日", "⿹", "enclosure_top_right", UnicodeTier.Bmp, new[] { "勹", "日" }),// 旬
                    new ExemplarSeed(0x9053, "⿺辶首", "⿺", "enclosure_bottom_left", UnicodeTier.Bmp, new[] { "辶", "首" }),// 道
                    new ExemplarSeed(0x9032, "⿺辶隹", "⿺", "enclosure_bottom_left", UnicodeTier.Bmp, new[] { "辶", "隹" }),// 進
                    new ExemplarSeed(0x901A, "⿺辶甬", "⿺", "enclosure_bottom_left", UnicodeTier.Bmp, new[] { "辶", "甬" }),// 通
                    new ExemplarSeed(0x8D77, "⿺走己", "⿺", "enclosure_bottom_left", UnicodeTier.Bmp, new[] { "走", "己" }),// 起
                    new ExemplarSeed(0x8D85, "⿺走召", "⿺", "enclosure_bottom_left", UnicodeTier.Bmp, new[] { "走", "召" }),// 超
                    new ExemplarSeed(0x8D8A, "⿺走戉", "⿺", "enclosure_bottom_left", UnicodeTier.Bmp, new[] { "走", "戉" }),// 越
                    new ExemplarSeed(0x9B45, "⿺鬼未", "⿺", "enclosure_bottom_left", UnicodeTier.Bmp, new[] { "鬼", "未" }),// 魅

                    // =========================================================================
                    // 7. TOÁN TỬ ĐAN XEN (Overlaid ⿻)
                    // =========================================================================
                    new ExemplarSeed(0x5DEB, "⿻工从", "⿻", "overlaid", UnicodeTier.Bmp, new[] { "工", "从" }),        // 巫
                    new ExemplarSeed(0x6771, "⿻木日", "⿻", "overlaid", UnicodeTier.Bmp, new[] { "木", "日" }),        // 東
                    new ExemplarSeed(0x675F, "⿻木口", "⿻", "overlaid", UnicodeTier.Bmp, new[] { "木", "口" }),        // 束
                    new ExemplarSeed(0x723D, "⿻大爻", "⿻", "overlaid", UnicodeTier.Bmp, new[] { "大", "爻" }),        // 爽
                    new ExemplarSeed(0x672C, "⿻木一", "⿻", "overlaid", UnicodeTier.Bmp, new[] { "木", "一" })         // 本
                };
            }
        }

        private static string _excludedCharacter = null;

        public static string CurrentExcludedCharacter
        {
            get
            {
                lock (SyncLock)
                {
                    return _excludedCharacter;
                }
            }
        }

        public static IDisposable ScopedExclude(string character)
        {
            return new ScopedExclusion(character);
        }

        private class ScopedExclusion : IDisposable
        {
            private readonly string _prev;
            public ScopedExclusion(string character)
            {
                lock (SyncLock)
                {
                    _prev = _excludedCharacter;
                    _excludedCharacter = character;
                }
            }
            public void Dispose()
            {
                lock (SyncLock)
                {
                    _excludedCharacter = _prev;
                }
            }
        }

        public static List<ExemplarSeed> AllSeeds
        {
            get { return GetSeeds(null); }
        }

        public static List<ExemplarSeed> GetSeeds(string op)
        {
            lock (SyncLock)
            {
                var list = new List<ExemplarSeed>();
                foreach (var s in _currentPool)
                {
                    if (!string.IsNullOrEmpty(_excludedCharacter) && s.Character == _excludedCharacter)
                        continue;
                    if (string.IsNullOrEmpty(op) || s.Operator == op)
                    {
                        list.Add(s);
                    }
                }
                return list;
            }
        }

        public static float AutoCompareWithUserIds(
            ExemplarSeed seed,
            string userComponent,
            string op,
            int posIndex,
            MorphologicalProfile userProfile)
        {
            if (seed == null || seed.Operator != op) return 0f;

            float score = 50.0f;

            // 1. So khớp thành phần đơn lẻ theo đúng vị trí (Atomic Positional Match)
            if (seed.Components != null && posIndex >= 0 && posIndex < seed.Components.Length)
            {
                string seedPart = seed.Components[posIndex];
                if (string.Equals(seedPart, userComponent, StringComparison.Ordinal))
                {
                    score += 45.0f; // Khớp chính xác 100% cùng ký tự tại đúng vị trí vai trò
                }
                else if (!string.IsNullOrEmpty(seedPart) && !string.IsNullOrEmpty(userComponent))
                {
                    if (seedPart.Contains(userComponent) || userComponent.Contains(seedPart))
                    {
                        score += 30.0f;
                    }
                }
            }

            // 2. So khớp hình thái học (Holistic Structural & Shape Match)
            if (userProfile != null)
            {
                if (userProfile.IsWideBase && (seed.MorphologicalRole == "wide_base" || seed.MorphologicalRole == "cavity_base"))
                    score += 15.0f;

                if (userProfile.IsRoof && seed.MorphologicalRole == "roof_shelter")
                    score += 15.0f;

                if (userProfile.IsNarrow && (seed.MorphologicalRole == "narrow_left" || seed.MorphologicalRole == "narrow_right"))
                    score += 15.0f;
            }

            return Math.Min(100.0f, Math.Max(0.0f, score));
        }

        /// <summary>
        /// Tìm kiếm vượt rào 3 tầng (BMP -> Ext-A -> Ext-B)
        /// Tối đa 10 trường hợp trùng khớp cao nhất, dừng sớm khi đủ điều kiện.
        /// </summary>
        public static List<ExemplarSeed> SearchTiered(
            string userComponent,
            string op,
            int posIndex,
            MorphologicalProfile userProfile,
            out UnicodeTier maxTierReached)
        {
            maxTierReached = UnicodeTier.Bmp;
            var candidates = new List<ExemplarSeed>();

            lock (SyncLock)
            {
                var pool = _currentPool.Where(s =>
                    s.Operator == op &&
                    (string.IsNullOrEmpty(_excludedCharacter) || s.Character != _excludedCharacter)).ToList();
                if (pool.Count == 0) return candidates;

                // TẦNG 1: Quét BMP
                var bmpSeeds = pool.Where(s => s.Tier == UnicodeTier.Bmp).ToList();
                foreach (var s in bmpSeeds)
                {
                    float sc = AutoCompareWithUserIds(s, userComponent, op, posIndex, userProfile);
                    if (sc >= 65.0f)
                    {
                        var clone = s.Clone();
                        clone.MatchScore = sc;
                        candidates.Add(clone);
                    }
                }

                int highQualityBmp = candidates.Count(c => c.MatchScore >= 90.0f);
                if (candidates.Count >= 10 || highQualityBmp >= 5)
                {
                    maxTierReached = UnicodeTier.Bmp;
                    return candidates.OrderByDescending(c => c.MatchScore).Take(10).ToList();
                }

                // TẦNG 2: Mở rộng quét Ext-A
                var extASeeds = pool.Where(s => s.Tier == UnicodeTier.ExtA).ToList();
                if (extASeeds.Count > 0)
                {
                    maxTierReached = UnicodeTier.ExtA;
                    foreach (var s in extASeeds)
                    {
                        float sc = AutoCompareWithUserIds(s, userComponent, op, posIndex, userProfile);
                        if (sc >= 65.0f)
                        {
                            var clone = s.Clone();
                            clone.MatchScore = sc;
                            candidates.Add(clone);
                        }
                    }
                }

                int highQualityExtA = candidates.Count(c => c.MatchScore >= 90.0f);
                if (candidates.Count >= 10 || highQualityExtA >= 5)
                {
                    return candidates.OrderByDescending(c => c.MatchScore).Take(10).ToList();
                }

                // TẦNG 3: Vượt rào sang Ext-B (Plane 2 / Chữ Nôm)
                var extBSeeds = pool.Where(s => s.Tier == UnicodeTier.ExtB).ToList();
                if (extBSeeds.Count > 0)
                {
                    maxTierReached = UnicodeTier.ExtB;
                    foreach (var s in extBSeeds)
                    {
                        float sc = AutoCompareWithUserIds(s, userComponent, op, posIndex, userProfile);
                        if (sc >= 60.0f)
                        {
                            var clone = s.Clone();
                            clone.MatchScore = sc;
                            candidates.Add(clone);
                        }
                    }
                }

                // Fallback nếu rỗng: lấy bất kỳ seed nào cùng operator
                if (candidates.Count == 0 && pool.Count > 0)
                {
                    foreach (var s in pool)
                    {
                        var clone = s.Clone();
                        clone.MatchScore = 60.0f;
                        candidates.Add(clone);
                    }
                }

                return candidates.OrderByDescending(c => c.MatchScore).Take(10).ToList();
            }
        }
    }
}
