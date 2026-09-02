using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClassRoom_Control.Configuration;
using ClassRoom_Control.Views.Teacher;

namespace ClassRoom_Control.Views.Setup;

public partial class RoleSelectView : UserControl
{
    private readonly MainWindow _mainWindow;
    private AppRole _selectedRole = AppRole.None;

    public RoleSelectView(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
    }

    private void TeacherCardClick(object sender, MouseButtonEventArgs e)
    {
        _selectedRole = AppRole.Teacher;
        HighlightCard(TeacherCard, true);
        HighlightCard(StudentCard, false);
        StudentNamePanel.Visibility = Visibility.Collapsed;
        ConfirmBtn.IsEnabled = true;
    }

    private void StudentCardClick(object sender, MouseButtonEventArgs e)
    {
        _selectedRole = AppRole.Student;
        HighlightCard(StudentCard, true);
        HighlightCard(TeacherCard, false);
        StudentNamePanel.Visibility = Visibility.Visible;
        StudentNameBox.Focus();
        ConfirmBtn.IsEnabled = true;
    }

    private void HighlightCard(System.Windows.Controls.Border card, bool selected)
    {
        if (selected)
        {
            card.BorderBrush = (SolidColorBrush)FindResource("AccentBlueBrush");
            card.Background = (SolidColorBrush)FindResource("BgHoverBrush");
        }
        else
        {
            card.BorderBrush = Brushes.Transparent;
            card.Background = (SolidColorBrush)FindResource("BgDarkBrush");
        }
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        var config = new AppConfig
        {
            Role = _selectedRole,
            StudentName = _selectedRole == AppRole.Student ? StudentNameBox.Text.Trim() : "",
            AutoStart = _selectedRole == AppRole.Student
        };

        ConfigService.Save(config);

        if (_selectedRole == AppRole.Teacher)
        {
            _mainWindow.NavigateTo(new DashboardView());
        }
        else
        {
            // For now, show dashboard. Later — minimize to tray.
            _mainWindow.NavigateTo(new DashboardView());
        }
    }
}
