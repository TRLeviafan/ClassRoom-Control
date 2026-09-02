using System.Windows;
using System.Windows.Input;

namespace ClassRoom_Control.Views.Teacher;

public partial class SendMessageDialog : Window
{
    public string MessageText { get; private set; } = string.Empty;

    public SendMessageDialog(string? targetStudentName = null)
    {
        InitializeComponent();

        if (!string.IsNullOrEmpty(targetStudentName))
        {
            DialogTitleText.Text = $"Сообщение для {targetStudentName}";
            DialogTargetText.Text = "Сообщение будет отправлено только этому ученику";
        }

        Loaded += (s, e) =>
        {
            MessageInput.Focus();
        };

        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var text = MessageInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("Введите текст сообщения.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageText = text;
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
