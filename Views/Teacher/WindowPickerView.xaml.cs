using System.Windows;
using System.Windows.Controls;
using ClassRoom_Control.Services.Teacher;

namespace ClassRoom_Control.Views.Teacher;

public partial class WindowPickerView : Window
{
    public WindowInfo? SelectedWindow { get; private set; }

    public WindowPickerView()
    {
        InitializeComponent();
        Loaded += WindowPickerView_Loaded;
        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        };
    }

    private void WindowPickerView_Loaded(object sender, RoutedEventArgs e)
    {
        WindowsList.ItemsSource = WindowEnumerator.GetVisibleWindows();
    }

    private void WindowsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnStart.IsEnabled = WindowsList.SelectedItem != null;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (WindowsList.SelectedItem is WindowInfo info)
        {
            SelectedWindow = info;
            DialogResult = true;
            Close();
        }
    }
}
