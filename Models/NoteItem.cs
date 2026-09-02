using System;

namespace ClassRoom_Control.Models;

public class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Новая заметка";
    public string PreviewText { get; set; } = "";
    public string RtfContent { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}