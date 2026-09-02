using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.MediaFoundation;

namespace ClassRoom_Control.Services.Student;

public class H264Decoder : IDisposable
{
    private static bool _mfStarted;
    private static readonly object _mfInitLock = new();

    private IMFTransform? _transform;
    private int _width;
    private int _height;
    private WriteableBitmap? _bitmap;
    private byte[]? _rgbBuffer;
    private bool _isDisposed;

    public event Action<WriteableBitmap>? FrameDecoded;

    public H264Decoder()
    {
        EnsureMediaFoundationStarted();
        InitializeDecoder();
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

    private void InitializeDecoder()
    {
        var inTypeInfo = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.H264
        };

        const uint MFT_ENUM_FLAG_SYNCMFT = 0x00000001;
        const uint MFT_ENUM_FLAG_HARDWARE = 0x00000004;
        const uint MFT_ENUM_FLAG_SORTANDFILTER = 0x00000040;

        // Try hardware decoder first, fallback to software
        var activateArray = MediaFactory.MFTEnumEx(
            TransformCategoryGuids.VideoDecoder,
            MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
            inTypeInfo,
            null);

        if (activateArray == null || !activateArray.Any())
        {
            activateArray = MediaFactory.MFTEnumEx(
                TransformCategoryGuids.VideoDecoder,
                MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_SORTANDFILTER,
                inTypeInfo,
                null);
        }

        if (activateArray == null || !activateArray.Any())
        {
            throw new PlatformNotSupportedException("Не найден H.264 видео-декодер в системе.");
        }

        var first = activateArray.First();
        _transform = first.ActivateObject<IMFTransform>();
        foreach (var act in activateArray)
        {
            act.Dispose();
        }

        // Configure Input Type (H.264)
        using var inputType = MediaFactory.MFCreateMediaType();
        inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
        _transform.SetInputType(0, inputType, 0);

        // Configure Output Type (NV12)
        ConfigureOutputType();

        // Notify stream begin
        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        _transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
    }

    private void ConfigureOutputType()
    {
        if (_transform == null) return;

        try
        {
            using var outType = MediaFactory.MFCreateMediaType();
            outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            _transform.SetOutputType(0, outType, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set output type: {ex.Message}");
        }
    }

    public void DecodeFrame(byte[] h264Data)
    {
        if (_transform == null || _isDisposed || h264Data.Length == 0) return;

        using var mediaBuffer = MediaFactory.MFCreateMemoryBuffer(h264Data.Length);
        mediaBuffer.Lock(out IntPtr pBuffer, out _, out _);
        try
        {
            Marshal.Copy(h264Data, 0, pBuffer, h264Data.Length);
        }
        finally
        {
            mediaBuffer.Unlock();
        }
        mediaBuffer.CurrentLength = h264Data.Length;

        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(mediaBuffer);

        try
        {
            _transform.ProcessInput(0, sample, 0);
        }
        catch
        {
            return;
        }

        DrainOutput();
    }

    private void DrainOutput()
    {
        if (_transform == null) return;

        int cbSize = 1920 * 1080 * 4; // 8 MB buffer for 1080p frame

        using var outBuffer = MediaFactory.MFCreateMemoryBuffer(cbSize);
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
            var hr = _transform.ProcessOutput(ProcessOutputFlags.None, 1, ref outputBuffer, out _);

            // Check if stream format changed (e.g. resolution detected from SPS header)
            if (hr.Code == -1072870047) // MF_E_TRANSFORM_STREAM_CHANGE (0xC00D6D61)
            {
                ConfigureOutputType();
                continue;
            }

            if (hr.Failure)
            {
                break;
            }

            var processedSample = outputBuffer.Sample;
            if (processedSample != null)
            {
                ProcessDecodedSample(processedSample);
            }
        }
    }

    private void ProcessDecodedSample(IMFSample sample)
    {
        using var currentType = _transform?.GetOutputCurrentType(0);
        if (currentType == null) return;

        MediaFactory.MFGetAttributeSize(currentType, MediaTypeAttributeKeys.FrameSize, out uint width, out uint height);
        if (width == 0 || height == 0) return;

        int w = (int)width;
        int h = (int)height;

        using var mergedBuffer = sample.ConvertToContiguousBuffer();
        mergedBuffer.Lock(out IntPtr pData, out _, out int currentLength);
        try
        {
            if (currentLength > 0)
            {
                EnsureBitmapAndBuffer(w, h);
                if (_bitmap != null && _rgbBuffer != null)
                {
                    // Convert NV12 to BGRA32 in _rgbBuffer
                    ConvertNv12ToBgra(pData, w, h, _rgbBuffer);

                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (_bitmap != null)
                        {
                            _bitmap.Lock();
                            Marshal.Copy(_rgbBuffer, 0, _bitmap.BackBuffer, _rgbBuffer.Length);
                            _bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
                            _bitmap.Unlock();

                            FrameDecoded?.Invoke(_bitmap);
                        }
                    });
                }
            }
        }
        finally
        {
            mergedBuffer.Unlock();
        }
    }

    private void EnsureBitmapAndBuffer(int w, int h)
    {
        if (_bitmap == null || _width != w || _height != h)
        {
            _width = w;
            _height = h;
            _rgbBuffer = new byte[w * h * 4];

            Application.Current.Dispatcher.Invoke(() =>
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            });
        }
    }

    private static unsafe void ConvertNv12ToBgra(IntPtr srcNv12, int width, int height, byte[] dstBgra)
    {
        byte* pSrc = (byte*)srcNv12.ToPointer();
        byte* pY = pSrc;
        byte* pUV = pSrc + (width * height);

        fixed (byte* pDst = dstBgra)
        {
            for (int y = 0; y < height; y++)
            {
                byte* yRow = pY + (y * width);
                byte* uvRow = pUV + ((y / 2) * width);
                byte* dstRow = pDst + (y * width * 4);

                for (int x = 0; x < width; x++)
                {
                    int yVal = yRow[x] - 16;
                    int uvIdx = (x / 2) * 2;
                    int uVal = uvRow[uvIdx] - 128;
                    int vVal = uvRow[uvIdx + 1] - 128;

                    // Fast integer YUV to RGB (Rec.601)
                    int c = Math.Max(0, yVal * 298);
                    int r = (c + 409 * vVal + 128) >> 8;
                    int g = (c - 100 * uVal - 208 * vVal + 128) >> 8;
                    int b = (c + 516 * uVal + 128) >> 8;

                    int dstOffset = x * 4;
                    dstRow[dstOffset] = (byte)Math.Clamp(b, 0, 255);       // B
                    dstRow[dstOffset + 1] = (byte)Math.Clamp(g, 0, 255);   // G
                    dstRow[dstOffset + 2] = (byte)Math.Clamp(r, 0, 255);   // R
                    dstRow[dstOffset + 3] = 255;                           // A
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _transform?.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
            _transform?.Dispose();
            _transform = null;
        }
        catch { }
    }
}
