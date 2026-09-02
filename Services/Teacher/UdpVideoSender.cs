using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ClassRoom_Control.Protocol;

namespace ClassRoom_Control.Services.Teacher;

public class UdpVideoSender : IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _multicastEndPoint;
    private uint _currentFrameIndex;
    private bool _isDisposed;

    // Optional unicast targets for targeted single-student streaming
    private IPEndPoint? _unicastTarget;

    public UdpVideoSender(string multicastIp = NetworkConstants.MulticastAddress, int port = NetworkConstants.VideoPort)
    {
        _udpClient = new UdpClient();
        _udpClient.Client.SendBufferSize = 1024 * 1024; // 1 MB buffer
        _udpClient.EnableBroadcast = true;
        _udpClient.MulticastLoopback = true;

        var ip = IPAddress.Parse(multicastIp);
        _udpClient.JoinMulticastGroup(ip);
        _multicastEndPoint = new IPEndPoint(ip, port);
    }

    public void SetUnicastTarget(string? ipAddress, int port = NetworkConstants.VideoPort)
    {
        if (string.IsNullOrEmpty(ipAddress))
        {
            _unicastTarget = null;
        }
        else
        {
            _unicastTarget = new IPEndPoint(IPAddress.Parse(ipAddress), port);
        }
    }

    public async Task SendFrameAsync(byte[] h264Frame, bool isKeyFrame, uint timestampMs)
    {
        if (_isDisposed || h264Frame.Length == 0) return;

        uint frameIndex = _currentFrameIndex++;
        int totalLength = h264Frame.Length;
        int maxPayload = VideoPacketHeader.MaxPayloadSize;
        ushort fragmentCount = (ushort)((totalLength + maxPayload - 1) / maxPayload);

        var flags = isKeyFrame ? VideoPacketFlags.KeyFrame : VideoPacketFlags.None;
        if (_unicastTarget != null)
        {
            flags |= VideoPacketFlags.Unicast;
        }

        var targetEndPoint = _unicastTarget ?? _multicastEndPoint;

        // Buffer for single packet (Header + Payload)
        byte[] packetBuffer = new byte[VideoPacketHeader.HeaderSize + maxPayload];

        for (ushort fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
        {
            int offset = fragmentIndex * maxPayload;
            int payloadSize = Math.Min(maxPayload, totalLength - offset);

            var currentFlags = flags;
            if (fragmentIndex == fragmentCount - 1)
            {
                currentFlags |= VideoPacketFlags.LastFragment;
            }

            var header = new VideoPacketHeader(
                frameIndex,
                fragmentIndex,
                fragmentCount,
                currentFlags,
                (ushort)payloadSize,
                timestampMs);

            header.WriteTo(packetBuffer.AsSpan(0, VideoPacketHeader.HeaderSize));
            Buffer.BlockCopy(h264Frame, offset, packetBuffer, VideoPacketHeader.HeaderSize, payloadSize);

            int packetSize = VideoPacketHeader.HeaderSize + payloadSize;
            try
            {
                await _udpClient.SendAsync(packetBuffer, packetSize, targetEndPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UDP Video Send error: {ex.Message}");
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _udpClient.DropMulticastGroup(_multicastEndPoint.Address);
            _udpClient.Close();
            _udpClient.Dispose();
        }
        catch { }
    }
}
