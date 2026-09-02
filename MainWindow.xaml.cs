using System.Windows;
using ClassRoom_Control.Configuration;
using ClassRoom_Control.Views.Setup;
using ClassRoom_Control.Views.Teacher;
using ClassRoom_Control.Views.Student;

namespace ClassRoom_Control;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var config = ConfigService.Load();

        if (config.Role == AppRole.None)
        {
            NavigateTo(new RoleSelectView(this));
        }
        else if (config.Role == AppRole.Teacher)
        {
            NavigateTo(new DashboardView());
        }
        else
        {
            NavigateTo(new StudentModeView(this, config));
        }
    }

    public void NavigateTo(object view)
    {
        ContentArea.Content = view;
    }

    private void MinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e)
        => Close();
}
