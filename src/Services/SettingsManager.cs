using System;
using System.IO;
using System.Web.Script.Serialization;

namespace CJKPlayground.Services
{
    public class AppSettings
    {
        public string LastFontPath { get; set; }
        public string LastFontFamily { get; set; }
        public int LastStartCp { get; set; }
        public int LastEndCp { get; set; }
        public int LastPage { get; set; }
        public string Language { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool IsMaximized { get; set; }
        public bool AutoCompositeFont { get; set; }

        public AppSettings()
        {
            LastStartCp = 0x4E00;
            LastEndCp = 0x9FFF;
            LastPage = 0;
            Language = "vi";
            WindowWidth = 1280;
            WindowHeight = 840;
            IsMaximized = false;
            AutoCompositeFont = true;
        }
    }

    public static class SettingsManager
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CJKPlayground"
        );
        private static readonly string LegacySettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CJKPlayground",
            "settings.json"
        );
        private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                string targetPath = File.Exists(SettingsFilePath) ? SettingsFilePath : (File.Exists(LegacySettingsFilePath) ? LegacySettingsFilePath : null);
                if (targetPath != null && File.Exists(targetPath))
                {
                    string json = File.ReadAllText(targetPath);
                    var settings = Json.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                string json = Json.Serialize(settings);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }
}
