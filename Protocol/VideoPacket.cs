using System;
using System.Buffers.Binary;

namespace ClassRoom_Control.Protocol;

[Flags]
public enum VideoPacketFlags : byte
{
    None = 0,
    KeyFrame = 1 << 0,
    Unicast = 1 << 1,
    LastFragment = 1 << 2
}

public readonly struct VideoPacketHeader
{
    public const ushort MagicValue = 0x5644; // 'VD' (Video Data)
    public const int HeaderSize = 18;
    public const int MaxPayloadSize = 1360;

    public ushort Magic { get; }
    public uint FrameIndex { get; }
    public ushort FragmentIndex { get; }
    public ushort FragmentCount { get; }
    public VideoPacketFlags Flags { get; }
    public byte Reserved { get; }
    public ushort PayloadLength { get; }
    public uint Timestamp { get; }

    public VideoPacketHeader(
        uint frameIndex,
        ushort fragmentIndex,
        ushort fragmentCount,
        VideoPacketFlags flags,
        ushort payloadLength,
        uint timestamp)
    {
        Magic = MagicValue;
        FrameIndex = frameIndex;
        FragmentIndex = fragmentIndex;
        FragmentCount = fragmentCount;
        Flags = flags;
        Reserved = 0;
        PayloadLength = payloadLength;
        Timestamp = timestamp;
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination span is too small for VideoPacketHeader.", nameof(destination));

        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(0, 2), Magic);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(2, 4), FrameIndex);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(6, 2), FragmentIndex);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(8, 2), FragmentCount);
        destination[10] = (byte)Flags;
        destination[11] = Reserved;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(12, 2), PayloadLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(14, 4), Timestamp);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out VideoPacketHeader header)
    {
        header = default;
        if (source.Length < HeaderSize)
            return false;

        ushort magic = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(0, 2));
        if (magic != MagicValue)
            return false;

        uint frameIndex = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(2, 4));
        ushort fragmentIndex = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(6, 2));
        ushort fragmentCount = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(8, 2));
        var flags = (VideoPacketFlags)source[10];
        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(12, 2));
        uint timestamp = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(14, 4));

        if (source.Length < HeaderSize + payloadLength)
            return false;

        header = new VideoPacketHeader(frameIndex, fragmentIndex, fragmentCount, flags, payloadLength, timestamp);
        return true;
    }
}
