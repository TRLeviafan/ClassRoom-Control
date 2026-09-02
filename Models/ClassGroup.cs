using System;

namespace ClassRoom_Control.Models;

public class ClassGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string TelegramTarget { get; set; } = string.Empty; // e.g. "@school7_groupA" or "-100192837465"
    public string CloudSubfolder { get; set; } = string.Empty; // e.g. "Группа 7-А"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public override string ToString() => Name;
}
