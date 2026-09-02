using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClassRoom_Control.Models;
using ClassRoom_Control.Services.Common;
using ClassRoom_Control.Services.Teacher;

namespace ClassRoom_Control.Views.Teacher;

public partial class PublishLessonDialog : Window
{
    private readonly string _videoPath;
    private readonly TimeSpan _duration;
    private readonly GroupManager _groupManager = new();
    private string _botToken = TelegramService.DefaultBotToken;

    public PublishLessonDialog(string videoPath, TimeSpan duration, string? botToken = null)
    {
        InitializeComponent();
        _videoPath = videoPath;
        _duration = duration;
        if (!string.IsNullOrEmpty(botToken))
            _botToken = botToken;

        // Populate header details
        try
        {
            if (File.Exists(_videoPath))
            {
                var fi = new FileInfo(_videoPath);
                double mb = fi.Length / (1024.0 * 1024.0);
                FileInfoSubtitle.Text = $"Размер: {mb:F1} МБ • Длительность: {_duration:mm\\:ss} • Файл: {fi.Name}";
            }
        }
        catch { }

        LessonTopicInput.Text = $"Урок {DateTime.Now:dd.MM.yyyy}";

        LoadGroups();

        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
    }

    private void LoadGroups()
    {
        _groupManager.Load();
        GroupsCombo.ItemsSource = null;
        GroupsCombo.ItemsSource = _groupManager.Groups;
        if (_groupManager.Groups.Count > 0)
        {
            GroupsCombo.SelectedIndex = 0;
        }
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddGroupDialog(_botToken) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.CreatedGroup != null)
        {
            _groupManager.AddGroup(dlg.CreatedGroup);
            LoadGroups();
            GroupsCombo.SelectedItem = dlg.CreatedGroup;
        }
    }

    private void BrowseCloud_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку Google Диска или облачного хранилища"
        };

        if (dialog.ShowDialog() == true)
        {
            CloudRootPathInput.Text = dialog.FolderName;
        }
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        var selectedGroup = GroupsCombo.SelectedItem as ClassGroup;
        string topic = LessonTopicInput.Text.Trim();
        if (string.IsNullOrEmpty(topic)) topic = $"Урок {DateTime.Now:dd.MM.yyyy}";

        BtnPublish.IsEnabled = false;
        StatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
        StatusText.Text = "Обработка публикации...";

        string? cloudDestPath = null;

        // 1. Cloud Copy
        if (ChkCloudCopy.IsChecked == true && !string.IsNullOrWhiteSpace(CloudRootPathInput.Text))
        {
            try
            {
                string targetDir = CloudRootPathInput.Text.Trim();
                if (selectedGroup != null && !string.IsNullOrEmpty(selectedGroup.CloudSubfolder))
                {
                    targetDir = Path.Combine(targetDir, selectedGroup.CloudSubfolder);
                }
                Directory.CreateDirectory(targetDir);

                string destFile = Path.Combine(targetDir, Path.GetFileName(_videoPath));
                StatusText.Text = "Копирование в облачную папку...";
                await Task.Run(() => File.Copy(_videoPath, destFile, true));
                cloudDestPath = destFile;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cloud copy failed: {ex.Message}");
            }
        }

        // 2. Telegram Publish
        if (ChkTelegramPublish.IsChecked == true && selectedGroup != null && !string.IsNullOrEmpty(selectedGroup.TelegramTarget) && !string.IsNullOrEmpty(_botToken))
        {
            StatusText.Text = "Отправка в Telegram-канал группы...";
            var (tgSuccess, tgErr) = await TelegramService.SendLessonNotificationAsync(
                _botToken,
                selectedGroup.TelegramTarget,
                topic,
                _duration,
                _videoPath,
                cloudDestPath != null ? Path.GetFileName(cloudDestPath) : null);

            if (!tgSuccess)
            {
                StatusText.Foreground = (SolidColorBrush)FindResource("StatusYellowBrush");
                StatusText.Text = $"Файл сохранен, но Telegram вернул ошибку: {tgErr}";
                BtnPublish.IsEnabled = true;
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
