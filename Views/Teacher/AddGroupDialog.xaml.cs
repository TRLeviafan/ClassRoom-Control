using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClassRoom_Control.Models;
using ClassRoom_Control.Services.Common;

namespace ClassRoom_Control.Views.Teacher;

public partial class AddGroupDialog : Window
{
    public ClassGroup? CreatedGroup { get; private set; }
    private string _botToken = TelegramService.DefaultBotToken;

    public AddGroupDialog(string? botToken = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(botToken))
            _botToken = botToken;

        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };

        Loaded += async (s, e) =>
        {
            var info = await TelegramService.GetBotInfoAsync(_botToken);
            if (!string.IsNullOrEmpty(info.username))
                BotUsernameLabel.Text = info.username;
        };
    }

    private void CopyBotName_Click(object sender, RoutedEventArgs e)
    {
        CopyBotUsername();
    }

    private void BotUsernameLabel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        CopyBotUsername();
    }

    private void CopyBotUsername()
    {
        try
        {
            string username = BotUsernameLabel.Text.Trim();
            if (!string.IsNullOrEmpty(username))
            {
                Clipboard.SetText(username);
                StatusMsgText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
                StatusMsgText.Text = $"✓ Юзернейм {username} скопирован в буфер обмена!";
            }
        }
        catch { }
    }

    private void OpenBotTelegram_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string username = BotUsernameLabel.Text.Trim().TrimStart('@');
            if (!string.IsNullOrEmpty(username))
            {
                Process.Start(new ProcessStartInfo($"https://t.me/{username}") { UseShellExecute = true });
            }
        }
        catch { }
    }

    private async void AutoDiscover_Click(object sender, RoutedEventArgs e)
    {
        BtnAutoDiscover.IsEnabled = false;
        StatusMsgText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
        StatusMsgText.Text = "Поиск недавних чатов бота...";

        var chats = await TelegramService.DiscoverRecentChatsAsync(_botToken);
        BtnAutoDiscover.IsEnabled = true;

        if (chats.Count > 0)
        {
            DiscoveredCombo.ItemsSource = chats;
            DiscoveredPanel.Visibility = Visibility.Visible;
            DiscoveredCombo.SelectedIndex = 0;

            StatusMsgText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
            StatusMsgText.Text = $"✓ Найдено групп: {chats.Count}. Подставлена: «{chats[0].Title}».";

            MessageBox.Show(
                $"✓ Бот успешно обнаружил недавние чаты ({chats.Count})!\n\n" +
                $"Автоматически подставлена группа: «{chats[0].Title}»\n" +
                $"ID чата: {chats[0].Id}\n\n" +
                (chats.Count > 1 
                    ? "Вы можете выбрать другую группу из появившегося выпадающего списка.\nКогда всё готово, нажмите «Сохранить группу»."
                    : "Все поля заполнены автоматически. Нажмите «Сохранить группу» для завершения."),
                "Группа найдена",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            StatusMsgText.Foreground = (SolidColorBrush)FindResource("StatusYellowBrush");
            StatusMsgText.Text = "Недавние чаты не найдены. Убедитесь, что бот — администратор и в чат отправлено сообщение.";

            MessageBox.Show(
                "Недавние чаты бота пока не найдены.\n\n" +
                "Проверьте следующие шаги:\n" +
                "1. Добавьте бота в ваш канал или группу в Telegram.\n" +
                "2. Назначьте бота Администратором.\n" +
                "3. Отправьте в чат любое сообщение (например, «Тест»).\n" +
                "4. Нажмите «Найти группу автоматически» повторно.",
                "Группы не найдены",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DiscoveredCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscoveredCombo.SelectedItem is TelegramChatCandidate candidate)
        {
            TelegramTargetInput.Text = candidate.Id;
            GroupNameInput.Text = candidate.Title;
            if (string.IsNullOrWhiteSpace(CloudFolderInput.Text))
            {
                CloudFolderInput.Text = candidate.Title;
            }
            StatusMsgText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
            StatusMsgText.Text = $"✓ Выбрана группа: «{candidate.Title}» (ID: {candidate.Id})";
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = GroupNameInput.Text.Trim();
        string target = TelegramTargetInput.Text.Trim();
        string folder = CloudFolderInput.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            StatusMsgText.Foreground = (SolidColorBrush)FindResource("StatusRedBrush");
            StatusMsgText.Text = "Введите название группы/класса.";
            return;
        }

        // Check telegram connection if specified and token available
        if (!string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(_botToken))
        {
            BtnSave.IsEnabled = false;
            StatusMsgText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
            StatusMsgText.Text = "Установка связи с каналом Telegram...";

            var (success, title, error) = await TelegramService.CheckBotAccessAsync(_botToken, target);
            BtnSave.IsEnabled = true;

            if (!success)
            {
                StatusMsgText.Foreground = (SolidColorBrush)FindResource("StatusRedBrush");
                StatusMsgText.Text = $"Ошибка связи: {error}. Проверьте, что бот добавлен администратором.";
                return;
            }
        }

        CreatedGroup = new ClassGroup
        {
            Name = name,
            TelegramTarget = target,
            CloudSubfolder = string.IsNullOrEmpty(folder) ? name : folder
        };

        DialogResult = true;
        Close();
    }

    private void ReportBug_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new BugReportDialog("#TelegramSetupError", StatusMsgText.Text) { Owner = this };
        dlg.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
