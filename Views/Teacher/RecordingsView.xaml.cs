using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ClassRoom_Control.Views.Teacher;

public class RecordingItemModel
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string DateFormatted => CreatedDate.ToString("dd.MM.yyyy HH:mm");
    public string FileSizeFormatted { get; set; } = string.Empty;
}

public partial class RecordingsView : UserControl
{
    private static readonly string RecordingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "ClassRoom Recordings");

    public ObservableCollection<RecordingItemModel> Recordings { get; } = new();

    public RecordingsView()
    {
        InitializeComponent();
        RecordingsList.ItemsSource = Recordings;
        Loaded += (s, e) => LoadRecordings();
    }

    public void LoadRecordings()
    {
        Recordings.Clear();

        try
        {
            if (Directory.Exists(RecordingsFolder))
            {
                var dir = new DirectoryInfo(RecordingsFolder);
                var files = dir.GetFiles("*.mp4")
                               .OrderByDescending(f => f.CreationTime)
                               .ToList();

                foreach (var f in files)
                {
                    double mb = f.Length / (1024.0 * 1024.0);
                    Recordings.Add(new RecordingItemModel
                    {
                        FilePath = f.FullName,
                        FileName = f.Name,
                        CreatedDate = f.CreationTime,
                        FileSizeFormatted = $"{mb:F1} МБ"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to list recordings: {ex.Message}");
        }

        if (Recordings.Count == 0)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            RecordingsScrollViewer.Visibility = Visibility.Collapsed;
            RecordingsSummaryText.Text = "Записей не обнаружено";
        }
        else
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            RecordingsScrollViewer.Visibility = Visibility.Visible;
            RecordingsSummaryText.Text = $"Всего записей: {Recordings.Count}";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadRecordings();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(RecordingsFolder);
            Process.Start("explorer.exe", RecordingsFolder);
        }
        catch { }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void OpenInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && File.Exists(path))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch { }
        }
    }

    private void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && File.Exists(path))
        {
            var dlg = new PublishLessonDialog(path, TimeSpan.Zero) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && File.Exists(path))
        {
            var res = MessageBox.Show(
                $"Вы действительно хотите удалить эту запись?\n\n{Path.GetFileName(path)}",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(path);
                    LoadRecordings();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось удалить файл: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
