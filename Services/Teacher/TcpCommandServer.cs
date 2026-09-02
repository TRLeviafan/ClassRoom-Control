using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassRoom_Control.Protocol;

namespace ClassRoom_Control.Services.Teacher;

public class TcpCommandServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, (TcpClient client, StreamWriter writer)> _clients = new();
    private readonly StudentManager _studentManager;
    private bool _isDisposed = false;

    public StudentManager StudentManager => _studentManager;

    public event Action<string, CommandMessage>? MessageReceived;

    public TcpCommandServer(StudentManager studentManager)
    {
        _studentManager = studentManager;
    }

    public void Start(int port = NetworkConstants.CommandPort)
    {
        Stop();
        _cts = new CancellationTokenSource();

        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();

            // Accept clients loop
            Task.Run(async () =>
            {
                var token = _cts.Token;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync(token);
                        _ = HandleClientAsync(client, token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, _cts.Token);

            // Stale check loop
            Task.Run(async () =>
            {
                var token = _cts.Token;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(3000, token);
                        _studentManager.CheckStaleStudents();
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, _cts.Token);
        }
        catch { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        string currentStudentId = string.Empty;

        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();

            while (!token.IsCancellationRequested && client.Connected)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null) break;

                var message = CommandMessage.FromJson(line);
                if (message == null) continue;

                currentStudentId = message.SenderId;

                switch (message.Type)
                {
                    case CommandType.Register:
                        _clients[currentStudentId] = (client, writer);
                        _studentManager.RegisterStudent(currentStudentId, message.SenderName, clientIp);

                        var ack = CommandMessage.Create(CommandType.RegisterAck, "TEACHER", "Преподаватель", null, currentStudentId);
                        await writer.WriteLineAsync(ack.ToJson().AsMemory(), token);
                        break;

                    case CommandType.Heartbeat:
                        _studentManager.UpdateHeartbeat(currentStudentId, false, false);
                        var hbAck = CommandMessage.Create(CommandType.HeartbeatAck, "TEACHER", "Преподаватель", null, currentStudentId);
                        await writer.WriteLineAsync(hbAck.ToJson().AsMemory(), token);
                        break;

                    case CommandType.ResponseThumbnail:
                        if (!string.IsNullOrEmpty(message.Payload))
                        {
                            _studentManager.UpdateThumbnail(currentStudentId, message.Payload);
                        }
                        break;

                    default:
                        MessageReceived?.Invoke(currentStudentId, message);
                        break;
                }
            }
        }
        catch { }
        finally
        {
            if (!string.IsNullOrEmpty(currentStudentId))
            {
                _clients.TryRemove(currentStudentId, out _);
                _studentManager.MarkOffline(currentStudentId);
            }
            client.Close();
        }
    }

    public async Task BroadcastCommandAsync(CommandType type, string? payload = null)
    {
        var msg = CommandMessage.Create(type, "TEACHER", "Преподаватель", payload);
        var json = msg.ToJson();

        foreach (var (id, entry) in _clients)
        {
            try
            {
                await entry.writer.WriteLineAsync(json);
            }
            catch
            {
                _clients.TryRemove(id, out _);
                _studentManager.MarkOffline(id);
            }
        }
    }

    public async Task SendCommandAsync(string studentId, CommandType type, string? payload = null)
    {
        if (_clients.TryGetValue(studentId, out var entry))
        {
            try
            {
                var msg = CommandMessage.Create(type, "TEACHER", "Преподаватель", payload, studentId);
                await entry.writer.WriteLineAsync(msg.ToJson());
            }
            catch
            {
                _clients.TryRemove(studentId, out _);
                _studentManager.MarkOffline(studentId);
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        foreach (var (_, entry) in _clients)
        {
            try { entry.client.Close(); } catch { }
        }
        _clients.Clear();

        _listener?.Stop();
        _listener = null;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _isDisposed = true;
        }
    }
}