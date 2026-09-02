using System;
using System.Collections.Generic;
using ClassRoom_Control.Protocol;

namespace ClassRoom_Control.Services.Student;

public class CompleteFrameEventArgs : EventArgs
{
    public byte[] FrameData { get; }
    public bool IsKeyFrame { get; }
    public uint Timestamp { get; }

    public CompleteFrameEventArgs(byte[] frameData, bool isKeyFrame, uint timestamp)
    {
        FrameData = frameData;
        IsKeyFrame = isKeyFrame;
        Timestamp = timestamp;
    }
}

public class FrameReassembler
{
    private class PendingFrame
    {
        public uint FrameIndex { get; set; }
        public ushort FragmentCount { get; set; }
        public int ReceivedCount { get; set; }
        public bool IsKeyFrame { get; set; }
        public uint Timestamp { get; set; }
        public byte[][] Fragments { get; set; }
        public int TotalBytes { get; set; }

        public PendingFrame(uint frameIndex, ushort fragmentCount, bool isKeyFrame, uint timestamp)
        {
            FrameIndex = frameIndex;
            FragmentCount = fragmentCount;
            ReceivedCount = 0;
            IsKeyFrame = isKeyFrame;
            Timestamp = timestamp;
            Fragments = new byte[fragmentCount][];
            TotalBytes = 0;
        }
    }

    private PendingFrame? _currentFrame;
    private uint _lastCompletedFrameIndex;
    private bool _hasReceivedKeyFrame;

    public event EventHandler<CompleteFrameEventArgs>? CompleteFrameAssembled;

    public void ProcessPacket(in VideoPacketHeader header, byte[] buffer, int offset, int length)
    {
        bool isKeyFrame = (header.Flags & VideoPacketFlags.KeyFrame) != 0;

        // Wait for first keyframe to start decoding clean video without artifacts
        if (!_hasReceivedKeyFrame)
        {
            if (!isKeyFrame)
                return;

            _hasReceivedKeyFrame = true;
        }

        // Drop packets from old frames that already passed
        if (_currentFrame != null && header.FrameIndex < _currentFrame.FrameIndex)
            return;

        // If new frame arrives, abandon old incomplete frame
        if (_currentFrame == null || header.FrameIndex > _currentFrame.FrameIndex)
        {
            _currentFrame = new PendingFrame(header.FrameIndex, header.FragmentCount, isKeyFrame, header.Timestamp);
        }

        if (header.FragmentIndex < _currentFrame.FragmentCount && _currentFrame.Fragments[header.FragmentIndex] == null)
        {
            byte[] fragBytes = new byte[length];
            Buffer.BlockCopy(buffer, offset, fragBytes, 0, length);
            _currentFrame.Fragments[header.FragmentIndex] = fragBytes;
            _currentFrame.ReceivedCount++;
            _currentFrame.TotalBytes += fragBytes.Length;

            // Check if all fragments of the frame arrived
            if (_currentFrame.ReceivedCount == _currentFrame.FragmentCount)
            {
                byte[] assembledFrame = new byte[_currentFrame.TotalBytes];
                int writeOffset = 0;
                for (int i = 0; i < _currentFrame.FragmentCount; i++)
                {
                    var chunk = _currentFrame.Fragments[i];
                    if (chunk != null)
                    {
                        Buffer.BlockCopy(chunk, 0, assembledFrame, writeOffset, chunk.Length);
                        writeOffset += chunk.Length;
                    }
                }

                _lastCompletedFrameIndex = _currentFrame.FrameIndex;
                CompleteFrameAssembled?.Invoke(this, new CompleteFrameEventArgs(
                    assembledFrame,
                    _currentFrame.IsKeyFrame,
                    _currentFrame.Timestamp));

                _currentFrame = null;
            }
        }
    }

    public void Reset()
    {
        _currentFrame = null;
        _hasReceivedKeyFrame = false;
    }
}
