using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassRoom_Control.Protocol;

namespace ClassRoom_Control.Services.Student;

public class TcpCommandAgent : IDisposable
{
    private TcpClient? _client;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private bool _isDisposed = false;

    public string StudentId { get; set; } = Guid.NewGuid().ToString();
    public string StudentName { get; set; } = Environment.MachineName;

    public bool IsConnected => _client != null && _client.Connected;

    // Events for UI / handlers to subscribe to
    public event Action<string?>? DemoStarted;
    public event Action? DemoStopped;
    public event Action<string?>? ScreenLockRequested;
    public event Action? ScreenUnlockRequested;
    public event Action? InputLockRequested;
    public event Action? InputUnlockRequested;
    public event Action? ShutdownRequested;
    public event Action? RestartRequested;
    public event Action<string?>? MessageReceived;
    public event Action? IdentifyRequested;
    public event Action<string?>? FileTransferOfferReceived;
    public event Action? Connected;
    public event Action? Disconnected;

    public async Task ConnectAsync(string teacherIp, int port = NetworkConstants.CommandPort)
    {
        Disconnect();
        _cts = new CancellationTokenSource();

        try
        {
            _client = new TcpClient();
            _client.NoDelay = true;
            await _client.ConnectAsync(teacherIp, port, _cts.Token);

            var stream = _client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // Send registration
            var regMsg = CommandMessage.Create(CommandType.Register, StudentId, StudentName);
            await _writer.WriteLineAsync(regMsg.ToJson().AsMemory(), _cts.Token);

            Connected?.Invoke();

            // Start background command listener
            _ = Task.Run(() => ReadCommandsAsync(reader, _cts.Token), _cts.Token);

            // Start heartbeat sender
            _ = Task.Run(() => SendHeartbeatsAsync(_cts.Token), _cts.Token);
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    private async Task ReadCommandsAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null) break;

                var message = CommandMessage.FromJson(line);
                if (message == null) continue;

                InputBlocker.RefreshWatchdog();

                switch (message.Type)
                {
                    case CommandType.StartDemo:
                        DemoStarted?.Invoke(message.Payload);
                        break;
                    case CommandType.StopDemo:
                        DemoStopped?.Invoke();
                        break;
                    case CommandType.LockScreen:
                        ScreenLockRequested?.Invoke(message.Payload);
                        break;
                    case CommandType.UnlockScreen:
                        ScreenUnlockRequested?.Invoke();
                        break;
                    case CommandType.LockInput:
                        InputLockRequested?.Invoke();
                        break;
                    case CommandType.UnlockInput:
                        InputUnlockRequested?.Invoke();
                        break;
                    case CommandType.Shutdown:
                        ShutdownRequested?.Invoke();
                        break;
                    case CommandType.Restart:
                        RestartRequested?.Invoke();
                        break;
                    case CommandType.SendMessage:
                        MessageReceived?.Invoke(message.Payload);
                        break;
                    case CommandType.Identify:
                        IdentifyRequested?.Invoke();
                        break;
                    case CommandType.FileTransferOffer:
                        FileTransferOfferReceived?.Invoke(message.Payload);
                        break;
                    case CommandType.RequestThumbnail:
                        _ = Task.Run(async () =>
                        {
                            var base64 = ScreenShotService.CaptureThumbnailBase64();
                            if (!string.IsNullOrEmpty(base64) && _writer != null)
                            {
                                var resp = CommandMessage.Create(CommandType.ResponseThumbnail, StudentId, base64);
                                try
                                {
                                    await _writer.WriteLineAsync(resp.ToJson().AsMemory(), token);
                                }
                                catch { }
                            }
                        }, token);
                        break;
                }
            }
        }
        catch { }
        finally
        {
            Disconnect();
        }
    }

    private async Task SendHeartbeatsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsConnected)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(NetworkConstants.HeartbeatIntervalSeconds), token);
                if (_writer != null && IsConnected)
                {
                    var hb = CommandMessage.Create(CommandType.Heartbeat, StudentId, StudentName);
                    await _writer.WriteLineAsync(hb.ToJson().AsMemory(), token);
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                Disconnect();
                break;
            }
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _writer?.Dispose();
        _writer = null;

        _client?.Close();
        _client?.Dispose();
        _client = null;

        Disconnected?.Invoke();
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Disconnect();
            _isDisposed = true;
        }
    }
}