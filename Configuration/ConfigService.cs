using System.IO;
using System.Text.Json;

namespace ClassRoom_Control.Configuration;

public enum AppRole { None, Teacher, Student }

public class AppConfig
{
    public AppRole Role { get; set; } = AppRole.None;
    public string StudentName { get; set; } = "";
    public bool AutoStart { get; set; } = true;
    public string? ServerAddress { get; set; }
}

public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassRoomControl");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public static bool HasConfig() => File.Exists(ConfigPath);
}
