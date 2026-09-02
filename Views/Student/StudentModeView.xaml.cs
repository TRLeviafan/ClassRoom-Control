using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClassRoom_Control.Configuration;
using ClassRoom_Control.Services.Common;
using ClassRoom_Control.Services.Student;
using ClassRoom_Control.Views.Setup;

namespace ClassRoom_Control.Views.Student;

public partial class StudentModeView : UserControl
{
    private readonly MainWindow _mainWindow;
    private readonly AppConfig _config;
    private readonly DiscoveryService _discovery = new();
    private readonly TcpCommandAgent _agent = new();
    private readonly DemoOverlay _demoOverlay = new();
    private UdpVideoReceiver? _videoReceiver;
    private H264Decoder? _videoDecoder;

    private bool _isConnecting = false;

    public StudentModeView(MainWindow mainWindow, AppConfig config)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _config = config;

        var studentName = string.IsNullOrWhiteSpace(config.StudentName) ? Environment.MachineName : config.StudentName;
        _agent.StudentName = studentName;
        _agent.StudentId = Environment.MachineName;

        MachineNameText.Text = studentName;

        SetupAgentEvents();

        Loaded += StudentModeView_Loaded;
        Unloaded += StudentModeView_Unloaded;
    }

    private void SetupAgentEvents()
    {
        _agent.Connected += () => Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = (SolidColorBrush)FindResource("StatusGreenBrush");
            StatusTitleText.Text = "Подключено к преподавателю";
            StatusSubtitleText.Text = "Команды принимаются в реальном времени";
            DemoStateText.Text = "Готов к приёму демонстрации";
        });

        _agent.Disconnected += () => Dispatcher.Invoke(() =>
        {
            StatusDot.Fill = (SolidColorBrush)FindResource("StatusRedBrush");
            StatusTitleText.Text = "Связь потеряна";
            StatusSubtitleText.Text = "Повторный поиск преподавателя в сети...";
            DemoStateText.Text = "Ожидание подключения";
            InputBlocker.SetLock(false);
            StopVideoReception();
            _demoOverlay.HideOverlay();

            // Restart discovery
            StartDiscovery();
        });

        _agent.DemoStarted += (payload) => Dispatcher.Invoke(() =>
        {
            DemoStateText.Text = "Идёт демонстрация экрана";
            InputBlocker.SetLock(true);
            StartVideoReception();
            _demoOverlay.ShowDemo();
        });

        _agent.DemoStopped += () => Dispatcher.Invoke(() =>
        {
            DemoStateText.Text = "Демонстрация завершена";
            InputBlocker.SetLock(false);
            StopVideoReception();
            _demoOverlay.HideOverlay();
        });

        _agent.ScreenLockRequested += (message) => Dispatcher.Invoke(() =>
        {
            DemoStateText.Text = "Экран заблокирован";
            InputBlocker.SetLock(true);
            _demoOverlay.ShowLock(message ?? "Внимание на преподавателя!");
        });

        _agent.ScreenUnlockRequested += () => Dispatcher.Invoke(() =>
        {
            DemoStateText.Text = "Готов к приёму демонстрации";
            InputBlocker.SetLock(false);
            _demoOverlay.HideOverlay();
        });

        _agent.InputLockRequested += () => Dispatcher.Invoke(() =>
        {
            InputBlocker.SetLock(true);
        });

        _agent.InputUnlockRequested += () => Dispatcher.Invoke(() =>
        {
            InputBlocker.SetLock(false);
        });

        _agent.ShutdownRequested += () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true });
            }
            catch { }
        };

        _agent.RestartRequested += () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true });
            }
            catch { }
        };

        _agent.MessageReceived += (message) => Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                var dlg = new StudentMessageDialog(message);
                dlg.Show();
            }
        });

        _agent.IdentifyRequested += () => Dispatcher.Invoke(async () =>
        {
            var oldText = DemoStateText.Text;
            DemoStateText.Text = "ПОИСК КОМПЬЮТЕРА: ЭТО " + _agent.StudentName;
            
            _demoOverlay.ShowLock($"Это компьютер:\n{_agent.StudentName}");
            await System.Threading.Tasks.Task.Delay(3000);
            _demoOverlay.HideOverlay();
            
            DemoStateText.Text = oldText;
        });

        _agent.FileTransferOfferReceived += async (payload) =>
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(payload);
                var fileName = doc.RootElement.GetProperty("fileName").GetString();
                var port = doc.RootElement.GetProperty("port").GetInt32();
                var teacherIp = doc.RootElement.GetProperty("ip").GetString();
                
                if (fileName != null && teacherIp != null)
                {
                    Dispatcher.Invoke(() => DemoStateText.Text = $"Приём файла: {fileName}...");
                    
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    var savePath = System.IO.Path.Combine(desktopPath, fileName);
                    
                    await FileTransferService.ReceiveFileAsync(savePath, teacherIp, port);
                    
                    Dispatcher.Invoke(() => DemoStateText.Text = $"Файл сохранён на Рабочий стол: {fileName}");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"Ошибка приёма файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        };

        _discovery.TeacherDiscovered += async (teacherIp, port) =>
        {
            if (_agent.IsConnected || _isConnecting) return;

            _isConnecting = true;
            try
            {
                await _agent.ConnectAsync(teacherIp, port);
            }
            catch
            {
                // Retry discovery later
            }
            finally
            {
                _isConnecting = false;
            }
        };
    }

    private void StartVideoReception()
    {
        StopVideoReception();
        try
        {
            _videoDecoder = new H264Decoder();
            _videoDecoder.FrameDecoded += (bitmap) =>
            {
                Dispatcher.InvokeAsync(() => _demoOverlay.UpdateFrame(bitmap));
            };

            _videoReceiver = new UdpVideoReceiver();
            _videoReceiver.FrameReceived += (s, e) =>
            {
                _videoDecoder?.DecodeFrame(e.FrameData);
            };
            _videoReceiver.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start video reception: {ex.Message}");
        }
    }

    private void StopVideoReception()
    {
        try
        {
            _videoReceiver?.Stop();
            _videoReceiver?.Dispose();
            _videoReceiver = null;

            _videoDecoder?.Dispose();
            _videoDecoder = null;
        }
        catch { }
    }

    private void StudentModeView_Loaded(object sender, RoutedEventArgs e)
    {
        StartDiscovery();
    }

    private void StudentModeView_Unloaded(object sender, RoutedEventArgs e)
    {
        InputBlocker.SetLock(false);
        StopVideoReception();
        _discovery.Stop();
        _agent.Disconnect();
        _demoOverlay.ForceClose();
    }

    private void StartDiscovery()
    {
        StatusDot.Fill = (SolidColorBrush)FindResource("StatusYellowBrush");
        StatusTitleText.Text = "Поиск преподавателя...";
        StatusSubtitleText.Text = "Авто-обнаружение через UDP broadcast в локальной сети";
        _discovery.StartStudentDiscovery();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.WindowState = WindowState.Minimized;
    }

    private void ChangeRole_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Сбросить роль этого компьютера и вернуться к выбору роли?",
            "Смена роли",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _config.Role = AppRole.None;
            ConfigService.Save(_config);

            _discovery.Stop();
            _agent.Disconnect();
            _demoOverlay.ForceClose();

            _mainWindow.NavigateTo(new RoleSelectView(_mainWindow));
        }
    }
}