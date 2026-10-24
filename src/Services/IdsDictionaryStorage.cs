using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public static class IdsDictionaryStorage
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private static string GetFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "CJKPlayground");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return Path.Combine(dir, "settings_ids_dict.json");
        }

        private static string GetLegacyFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CJKPlayground", "settings_ids_dict.json");
        }

        public static List<SavedCustomGlyph> Load()
        {
            try
            {
                string path = GetFilePath();
                if (!File.Exists(path))
                {
                    string leg = GetLegacyFilePath();
                    if (File.Exists(leg)) path = leg;
                }

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var list = Json.Deserialize<List<SavedCustomGlyph>>(json);
                    if (list != null) return list;
                }
            }
            catch { }

            // Default initial glyphs if dictionary is empty
            var initial = new List<SavedCustomGlyph>
            {
                new SavedCustomGlyph
                {
                    Id = "glyph_default_troi",
                    Name = "Chữ Trời (𡗶)",
                    IdsString = "⿱天上",
                    PuaCode = "U+215F6",
                    Reading = "Trời",
                    Meaning = "Bầu trời, thượng đế trong quan niệm người Việt cổ.",
                    CreatedAt = DateTime.UtcNow.Ticks
                },
                new SavedCustomGlyph
                {
                    Id = "glyph_default_dien_ngon",
                    Name = "Chữ Điện Ngôn (⿰電言)",
                    IdsString = "⿰電言",
                    PuaCode = "U+E100",
                    Reading = "Điện Ngôn",
                    Meaning = "Chữ tự chế hợp thể từ Điện và Ngôn biểu thị tin nhắn điện tử.",
                    CreatedAt = DateTime.UtcNow.Ticks
                }
            };
            return initial;
        }

        public static void SaveAll(List<SavedCustomGlyph> list)
        {
            try
            {
                string path = GetFilePath();
                string json = Json.Serialize(list ?? new List<SavedCustomGlyph>());
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static void AddOrUpdate(SavedCustomGlyph glyph)
        {
            if (glyph == null) return;
            var list = Load();
            int idx = list.FindIndex(x => x.Id == glyph.Id || x.PuaCode.Equals(glyph.PuaCode, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                list[idx] = glyph;
            }
            else
            {
                list.Insert(0, glyph);
            }
            SaveAll(list);
        }

        public static void Remove(string glyphId)
        {
            if (string.IsNullOrEmpty(glyphId)) return;
            var list = Load();
            list.RemoveAll(x => x.Id == glyphId);
            SaveAll(list);
        }
    }
}
