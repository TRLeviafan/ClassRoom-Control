using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClassRoom_Control.Services.Common;
using ClassRoom_Control.Services.Teacher;

namespace ClassRoom_Control.Views.Teacher;

public partial class BugReportDialog : Window
{
    private string _botToken;
    private string _devChatId;

    public BugReportDialog(string defaultTag = "#BugReport", string? defaultDetails = null)
    {
        InitializeComponent();

        // Берём токен и ChatID из настроек / secrets.json
        _botToken = TelegramService.GetEffectiveBotToken();
        _devChatId = TelegramService.GetEffectiveDevChatId();

        DetailsInput.Text = defaultDetails ?? string.Empty;

        // Попробуем выбрать соответствующий тег в ComboBox
        foreach (ComboBoxItem item in CategoryCombo.Items)
        {
            if (item.Tag is string tag && tag.Equals(defaultTag, StringComparison.OrdinalIgnoreCase))
            {
                CategoryCombo.SelectedItem = item;
                break;
            }
        }

        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        string message = MessageInput.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            StatusText.Foreground = (SolidColorBrush)FindResource("StatusRedBrush");
            StatusText.Text = "Пожалуйста, опишите, что произошло.";
            return;
        }

        string tag = "#BugReport";
        if (CategoryCombo.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string itemTag)
        {
            tag = itemTag;
        }

        BtnSend.IsEnabled = false;
        StatusText.Foreground = (SolidColorBrush)FindResource("AccentBlueLightBrush");
        StatusText.Text = "Отправка баг-репорта...";

        bool ok = await TelegramService.SendBugReportAsync(_botToken, _devChatId, tag, message, DetailsInput.Text.Trim());
        BtnSend.IsEnabled = true;

        if (ok)
        {
            MessageBox.Show("Спасибо! Баг-репорт успешно доставлен разработчику.", "Отправлено", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        else
        {
            StatusText.Foreground = (SolidColorBrush)FindResource("StatusYellowBrush");
            StatusText.Text = "Не удалось отправить автоматически. Проверьте интернет или токен бота в Настройках.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
