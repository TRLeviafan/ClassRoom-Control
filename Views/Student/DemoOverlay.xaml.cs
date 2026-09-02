using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClassRoom_Control.Views.Student;

public partial class DemoOverlay : Window
{
    private bool _canReallyClose = false;

    public DemoOverlay()
    {
        InitializeComponent();
        SetFullScreenBounds();
    }

    private void SetFullScreenBounds()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public void ShowDemo()
    {
        SetFullScreenBounds();
        LockPanel.Visibility = Visibility.Collapsed;
        WaitingPanel.Visibility = Visibility.Visible;
        ScreenImage.Source = null;

        Show();
        Activate();
        Topmost = true;
    }

    public void ShowLock(string message = "")
    {
        SetFullScreenBounds();
        WaitingPanel.Visibility = Visibility.Collapsed;
        LockPanel.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(message))
        {
            LockMessageText.Text = message;
        }

        Show();
        Activate();
        Topmost = true;
    }

    public void HideOverlay()
    {
        Hide();
        ScreenImage.Source = null;
    }

    public void UpdateFrame(BitmapSource frame)
    {
        ScreenImage.Source = frame;
        if (WaitingPanel.Visibility != Visibility.Collapsed)
        {
            WaitingPanel.Visibility = Visibility.Collapsed;
        }
    }

    public void ForceClose()
    {
        _canReallyClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_canReallyClose)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            base.OnClosing(e);
        }
    }
}