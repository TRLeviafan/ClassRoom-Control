using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ClassRoom_Control.Models;

namespace ClassRoom_Control.Services.Teacher;

public static class NotesService
{
    private static readonly string NotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassRoomControl");

    private static readonly string NotesFile = Path.Combine(NotesDir, "notes.json");

    public static List<NoteItem> LoadNotes()
    {
        try
        {
            if (File.Exists(NotesFile))
            {
                var json = File.ReadAllText(NotesFile, Encoding.UTF8);
                var notes = JsonSerializer.Deserialize<List<NoteItem>>(json);
                if (notes != null && notes.Count > 0)
                    return notes;
            }
        }
        catch { }

        return CreateSampleNotes();
    }

    public static void SaveNotes(IEnumerable<NoteItem> notes)
    {
        try
        {
            Directory.CreateDirectory(NotesDir);
            var json = JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(NotesFile, json, Encoding.UTF8);
        }
        catch { }
    }

    public static string GetRtf(RichTextBox rtb)
    {
        try
        {
            var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.Rtf);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void SetRtf(RichTextBox rtb, string rtf, string fallbackText = "")
    {
        rtb.Document.Blocks.Clear();

        if (!string.IsNullOrWhiteSpace(rtf))
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(rtf);
                using var ms = new MemoryStream(bytes);
                var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
                range.Load(ms, DataFormats.Rtf);
                return;
            }
            catch { }
        }

        // Fallback plain text
        var p = new Paragraph();
        if (!string.IsNullOrEmpty(fallbackText))
        {
            p.Inlines.Add(new Run(fallbackText));
        }
        rtb.Document.Blocks.Add(p);
    }

    public static string GetPlainText(RichTextBox rtb)
    {
        try
        {
            var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            return range.Text.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<NoteItem> CreateSampleNotes()
    {
        return new List<NoteItem>
        {
            new NoteItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = "План занятия — Компьютерные сети",
                PreviewText = "1. Повторение: IP-адреса и маски подсетей. 2. Практика на ПК: трассировка маршрута. 3. Задание на дом.",
                CreatedAt = DateTime.Now.AddHours(-2),
                UpdatedAt = DateTime.Now.AddHours(-1)
            },
            new NoteItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Важные правила и пароли",
                PreviewText = "Пароль от админки ПК: Teach2026! Не забыть выключить все ПК после последнего урока.",
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            }
        };
    }
}