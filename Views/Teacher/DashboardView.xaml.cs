using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClassRoom_Control.Protocol;
using ClassRoom_Control.Services.Common;
using ClassRoom_Control.Services.Teacher;

namespace ClassRoom_Control.Views.Teacher;

public partial class DashboardView : UserControl
{
    private NotesView? _notesView;
    private RecordingsView? _recordingsView;
    private SettingsView? _settingsView;
    private readonly DiscoveryService _discovery = new();
    private readonly StudentManager _studentManager = new();
    private readonly TcpCommandServer _server;

    private bool _isDemoActive = false;
    private bool _isLockActive = false;

    private ScreenCapturer? _capturer;
    private PreviewImageHelper? _previewHelper;
    private H264Encoder? _encoder;
    private UdpVideoSender? _videoSender;
    private System.Windows.Threading.DispatcherTimer? _thumbnailTimer;
    private readonly LessonRecorder _recorder = new();
    private System.Windows.Threading.DispatcherTimer? _recordDurationTimer;

    public DashboardView()
    {
        InitializeComponent();

        _server = new TcpCommandServer(_studentManager);
        _studentManager.StudentsChanged += OnStudentsChanged;
        StudentGrid.ItemsSource = _studentManager.Students;

        Loaded += DashboardView_Loaded;
        Unloaded += DashboardView_Unloaded;
    }

    private void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.Activated += Window_Activated;
            window.Deactivated += Window_Deactivated;
        }

        try
        {
            _discovery.StartTeacherListener();
            _server.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка запуска сети: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Start periodic student screen thumbnail polling (every 5 seconds)
        _thumbnailTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _thumbnailTimer.Tick += async (s, args) =>
        {
            if (_studentManager.Students.Any(st => st.IsOnline))
            {
                await _server.BroadcastCommandAsync(CommandType.RequestThumbnail);
            }
        };
        _thumbnailTimer.Start();

        UpdateUiStats();

        // Background maintenance: auto-cleanup old recordings and check updates
        _ = Task.Run(() =>
        {
            AutoCleanupService.RunCleanupIfEnabled();
            _ = UpdateService.CheckForUpdatesAsync();
        });
    }

    private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
    {
        _thumbnailTimer?.Stop();
        _thumbnailTimer = null;

        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.Activated -= Window_Activated;
            window.Deactivated -= Window_Deactivated;
        }

        _discovery.Stop();
        _server.Stop();
        _recordDurationTimer?.Stop();
        _recordDurationTimer = null;
        _recorder.Dispose();
        StopCapture();
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (_isDemoActive && AppSettings.Current.EnableDemoPreview)
        {
            PreviewPanel.Visibility = Visibility.Visible;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        PreviewPanel.Visibility = Visibility.Collapsed;
    }

    private void OnStudentsChanged()
    {
        UpdateUiStats();
    }

    private void UpdateUiStats()
    {
        int total = _studentManager.Students.Count;
        int online = _studentManager.Students.Count(s => s.IsOnline);

        ConnectedCountRun.Text = total.ToString();
        OnlineBadgeText.Text = $"{online} онлайн";

        if (total == 0)
        {
            EmptyStudentsPanel.Visibility = Visibility.Visible;
            StudentsScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyStudentsPanel.Visibility = Visibility.Collapsed;
            StudentsScrollViewer.Visibility = Visibility.Visible;
        }
    }

    // ─── BOTTOM ACTION BUTTONS ───

    private async void BtnDemo_Click(object sender, RoutedEventArgs e)
    {
        if (!_isDemoActive)
        {
            var picker = new WindowPickerView { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true && picker.SelectedWindow != null)
            {
                _isDemoActive = true;
                BtnDemo.Background = (SolidColorBrush)FindResource("AccentBlueDarkBrush");
                BtnDemoIcon.Text = "⏹";
                BtnDemoText.Text = "Остановить демку";

                _studentManager.SetAllDemoState(true);
                
                StartCapture(picker.SelectedWindow);
                
                await _server.BroadcastCommandAsync(CommandType.StartDemo);
            }
        }
        else
        {
            _isDemoActive = false;
            BtnDemo.Background = (SolidColorBrush)FindResource("AccentBlueBrush");
            BtnDemoIcon.Text = "▶";
            BtnDemoText.Text = "Начать демонстрацию";

            _studentManager.SetAllDemoState(false);
            
            StopCapture();
            
            await _server.BroadcastCommandAsync(CommandType.StopDemo);
        }
    }

    private void StartCapture(WindowInfo target)
    {
        _capturer = new ScreenCapturer(target);
        _previewHelper = new PreviewImageHelper(_capturer.D3DDevice, _capturer.D3DContext);

        try
        {
            _videoSender = new UdpVideoSender();
            _encoder = new H264Encoder(_capturer.D3DDevice, _capturer.D3DContext, _capturer.Width, _capturer.Height, 30, 4_000_000);
            _encoder.FrameEncoded += async (s, e) =>
            {
                if (_recorder.IsRecording)
                {
                    _recorder.WriteVideoFrame(e.Data, 0, e.Data.Length);
                }

                if (_videoSender != null)
                {
                    uint timestampMs = (uint)(e.Timestamp100Ns / 10_000);
                    await _videoSender.SendFrameAsync(e.Data, e.IsKeyFrame, timestampMs);
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Encoder init failed: {ex.Message}");
        }
        
        _capturer.FrameCaptured += texture => 
        {
            // Кодируем кадр в H.264
            try
            {
                _encoder?.EncodeTexture(texture);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Encode error: {ex.Message}");
            }

            // FrameCaptured runs on a background thread.
            // We must use the Dispatcher to access UI properties like Window.IsActive
            bool isActive = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                isActive = Window.GetWindow(this)?.IsActive == true;
            });

            if (isActive && AppSettings.Current.EnableDemoPreview)
            {
                var bmp = _previewHelper.GetPreviewBitmap(texture);
                if (bmp != null)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        PreviewImage.Source = bmp;
                    });
                }
            }
        };

        if (Window.GetWindow(this)?.IsActive == true && AppSettings.Current.EnableDemoPreview)
            PreviewPanel.Visibility = Visibility.Visible;
    }

    private void StopCapture()
    {
        _videoSender?.Dispose();
        _videoSender = null;

        // If recording is still active, keep capturer and encoder running
        if (_recorder.IsRecording)
        {
            return;
        }

        PreviewPanel.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;

        _encoder?.Dispose();
        _encoder = null;

        _previewHelper?.Dispose();
        _previewHelper = null;

        _capturer?.Dispose();
        _capturer = null;
    }

    private async void BtnLock_Click(object sender, RoutedEventArgs e)
    {
        _isLockActive = !_isLockActive;

        if (_isLockActive)
        {
            BtnLock.Background = (SolidColorBrush)FindResource("StatusRedBrush");
            BtnLockText.Text = "Разблокировать всех";

            _studentManager.SetAllLockState(true);
            await _server.BroadcastCommandAsync(CommandType.LockScreen, "Внимание на преподавателя!");
        }
        else
        {
            BtnLock.Background = (SolidColorBrush)FindResource("BgLightBrush");
            BtnLockText.Text = "Заблокировать всех";

            _studentManager.SetAllLockState(false);
            await _server.BroadcastCommandAsync(CommandType.UnlockScreen);
        }
    }

    private async void BtnMessage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SendMessageDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            await _server.BroadcastCommandAsync(CommandType.SendMessage, dlg.MessageText);
        }
    }

    private async void BtnRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            // Stop recording
            _recordDurationTimer?.Stop();
            _recordDurationTimer = null;

            BtnRecord.Background = (SolidColorBrush)FindResource("BgLightBrush");
            BtnRecordIcon.Text = "⏺";
            BtnRecordText.Text = "Начать запись";

            var savedPath = await _recorder.StopRecordingAsync();

            if (!_isDemoActive)
            {
                StopCapture();
            }

            if (!string.IsNullOrEmpty(savedPath))
            {
                var publishDlg = new PublishLessonDialog(savedPath, _recorder.Duration) { Owner = Window.GetWindow(this) };
                publishDlg.ShowDialog();

                _ = Task.Run(AutoCleanupService.RunCleanupIfEnabled);
            }
        }
        else
        {
            // Start recording
            if (_capturer == null)
            {
                StartCapture(new WindowInfo { Handle = IntPtr.Zero, Title = "Весь экран", IsScreen = true });
            }

            _recorder.StartRecording(_capturer?.Width ?? 1920, _capturer?.Height ?? 1080, 30);

            BtnRecord.Background = (SolidColorBrush)FindResource("StatusRedBrush");
            BtnRecordIcon.Text = "⏹";
            BtnRecordText.Text = "Остановить запись (00:00)";

            _recordDurationTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _recordDurationTimer.Tick += (s, args) =>
            {
                BtnRecordText.Text = $"Остановить запись ({_recorder.Duration:mm\\:ss})";
            };
            _recordDurationTimer.Start();
        }
    }

    private async void BtnShutdown_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Вы уверены, что хотите выключить компьютеры всех подключённых учеников?",
            "Выключение компьютеров",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _server.BroadcastCommandAsync(CommandType.Shutdown);
        }
    }

    private async void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Вы уверены, что хотите перезагрузить компьютеры всех подключённых учеников?",
            "Перезагрузка компьютеров",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _server.BroadcastCommandAsync(CommandType.Restart);
        }
    }

    // ─── NAVIGATION ───

    private void ResetNavStyles()
    {
        DashboardContentArea.Visibility = Visibility.Collapsed;
        NotesContentArea.Visibility = Visibility.Collapsed;
        RecordingsContentArea.Visibility = Visibility.Collapsed;
        SettingsContentArea.Visibility = Visibility.Collapsed;

        var transparent = Brushes.Transparent;
        var textSecondary = (SolidColorBrush)FindResource("TextSecondaryBrush");

        NavDashboard.Background = transparent;
        NavDashboardText.Foreground = textSecondary;

        NavRecordings.Background = transparent;
        NavRecordingsText.Foreground = textSecondary;

        NavNotes.Background = transparent;
        NavNotesText.Foreground = textSecondary;

        NavSettings.Background = transparent;
        NavSettingsText.Foreground = textSecondary;
    }

    private void NavDashboard_Click(object sender, MouseButtonEventArgs e)
    {
        ResetNavStyles();
        DashboardContentArea.Visibility = Visibility.Visible;
        NavDashboard.Background = (SolidColorBrush)FindResource("AccentBlueMutedBrush");
        NavDashboardText.Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush");
    }

    private void NavRecordings_Click(object sender, MouseButtonEventArgs e)
    {
        ResetNavStyles();
        if (_recordingsView == null)
        {
            _recordingsView = new RecordingsView();
            RecordingsContentArea.Content = _recordingsView;
        }
        else
        {
            _recordingsView.LoadRecordings();
        }

        RecordingsContentArea.Visibility = Visibility.Visible;
        NavRecordings.Background = (SolidColorBrush)FindResource("AccentBlueMutedBrush");
        NavRecordingsText.Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush");
    }

    private void NavNotes_Click(object sender, MouseButtonEventArgs e)
    {
        ResetNavStyles();
        if (_notesView == null)
        {
            _notesView = new NotesView();
            NotesContentArea.Content = _notesView;
        }

        NotesContentArea.Visibility = Visibility.Visible;
        NavNotes.Background = (SolidColorBrush)FindResource("AccentBlueMutedBrush");
        NavNotesText.Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush");
    }

    private void NavSettings_Click(object sender, MouseButtonEventArgs e)
    {
        ResetNavStyles();
        if (_settingsView == null)
        {
            _settingsView = new SettingsView();
            SettingsContentArea.Content = _settingsView;
        }

        SettingsContentArea.Visibility = Visibility.Visible;
        NavSettings.Background = (SolidColorBrush)FindResource("AccentBlueMutedBrush");
        NavSettingsText.Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush");
    }

    // ─── CONTEXT MENU & FILE TRANSFER ───

    private string? GetStudentIdFromSender(object sender)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string id)
            return id;
        return null;
    }

    private async void ContextIdentify_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            await _server.SendCommandAsync(id, CommandType.Identify);
        }
    }

    private async void ContextDemo_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            var picker = new WindowPickerView { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true)
            {
                // Send demo specifically to this student
                // TODO: start capturing
                await _server.SendCommandAsync(id, CommandType.StartDemo);
            }
        }
    }

    private async void ContextSendMessage_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            var student = _studentManager.Students.FirstOrDefault(s => s.Id == id);
            var dlg = new SendMessageDialog(student?.Name) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                await _server.SendCommandAsync(id, CommandType.SendMessage, dlg.MessageText);
            }
        }
    }

    private async void ContextToggleLock_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            var student = _studentManager.Students.FirstOrDefault(s => s.Id == id);
            if (student != null)
            {
                bool newLockState = !student.IsLocked;
                _studentManager.SetStudentLock(id, newLockState);

                if (newLockState)
                    await _server.SendCommandAsync(id, CommandType.LockScreen, "Внимание на преподавателя!");
                else
                    await _server.SendCommandAsync(id, CommandType.UnlockScreen);
            }
        }
    }

    private async void ContextRefreshScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            await _server.SendCommandAsync(id, CommandType.RequestThumbnail);
        }
    }

    private async void ContextShutdown_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            var result = MessageBox.Show("Выключить этот компьютер?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                await _server.SendCommandAsync(id, CommandType.Shutdown);
        }
    }

    private async void ContextRestart_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            var result = MessageBox.Show("Перезагрузить этот компьютер?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                await _server.SendCommandAsync(id, CommandType.Restart);
        }
    }

    private async void ContextSendFile_Click(object sender, RoutedEventArgs e)
    {
        if (GetStudentIdFromSender(sender) is string id)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Выберите файл для отправки" };
            if (dialog.ShowDialog() == true)
            {
                await StartFileTransferAsync(id, dialog.FileName);
            }
        }
    }

    private async void StudentCard_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                // Find which student this drop occurred on
                var border = sender as Border;
                var student = border?.DataContext as ClassRoom_Control.Models.StudentInfo;
                if (student != null)
                {
                    await StartFileTransferAsync(student.Id, files[0]);
                }
            }
        }
    }

    private async System.Threading.Tasks.Task StartFileTransferAsync(string studentId, string filePath)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            var port = 9101; // Can be dynamic
            var teacherIp = NetworkHelper.GetLocalIpAddress();

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                fileName = fileInfo.Name,
                port = port,
                ip = teacherIp
            });

            // Start listening on a background task
            _ = System.Threading.Tasks.Task.Run(() => FileTransferService.SendFileAsync(filePath, port));

            // Tell the student to connect and download
            await _server.SendCommandAsync(studentId, CommandType.FileTransferOffer, payload);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при передаче файла: {ex.Message}");
        }
    }
}