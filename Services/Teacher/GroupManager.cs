using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ClassRoom_Control.Models;

namespace ClassRoom_Control.Services.Teacher;

public class GroupManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassRoom Control");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "groups.json");

    public ObservableCollection<ClassGroup> Groups { get; } = new();

    public GroupManager()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var list = JsonSerializer.Deserialize<ClassGroup[]>(json);
                Groups.Clear();
                if (list != null)
                {
                    foreach (var g in list)
                    {
                        Groups.Add(g);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load groups: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Groups, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save groups: {ex.Message}");
        }
    }

    public void AddGroup(ClassGroup group)
    {
        Groups.Add(group);
        Save();
    }

    public void RemoveGroup(ClassGroup group)
    {
        Groups.Remove(group);
        Save();
    }
}
