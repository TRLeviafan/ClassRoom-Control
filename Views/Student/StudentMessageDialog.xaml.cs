using System.Media;
using System.Windows;
using System.Windows.Input;

namespace ClassRoom_Control.Views.Student;

public partial class StudentMessageDialog : Window
{
    public StudentMessageDialog(string message)
    {
        InitializeComponent();

        MessageBodyText.Text = message;

        Loaded += (s, e) =>
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch { }
        };

        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
