using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ClassRoom_Control.Protocol;

namespace ClassRoom_Control.Services.Student;

public class UdpVideoReceiver : IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly FrameReassembler _reassembler = new();
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _isDisposed;

    public event EventHandler<CompleteFrameEventArgs>? FrameReceived;

    public UdpVideoReceiver(string multicastIp = NetworkConstants.MulticastAddress, int port = NetworkConstants.VideoPort)
    {
        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.ReceiveBufferSize = 2 * 1024 * 1024; // 2 MB buffer

        var localEp = new IPEndPoint(IPAddress.Any, port);
        _udpClient.Client.Bind(localEp);

        var mcastAddress = IPAddress.Parse(multicastIp);
        try
        {
            _udpClient.JoinMulticastGroup(mcastAddress);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to join multicast group: {ex.Message}");
        }

        _reassembler.CompleteFrameAssembled += (s, e) =>
        {
            FrameReceived?.Invoke(this, e);
        };
    }

    public void Start()
    {
        if (_receiveTask != null) return;

        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(ReceiveLoopAsync);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _udpClient.Close();
        }
        catch { }

        _reassembler.Reset();
        _receiveTask = null;
    }

    private async Task ReceiveLoopAsync()
    {
        var token = _cts?.Token ?? CancellationToken.None;

        while (!token.IsCancellationRequested && !_isDisposed)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(token);
                var data = result.Buffer;

                if (VideoPacketHeader.TryParse(data, out var header))
                {
                    int payloadOffset = VideoPacketHeader.HeaderSize;
                    _reassembler.ProcessPacket(header, data, payloadOffset, header.PayloadLength);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UDP Receive error: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
        _udpClient.Dispose();
    }
}
