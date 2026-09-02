using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClassRoom_Control.Models;
using ClassRoom_Control.Services.Common;
using ClassRoom_Control.Services.Teacher;

namespace ClassRoom_Control.Views.Teacher;

public partial class SettingsView : UserControl
{
    private readonly GroupManager _groupManager = new();

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (s, e) => LoadSettingsToUi();
    }

    private async void LoadSettingsToUi()
    {
        var s = AppSettings.Current;

        // Telegram (из настроек или secrets.json)
        string effectiveToken = TelegramService.GetEffectiveBotToken(s.TelegramBotToken);
        string effectiveDevId = TelegramService.GetEffectiveDevChatId(s.DeveloperChatId);

        TelegramTokenInput.Text = !string.IsNullOrWhiteSpace(s.TelegramBotToken) ? s.TelegramBotToken : effectiveToken;
        DeveloperChatIdInput.Text = !string.IsNullOrWhiteSpace(s.DeveloperChatId) ? s.DeveloperChatId : effectiveDevId;

        // Fetch official bot info
        var botInfo = await TelegramService.GetBotInfoAsync(effectiveToken);
        if (!string.IsNullOrEmpty(botInfo.username))
        {
            BotUsernameText.Text = botInfo.username;
            BotStatusBadge.Text = botInfo.ok ? "✓ Готов к работе" : "⚠️ Не настроен";
        }

        // Cloud & Retention
        CloudFolderInput.Text = s.CloudRootFolder;
        ChkAutoCopyCloud.IsChecked = s.AutoCopyCloud;
        ChkAutoCleanup.IsChecked = s.AutoCleanupEnabled;

        foreach (ComboBoxItem item in RetentionDaysCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int days) && days == s.RetentionDays)
            {
                RetentionDaysCombo.SelectedItem = item;
                break;
            }
        }

        // Demo & Video
        ChkEnablePreview.IsChecked = s.EnableDemoPreview;

        foreach (ComboBoxItem item in FpsCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int fps) && fps == s.Fps)
            {
                FpsCombo.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in BitrateCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int mbps) && mbps == s.BitrateMbps)
            {
                BitrateCombo.SelectedItem = item;
                break;
            }
        }

        // System
        ChkStartup.IsChecked = s.LaunchOnStartup;

        // Groups
        LoadGroups();
    }

    private void LoadGroups()
    {
        _groupManager.Load();
        GroupsListBox.ItemsSource = null;
        GroupsListBox.ItemsSource = _groupManager.Groups;
    }

    private void ToggleDevSettings_Click(object sender, RoutedEventArgs e)
    {
        DevSettingsPanel.Visibility = DevSettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void CopyBotUsername_Click(object sender, RoutedEventArgs e)
    {
        CopyBotToClipboard();
    }

    private void BotUsername_MouseDown(object sender, MouseButtonEventArgs e)
    {
        CopyBotToClipboard();
    }

    private void CopyBotToClipboard()
    {
        try
        {
            string username = BotUsernameText.Text.Trim();
            if (!string.IsNullOrEmpty(username))
            {
                Clipboard.SetText(username);
                SaveStatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
                SaveStatusText.Text = $"✓ Юзернейм {username} скопирован в буфер обмена!";
                MessageBox.Show($"Юзернейм бота {username} скопирован в буфер обмена!\n\nВставьте его в поле поиска Telegram, чтобы добавить бота администратором в ваш канал/группу.", "Скопировано", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch { }
    }

    private void OpenBotInTelegram_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string username = BotUsernameText.Text.Trim().TrimStart('@');
            if (!string.IsNullOrEmpty(username))
            {
                Process.Start(new ProcessStartInfo($"https://t.me/{username}") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть ссылку: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckToken_Click(object sender, RoutedEventArgs e)
    {
        string token = TelegramService.GetEffectiveBotToken(TelegramTokenInput.Text);
        if (string.IsNullOrEmpty(token))
        {
            MessageBox.Show("Официальный токен бота пока не задан в конфигурации приложения. Вы можете указать собственный токен в «Расширенных настройках бота».", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveStatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
        SaveStatusText.Text = "Проверка связи с Telegram Bot API...";

        var botInfo = await TelegramService.GetBotInfoAsync(token);
        if (botInfo.ok)
        {
            BotUsernameText.Text = botInfo.username;
            BotStatusBadge.Text = "✓ Активен";
            SaveStatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
            SaveStatusText.Text = $"Бот {botInfo.username} успешно отвечает на запросы!";
            MessageBox.Show($"Бот {botInfo.username} активен и готов к отправке видео уроков!", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            SaveStatusText.Foreground = (SolidColorBrush)FindResource("StatusYellowBrush");
            SaveStatusText.Text = "Не удалось связаться с Telegram. Проверьте подключение к Интернету.";
            MessageBox.Show("Не удалось связаться с Telegram Bot API. Проверьте доступ к сети Интернет.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BrowseCloudFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку Google Диска / облака для сохранения уроков"
        };

        if (dialog.ShowDialog() == true)
        {
            CloudFolderInput.Text = dialog.FolderName;
        }
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        string token = TelegramService.GetEffectiveBotToken(TelegramTokenInput.Text);
        var dlg = new AddGroupDialog(token) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.CreatedGroup != null)
        {
            _groupManager.AddGroup(dlg.CreatedGroup);
            LoadGroups();
        }
    }

    private void RemoveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ClassGroup group)
        {
            _groupManager.RemoveGroup(group);
            LoadGroups();
        }
    }

    private void OpenBugReport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new BugReportDialog("#UserFeedback", null) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private void OpenDeveloperTelegram_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string devId = TelegramService.GetEffectiveDevChatId();
            string url = !string.IsNullOrWhiteSpace(devId) && !devId.StartsWith("-")
                ? $"tg://user?id={devId}"
                : "https://t.me/";

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("https://t.me/") { UseShellExecute = true }); } catch { }
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        SaveStatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
        SaveStatusText.Text = "Проверка обновлений на GitHub...";

        var update = await UpdateService.CheckForUpdatesAsync();
        if (update.HasUpdate)
        {
            var res = MessageBox.Show(
                $"🎉 Доступна новая версия: {update.NewVersion} (текущая: {AppMetadata.Version})!\n\n" +
                $"Что нового:\n{update.Changelog}\n\n" +
                "Открыть страницу загрузки обновления?",
                "Доступно обновление",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes && !string.IsNullOrEmpty(update.ReleasePageUrl))
            {
                Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
            }
        }
        else
        {
            SaveStatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
            SaveStatusText.Text = $"✓ У вас установлена актуальная версия ClassRoom Control ({AppMetadata.Version}).";
            MessageBox.Show(
                $"У вас установлена самая свежая версия ClassRoom Control ({AppMetadata.Version}).\nОбновлений пока нет.",
                "Обновления",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;

        s.TelegramBotToken = TelegramTokenInput.Text.Trim();
        s.DeveloperChatId = DeveloperChatIdInput.Text.Trim();

        s.CloudRootFolder = CloudFolderInput.Text.Trim();
        s.AutoCopyCloud = ChkAutoCopyCloud.IsChecked == true;
        s.AutoCleanupEnabled = ChkAutoCleanup.IsChecked == true;

        if (RetentionDaysCombo.SelectedItem is ComboBoxItem retItem && retItem.Tag is string retStr && int.TryParse(retStr, out int days))
        {
            s.RetentionDays = days;
        }

        s.EnableDemoPreview = ChkEnablePreview.IsChecked == true;

        if (FpsCombo.SelectedItem is ComboBoxItem fpsItem && fpsItem.Tag is string fpsStr && int.TryParse(fpsStr, out int fps))
        {
            s.Fps = fps;
        }

        if (BitrateCombo.SelectedItem is ComboBoxItem brItem && brItem.Tag is string brStr && int.TryParse(brStr, out int mbps))
        {
            s.BitrateMbps = mbps;
        }

        s.LaunchOnStartup = ChkStartup.IsChecked == true;

        AppSettings.Save();

        // Also sync with AutoCleanupService
        AutoCleanupService.SaveSettings(new CleanupSettings
        {
            AutoCleanupEnabled = s.AutoCleanupEnabled,
            RetentionDays = s.RetentionDays
        });

        SaveStatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
        SaveStatusText.Text = "✓ Настройки успешно сохранены!";
    }
}
