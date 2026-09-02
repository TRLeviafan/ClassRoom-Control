using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassRoom_Control.Protocol;

namespace ClassRoom_Control.Services.Common;

public class DiscoveryService : IDisposable
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private bool _isDisposed = false;

    // Fired on Student when Teacher is discovered
    public event Action<string, int>? TeacherDiscovered;

    // ─── TEACHER MODE: Listens for students looking for teacher ───
    public void StartTeacherListener(int commandPort = NetworkConstants.CommandPort)
    {
        Stop();
        _cts = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, NetworkConstants.DiscoveryPort));

            Task.Run(async () =>
            {
                var token = _cts.Token;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _udpClient.ReceiveAsync(token);
                        var message = Encoding.UTF8.GetString(result.Buffer);

                        if (message.StartsWith("DISCOVER_TEACHER"))
                        {
                            // Send reply back to the student
                            var response = $"TEACHER_HERE:{commandPort}";
                            var bytes = Encoding.UTF8.GetBytes(response);
                            await _udpClient.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, _cts.Token);
        }
        catch { }
    }

    // ─── STUDENT MODE: Broadcasts request to find teacher ───
    public void StartStudentDiscovery()
    {
        Stop();
        _cts = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient();
            _udpClient.EnableBroadcast = true;
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Listener loop for Teacher responses
            Task.Run(async () =>
            {
                var token = _cts.Token;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _udpClient.ReceiveAsync(token);
                        var response = Encoding.UTF8.GetString(result.Buffer);

                        if (response.StartsWith("TEACHER_HERE:"))
                        {
                            var parts = response.Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                            {
                                var teacherIp = result.RemoteEndPoint.Address.ToString();
                                TeacherDiscovered?.Invoke(teacherIp, port);
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, _cts.Token);

            // Periodic broadcaster loop
            Task.Run(async () =>
            {
                var token = _cts.Token;
                var broadcastEp = new IPEndPoint(IPAddress.Broadcast, NetworkConstants.DiscoveryPort);
                var requestBytes = Encoding.UTF8.GetBytes("DISCOVER_TEACHER");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await _udpClient.SendAsync(requestBytes, requestBytes.Length, broadcastEp);
                        await Task.Delay(TimeSpan.FromSeconds(NetworkConstants.DiscoveryIntervalSeconds), token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, _cts.Token);
        }
        catch { }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
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