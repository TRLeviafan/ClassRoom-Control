using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ClassRoom_Control.Services.Teacher;

public class PreviewImageHelper : IDisposable
{
    private ID3D11Device _device;
    private ID3D11DeviceContext _context;
    private ID3D11Texture2D? _stagingTexture;
    private WriteableBitmap? _bitmap;
    private int _width;
    private int _height;
    
    // Throttle preview updates to save resources
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _throttle = TimeSpan.FromMilliseconds(100); // 10 FPS max

    public PreviewImageHelper(ID3D11Device device, ID3D11DeviceContext context)
    {
        _device = device;
        _context = context;
    }

    public WriteableBitmap? GetPreviewBitmap(ID3D11Texture2D sourceTexture)
    {
        if (DateTime.Now - _lastUpdate < _throttle)
            return _bitmap; // Return cached

        var desc = sourceTexture.Description;

        if (_stagingTexture == null || _width != (int)desc.Width || _height != (int)desc.Height)
        {
            _stagingTexture?.Dispose();
            _width = (int)desc.Width;
            _height = (int)desc.Height;

            var stagingDesc = new Texture2DDescription
            {
                Width = (uint)_width,
                Height = (uint)_height,
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
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
            });
        }

        // Copy resource to staging
        _context.CopyResource(_stagingTexture, sourceTexture);
        
        // Map staging texture
        var mapped = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_bitmap != null)
                {
                    _bitmap.Lock();
                    
                    // Copy scanlines
                    int stride = _width * 4;
                    for (int y = 0; y < _height; y++)
                    {
                        IntPtr sourcePtr = mapped.DataPointer + y * (int)mapped.RowPitch;
                        IntPtr destPtr = _bitmap.BackBuffer + y * _bitmap.BackBufferStride;
                        unsafe
                        {
                            Buffer.MemoryCopy(sourcePtr.ToPointer(), destPtr.ToPointer(), stride, stride);
                        }
                    }

                    _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    _bitmap.Unlock();
                }
            });
        }
        finally
        {
            _context.Unmap(_stagingTexture, 0);
        }

        _lastUpdate = DateTime.Now;
        return _bitmap;
    }

    public void Dispose()
    {
        _stagingTexture?.Dispose();
    }
}
