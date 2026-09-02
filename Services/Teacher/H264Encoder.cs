using System;
using System.Linq;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

namespace ClassRoom_Control.Services.Teacher;

public class EncodedFrameEventArgs : EventArgs
{
    public byte[] Data { get; }
    public bool IsKeyFrame { get; }
    public long Timestamp100Ns { get; }

    public EncodedFrameEventArgs(byte[] data, bool isKeyFrame, long timestamp)
    {
        Data = data;
        IsKeyFrame = isKeyFrame;
        Timestamp100Ns = timestamp;
    }
}

public class H264Encoder : IDisposable
{
    private static bool _mfStarted;
    private static readonly object _mfInitLock = new();

    private IMFTransform? _transform;
    private int _width;
    private int _height;
    private int _frameRate;
    private int _bitRate;
    private long _frameIndex;
    private readonly long _frameDuration100Ns;

    private ID3D11Device _device;
    private ID3D11DeviceContext _context;
    private ID3D11Texture2D? _stagingTexture;
    private byte[]? _nv12Buffer;

    public event EventHandler<EncodedFrameEventArgs>? FrameEncoded;

    public H264Encoder(ID3D11Device device, ID3D11DeviceContext context, int width, int height, int fps = 30, int bitrate = 4_000_000)
    {
        _device = device;
        _context = context;
        _width = (width % 2 == 0) ? width : width - 1;
        _height = (height % 2 == 0) ? height : height - 1;
        _frameRate = fps;
        _bitRate = bitrate;
        _frameDuration100Ns = 10_000_000L / _frameRate;

        EnsureMediaFoundationStarted();
        InitializeEncoder();
    }

    private static void EnsureMediaFoundationStarted()
    {
        lock (_mfInitLock)
        {
            if (!_mfStarted)
            {
                MediaFactory.MFStartup().CheckError();
                _mfStarted = true;
            }
        }
    }

    private void InitializeEncoder()
    {
        var outTypeInfo = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.H264
        };

        const uint MFT_ENUM_FLAG_SYNCMFT = 0x00000001;
        const uint MFT_ENUM_FLAG_HARDWARE = 0x00000004;
        const uint MFT_ENUM_FLAG_SORTANDFILTER = 0x00000040;

        // 1. Try Hardware Encoder first
        var activateArray = MediaFactory.MFTEnumEx(
            TransformCategoryGuids.VideoEncoder,
            MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
            null,
            outTypeInfo);

        // 2. Fallback to Software/Sync MFT
        if (activateArray == null || !activateArray.Any())
        {
            activateArray = MediaFactory.MFTEnumEx(
                TransformCategoryGuids.VideoEncoder,
                MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_SORTANDFILTER,
                null,
                outTypeInfo);
        }

        if (activateArray == null || !activateArray.Any())
        {
            throw new PlatformNotSupportedException("Не найден подходящий H.264 видео-кодировщик в системе.");
        }

        var firstActivate = activateArray.First();
        _transform = firstActivate.ActivateObject<IMFTransform>();
        foreach (var act in activateArray)
        {
            act.Dispose();
        }

        // Configure Output Media Type (H.264, Low Latency, CBR)
        using var outputType = MediaFactory.MFCreateMediaType();
        outputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        outputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
        outputType.Set(MediaTypeAttributeKeys.AvgBitrate, _bitRate);
        MediaFactory.MFSetAttributeSize(outputType, MediaTypeAttributeKeys.FrameSize, (uint)_width, (uint)_height);
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.FrameRate, (uint)_frameRate, 1);
        MediaFactory.MFSetAttributeRatio(outputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        outputType.Set(MediaTypeAttributeKeys.InterlaceMode, (int)VideoInterlaceMode.Progressive);

        _transform.SetOutputType(0, outputType, 0);

        // Configure Input Media Type (NV12)
        using var inputType = MediaFactory.MFCreateMediaType();
        inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        MediaFactory.MFSetAttributeSize(inputType, MediaTypeAttributeKeys.FrameSize, (uint)_width, (uint)_height);
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.FrameRate, (uint)_frameRate, 1);
        MediaFactory.MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        inputType.Set(MediaTypeAttributeKeys.InterlaceMode, (int)VideoInterlaceMode.Progressive);

        _transform.SetInputType(0, inputType, 0);

        // Notify streaming begin
        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        _transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);

        _nv12Buffer = new byte[_width * _height * 3 / 2];
    }

    public void EncodeTexture(ID3D11Texture2D texture)
    {
        if (_transform == null || _nv12Buffer == null) return;

        var desc = texture.Description;
        if (_stagingTexture == null || _stagingTexture.Description.Width != desc.Width || _stagingTexture.Description.Height != desc.Height)
        {
            _stagingTexture?.Dispose();
            var stagingDesc = new Texture2DDescription
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            };
            _stagingTexture = _device.CreateTexture2D(stagingDesc);
        }

        // Copy captured frame texture to CPU-readable staging texture
        _context.CopyResource(_stagingTexture, texture);

        var mapped = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            // Convert BGRA to NV12 in _nv12Buffer
            ConvertBgraToNv12(mapped.DataPointer, (int)mapped.RowPitch, (int)desc.Width, (int)desc.Height, _nv12Buffer, _width, _height);
        }
        finally
        {
            _context.Unmap(_stagingTexture, 0);
        }

        // Feed NV12 sample into encoder
        long sampleTime = _frameIndex * _frameDuration100Ns;
        _frameIndex++;

        using var mediaBuffer = MediaFactory.MFCreateMemoryBuffer((int)_nv12Buffer.Length);
        mediaBuffer.Lock(out IntPtr bufferPtr, out _, out _);
        try
        {
            Marshal.Copy(_nv12Buffer, 0, bufferPtr, _nv12Buffer.Length);
        }
        finally
        {
            mediaBuffer.Unlock();
        }
        mediaBuffer.CurrentLength = (int)_nv12Buffer.Length;

        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(mediaBuffer);
        sample.SampleTime = sampleTime;
        sample.SampleDuration = _frameDuration100Ns;

        _transform.ProcessInput(0, sample, 0);

        // Retrieve encoded H.264 output samples
        DrainOutput();
    }

    private void DrainOutput()
    {
        if (_transform == null) return;

        using var outBuffer = MediaFactory.MFCreateMemoryBuffer(_width * _height);
        using var outSample = MediaFactory.MFCreateSample();
        outSample.AddBuffer(outBuffer);

        var outputBuffer = new OutputDataBuffer
        {
            StreamID = 0,
            Sample = outSample,
            Status = 0,
            Events = null!
        };

        while (true)
        {
            var hr = _transform.ProcessOutput(ProcessOutputFlags.None, 1, ref outputBuffer, out var status);
            if (hr.Failure)
            {
                break; // No more samples or needs more input
            }

            var processedSample = outputBuffer.Sample;
            if (processedSample != null)
            {
                using var mergedBuffer = processedSample.ConvertToContiguousBuffer();
                mergedBuffer.Lock(out IntPtr pData, out _, out int currentLength);
                try
                {
                    if (currentLength > 0)
                    {
                        byte[] encodedData = new byte[currentLength];
                        Marshal.Copy(pData, encodedData, 0, currentLength);

                        bool isKeyFrame = IsH264KeyFrame(encodedData);

                        FrameEncoded?.Invoke(this, new EncodedFrameEventArgs(encodedData, isKeyFrame, processedSample.SampleTime));
                    }
                }
                finally
                {
                    mergedBuffer.Unlock();
                }
            }
        }
    }

    private static bool IsH264KeyFrame(byte[] data)
    {
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && (data[i + 2] == 1 || (data[i + 2] == 0 && data[i + 3] == 1)))
            {
                int nalStart = (data[i + 2] == 1) ? i + 3 : i + 4;
                if (nalStart < data.Length)
                {
                    int nalType = data[nalStart] & 0x1F;
                    if (nalType == 5 || nalType == 7) // IDR slice (5) or SPS (7)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static unsafe void ConvertBgraToNv12(
        IntPtr srcBgra, int srcStride, int srcWidth, int srcHeight,
        byte[] dstNv12, int dstWidth, int dstHeight)
    {
        fixed (byte* pDst = dstNv12)
        {
            byte* pY = pDst;
            byte* pUV = pDst + (dstWidth * dstHeight);

            byte* pSrc = (byte*)srcBgra.ToPointer();
            int copyWidth = Math.Min(srcWidth, dstWidth);
            int copyHeight = Math.Min(srcHeight, dstHeight);

            for (int y = 0; y < copyHeight; y++)
            {
                byte* srcRow = pSrc + (y * srcStride);
                byte* yRow = pY + (y * dstWidth);
                byte* uvRow = pUV + ((y / 2) * dstWidth);

                bool isEvenRow = (y % 2 == 0);

                for (int x = 0; x < copyWidth; x++)
                {
                    int srcOffset = x * 4;
                    byte b = srcRow[srcOffset];
                    byte g = srcRow[srcOffset + 1];
                    byte r = srcRow[srcOffset + 2];

                    // Rec.601 color conversion: RGB -> YUV
                    int yVal = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                    yRow[x] = (byte)Math.Clamp(yVal, 16, 235);

                    if (isEvenRow && (x % 2 == 0))
                    {
                        int uVal = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                        int vVal = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

                        uvRow[x] = (byte)Math.Clamp(uVal, 16, 240);
                        uvRow[x + 1] = (byte)Math.Clamp(vVal, 16, 240);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _transform?.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
            _transform?.Dispose();
            _transform = null;
        }
        catch { }

        _stagingTexture?.Dispose();
        _stagingTexture = null;
    }
}
