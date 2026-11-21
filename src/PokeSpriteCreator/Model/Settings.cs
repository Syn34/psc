using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PokeSpriteCreator.Json;

namespace PokeSpriteCreator.Model
{
    /// <summary>App settings, kept in %AppData%\PokeSpriteCreator\settings.json.</summary>
    public class Settings
    {
        public string PartsFolder;
        public List<string> Recent = new List<string>();
        public int LastGen = 3;
        public string LastKind = "Front";
        public int ExportScale = 2;
        public string LastExportFolder;
        public string LastProjectFolder;
        public bool ShowGrid = true;
        public bool DarkMode;
        public List<string> CustomColors = new List<string>();

        public static string Dir
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("PSC_SETTINGS_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokeSpriteCreator");
            }
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static string DefaultPartsFolder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Parts"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    var o = Json.Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JsonObject;
                    if (o != null)
                    {
                        s.PartsFolder = o.GetString("partsFolder");
                        s.LastGen = o.GetInt("lastGen", 3);
                        s.LastKind = o.GetString("lastKind", "Front");
                        s.ExportScale = o.GetInt("exportScale", 2);
                        s.LastExportFolder = o.GetString("lastExportFolder");
                        s.LastProjectFolder = o.GetString("lastProjectFolder");
                        s.ShowGrid = o.GetBool("showGrid", true);
                        s.DarkMode = o.GetBool("darkMode", false);
                        var r = o.GetArray("recent"); if (r != null) s.Recent = r.Select(x => Convert.ToString(x)).ToList();
                        var c = o.GetArray("customColors"); if (c != null) s.CustomColors = c.Select(x => Convert.ToString(x)).ToList();
                    }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(s.PartsFolder)) s.PartsFolder = DefaultPartsFolder;
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var o = new JsonObject();
                o["partsFolder"] = PartsFolder;
                o["lastGen"] = LastGen;
                o["lastKind"] = LastKind;
                o["exportScale"] = ExportScale;
                o["lastExportFolder"] = LastExportFolder;
                o["lastProjectFolder"] = LastProjectFolder;
                o["showGrid"] = ShowGrid;
                o["darkMode"] = DarkMode;
                o["recent"] = Recent.Cast<object>().ToList();
                o["customColors"] = CustomColors.Cast<object>().ToList();
                File.WriteAllText(FilePath, Json.Json.Write(o), new UTF8Encoding(false));
            }
            catch { }
        }

        public void AddRecent(string path)
        {
            Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, path);
            if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
        }
    }
}
