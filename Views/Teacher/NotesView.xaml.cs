using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClassRoom_Control.Models;
using ClassRoom_Control.Services.Teacher;

namespace ClassRoom_Control.Views.Teacher;

public partial class NotesView : UserControl
{
    private List<NoteItem> _notes = new();
    private NoteItem? _currentNote;
    private bool _isInternalUpdate = false;

    public NotesView()
    {
        InitializeComponent();
        Loaded += NotesView_Loaded;
    }

    private void NotesView_Loaded(object sender, RoutedEventArgs e)
    {
        _notes = NotesService.LoadNotes();
        if (_notes.Count == 0)
        {
            CreateNewNote();
        }
        else
        {
            RenderNotesList();
            SelectNote(_notes[0]);
        }
    }

    private void RenderNotesList(string filter = "")
    {
        NotesListPanel.Children.Clear();

        var query = string.IsNullOrWhiteSpace(filter)
            ? _notes
            : _notes.Where(n => n.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                             || n.PreviewText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        NotesCountText.Text = $"{query.Count} {GetNotesWord(query.Count)}";

        foreach (var note in query)
        {
            var card = CreateNoteCard(note);
            NotesListPanel.Children.Add(card);
        }
    }

    private string GetNotesWord(int count)
    {
        int rem100 = count % 100;
        int rem10 = count % 10;
        if (rem100 >= 11 && rem100 <= 19) return "заметок";
        if (rem10 == 1) return "заметка";
        if (rem10 >= 2 && rem10 <= 4) return "заметки";
        return "заметок";
    }

    private Border CreateNoteCard(NoteItem note)
    {
        bool isSelected = _currentNote != null && _currentNote.Id == note.Id;

        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 10, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            Tag = note,
            BorderThickness = new Thickness(2),
            BorderBrush = isSelected ? (SolidColorBrush)FindResource("AccentBlueBrush") : Brushes.Transparent,
            Background = isSelected ? (SolidColorBrush)FindResource("AccentBlueMutedBrush") : (SolidColorBrush)FindResource("BgDarkerBrush")
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: Title + Delete button
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(note.Title) ? "Без названия" : note.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 0);
        headerGrid.Children.Add(titleText);

        var delBtn = new Button
        {
            Content = "✕",
            FontSize = 11,
            Foreground = (SolidColorBrush)FindResource("TextMutedBrush"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(4, 0, 4, 0),
            Tag = note
        };
        delBtn.Click += (s, e) =>
        {
            e.Handled = true;
            DeleteNote(note);
        };
        Grid.SetColumn(delBtn, 1);
        headerGrid.Children.Add(delBtn);

        Grid.SetRow(headerGrid, 0);
        grid.Children.Add(headerGrid);

        // Preview snippet
        var preview = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(note.PreviewText) ? "Нет текста..." : note.PreviewText,
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };
        Grid.SetRow(preview, 1);
        grid.Children.Add(preview);

        // Date
        var dateText = new TextBlock
        {
            Text = note.UpdatedAt.ToString("dd.MM.yyyy HH:mm"),
            FontSize = 11,
            Foreground = (SolidColorBrush)FindResource("TextMutedBrush")
        };
        Grid.SetRow(dateText, 2);
        grid.Children.Add(dateText);

        border.Child = grid;

        // Hover effect if not selected
        if (!isSelected)
        {
            border.MouseEnter += (s, e) =>
            {
                if (_currentNote?.Id != note.Id)
                    border.Background = (SolidColorBrush)FindResource("BgHoverBrush");
            };
            border.MouseLeave += (s, e) =>
            {
                if (_currentNote?.Id != note.Id)
                    border.Background = (SolidColorBrush)FindResource("BgDarkerBrush");
            };
        }

        border.MouseDown += (s, e) =>
        {
            if (_currentNote?.Id != note.Id)
            {
                SaveCurrentNote();
                SelectNote(note);
            }
        };

        return border;
    }

    private void SelectNote(NoteItem note)
    {
        _currentNote = note;
        _isInternalUpdate = true;

        NoteTitleBox.Text = note.Title;
        NotesService.SetRtf(NoteEditor, note.RtfContent, note.PreviewText);
        UpdateInfoText();

        _isInternalUpdate = false;
        RenderNotesList(SearchBox.Text);
    }

    private void CreateNewNote()
    {
        SaveCurrentNote();

        var newNote = new NoteItem
        {
            Title = "Новая заметка",
            PreviewText = "",
            RtfContent = "",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _notes.Insert(0, newNote);
        NotesService.SaveNotes(_notes);

        SelectNote(newNote);
        NoteTitleBox.Focus();
        NoteTitleBox.SelectAll();
    }

    private void DeleteNote(NoteItem note)
    {
        int index = _notes.IndexOf(note);
        _notes.Remove(note);
        NotesService.SaveNotes(_notes);

        if (_currentNote?.Id == note.Id)
        {
            if (_notes.Count > 0)
            {
                int newIndex = Math.Clamp(index, 0, _notes.Count - 1);
                SelectNote(_notes[newIndex]);
            }
            else
            {
                CreateNewNote();
            }
        }
        else
        {
            RenderNotesList(SearchBox.Text);
        }
    }

    private void DeleteCurrentNote_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote != null)
        {
            DeleteNote(_currentNote);
        }
    }

    private void SaveCurrentNote()
    {
        if (_currentNote == null || _isInternalUpdate) return;

        _currentNote.Title = string.IsNullOrWhiteSpace(NoteTitleBox.Text) ? "Без названия" : NoteTitleBox.Text.Trim();
        _currentNote.RtfContent = NotesService.GetRtf(NoteEditor);
        _currentNote.PreviewText = NotesService.GetPlainText(NoteEditor);
        _currentNote.UpdatedAt = DateTime.Now;

        NotesService.SaveNotes(_notes);
    }

    private void UpdateInfoText()
    {
        if (_currentNote == null) return;
        string plain = NotesService.GetPlainText(NoteEditor);
        int chars = plain.Length;
        NoteInfoText.Text = $"Изменено: {_currentNote.UpdatedAt:dd.MM.yyyy HH:mm}  •  {chars} символов";
    }

    private void NoteTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate || _currentNote == null) return;
        SaveCurrentNote();
        RenderNotesList(SearchBox.Text);
    }

    private void NoteEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInternalUpdate || _currentNote == null) return;
        SaveCurrentNote();
        UpdateInfoText();
    }

    private void NewNote_Click(object sender, RoutedEventArgs e) => CreateNewNote();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder != null)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        RenderNotesList(SearchBox.Text);
    }

    // ═══════════════════════════════════════════════
    //           FORMATTING ACTIONS
    // ═══════════════════════════════════════════════

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, NoteEditor);
        NoteEditor.Focus();
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleItalic.Execute(null, NoteEditor);
        NoteEditor.Focus();
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleUnderline.Execute(null, NoteEditor);
        NoteEditor.Focus();
    }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var range = NoteEditor.Selection;
        var prop = range.GetPropertyValue(Inline.TextDecorationsProperty);
        var decorations = prop as TextDecorationCollection;
        bool hasStrike = false;

        if (decorations != null)
        {
            foreach (var d in decorations)
            {
                if (d.Location == TextDecorationLocation.Strikethrough)
                {
                    hasStrike = true;
                    break;
                }
            }
        }

        var newDecorations = new TextDecorationCollection();
        if (decorations != null)
        {
            foreach (var d in decorations)
            {
                if (d.Location != TextDecorationLocation.Strikethrough)
                    newDecorations.Add(d);
            }
        }

        if (!hasStrike)
        {
            newDecorations.Add(TextDecorations.Strikethrough);
        }

        range.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecorations);
        NoteEditor.Focus();
    }

    private void H1_Click(object sender, RoutedEventArgs e)
    {
        NoteEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 22.0);
        NoteEditor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        NoteEditor.Focus();
    }

    private void H2_Click(object sender, RoutedEventArgs e)
    {
        NoteEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 17.0);
        NoteEditor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
        NoteEditor.Focus();
    }

    private void NormalSize_Click(object sender, RoutedEventArgs e)
    {
        NoteEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
        NoteEditor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        NoteEditor.Focus();
    }

    private void BulletList_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBullets.Execute(null, NoteEditor);
        NoteEditor.Focus();
    }

    private void NumberedList_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleNumbering.Execute(null, NoteEditor);
        NoteEditor.Focus();
    }

    private void ColorChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                NoteEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            }
            catch { }
        }
        NoteEditor.Focus();
    }

    private void ClearFormat_Click(object sender, RoutedEventArgs e)
    {
        var range = NoteEditor.Selection;
        if (!range.IsEmpty)
        {
            range.ClearAllProperties();
            range.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)FindResource("TextPrimaryBrush"));
            range.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
            range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
        }
        NoteEditor.Focus();
    }
}