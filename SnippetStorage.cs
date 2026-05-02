using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Clipora
{
    public static class SnippetStorage
    {
        private const string SnippetsFile = "snippets.json";
        private const string SettingsFile = "settings.json";

        private static string GetFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Clipora");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string GetPath(string name) => Path.Combine(GetFolder(), name);

        public static List<Snippet> LoadSnippets()
        {
            var path = GetPath(SnippetsFile);
            if (!File.Exists(path)) return new List<Snippet>();
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<Snippet>>(json) ?? new List<Snippet>();
            }
            catch
            {
                return new List<Snippet>();
            }
        }

        public static void SaveSnippets(IEnumerable<Snippet> snippets)
        {
            try
            {
                var json = JsonSerializer.Serialize(snippets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetPath(SnippetsFile), json);
            }
            catch { }
        }

        public static AppSettings LoadSettings()
        {
            var path = GetPath(SettingsFile);
            if (!File.Exists(path)) return new AppSettings();
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetPath(SettingsFile), json);
            }
            catch { }
        }

        public static string ExportJson(IEnumerable<Snippet> snippets)
            => JsonSerializer.Serialize(snippets, new JsonSerializerOptions { WriteIndented = true });

        public static List<Snippet>? ImportJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<List<Snippet>>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
