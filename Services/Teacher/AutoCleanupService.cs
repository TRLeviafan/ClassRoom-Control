using System;
using System.IO;
using System.Text.Json;

namespace ClassRoom_Control.Services.Teacher;

public class CleanupSettings
{
    public bool AutoCleanupEnabled { get; set; } = false;
    public int RetentionDays { get; set; } = 30; // Min 30 days
}

public static class AutoCleanupService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassRoom Control",
        "cleanup_settings.json");

    public static CleanupSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<CleanupSettings>(json) ?? new CleanupSettings();
            }
        }
        catch { }
        return new CleanupSettings();
    }

    public static void SaveSettings(CleanupSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (settings.RetentionDays < 30) settings.RetentionDays = 30;
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void RunCleanupIfEnabled()
    {
        try
        {
            var settings = LoadSettings();
            if (!settings.AutoCleanupEnabled) return;

            int days = Math.Max(30, settings.RetentionDays);
            var cutoff = DateTime.Now.AddDays(-days);

            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ClassRoom Recordings");
            if (!Directory.Exists(folder)) return;

            var dirInfo = new DirectoryInfo(folder);
            foreach (var file in dirInfo.GetFiles())
            {
                if (file.CreationTime < cutoff || file.LastWriteTime < cutoff)
                {
                    try
                    {
                        file.Delete();
                        System.Diagnostics.Debug.WriteLine($"[Cleanup] Deleted old recording: {file.Name}");
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoCleanupService error: {ex.Message}");
        }
    }
}
