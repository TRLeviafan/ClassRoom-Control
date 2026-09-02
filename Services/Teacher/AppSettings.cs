using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ClassRoom_Control.Services.Teacher;

public class AppSettingsData
{
    // Telegram
    public string TelegramBotToken { get; set; } = string.Empty;
    public string DeveloperChatId { get; set; } = string.Empty;

    // Cloud & Storage
    public string CloudRootFolder { get; set; } = string.Empty;
    public bool AutoCopyCloud { get; set; } = false;
    public bool AutoCleanupEnabled { get; set; } = false;
    public int RetentionDays { get; set; } = 30; // Min 30 days

    // Video Stream & Demo
    public bool EnableDemoPreview { get; set; } = true;
    public int Fps { get; set; } = 30; // 15, 30, 60
    public int BitrateMbps { get; set; } = 4; // 2, 4, 8

    // System
    public bool LaunchOnStartup { get; set; } = false;
}

public static class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassRoom Control",
        "app_settings.json");

    private static AppSettingsData _current = new();
    public static AppSettingsData Current => _current;

    static AppSettings()
    {
        Load();
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _current = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load app settings: {ex.Message}");
            _current = new AppSettingsData();
        }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (_current.RetentionDays < 30) _current.RetentionDays = 30;

            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);

            ApplyStartupRegistry(_current.LaunchOnStartup);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save app settings: {ex.Message}");
        }
    }

    private static void ApplyStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                string appName = "ClassRoom Control";
                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(appName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update startup registry: {ex.Message}");
        }
    }
}
