
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LocalCursor.Services
{
    public static class PersistenceService
    {
        public static void SaveChat(string workspacePath, List<SimpleMessage> history)
        {
            try
            {
                if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;
                
                var path = Path.Combine(workspacePath, "chat_history.json");
                var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static List<SimpleMessage> LoadChat(string workspacePath)
        {
            try
            {
                if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return new List<SimpleMessage>();
                
                var path = Path.Combine(workspacePath, "chat_history.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<List<SimpleMessage>>(json) ?? new List<SimpleMessage>();
                }
            }
            catch { }
            return new List<SimpleMessage>();
        }

        public static void SaveGeneralSettings(AppSettings settings)
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var settingsDir = Path.Combine(appDataPath, "LocalCursor");
                if (!Directory.Exists(settingsDir)) Directory.CreateDirectory(settingsDir);

                var path = Path.Combine(settingsDir, "settings.json");
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static AppSettings LoadGeneralSettings()
        {
            try
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var path = Path.Combine(appDataPath, "LocalCursor", "settings.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }
    }

    public class AppSettings
    {
        public string? SelectedProvider { get; set; }
        public string? SelectedModel { get; set; }
        public string? ApiEndpoint { get; set; }
        public string? ExecutionMode { get; set; }
        public string? WorkspacePath { get; set; }
        public bool IsAutoRotateEnabled { get; set; }
    }
}
