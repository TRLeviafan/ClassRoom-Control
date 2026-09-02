using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Vortice.Direct3D11;
using Vortice.DXGI;
using System.Threading;

namespace ClassRoom_Control.Services.Teacher
{
    // ─── COM Interop for Windows.Graphics.Capture ───
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    public interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            [In] ref Guid iid,
            out IntPtr result);

        IntPtr CreateForMonitor(
            [In] IntPtr monitor,
            [In] ref Guid iid,
            out IntPtr result);
    }

    public static class CaptureHelper
    {
        private static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId,
            ref Guid iid,
            out IGraphicsCaptureItemInterop factory);

        private static IGraphicsCaptureItemInterop GetInteropFactory()
        {
            Guid iid = typeof(IGraphicsCaptureItemInterop).GUID;
            string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            WindowsCreateString(className, className.Length, out IntPtr hstring);
            
            try
            {
                int hr = RoGetActivationFactory(hstring, ref iid, out var factory);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return factory;
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }

        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hWnd)
        {
            var factory = GetInteropFactory();
            Guid guid = GraphicsCaptureItemGuid;
            factory.CreateForWindow(hWnd, ref guid, out IntPtr ptr);
            return GraphicsCaptureItem.FromAbi(ptr);
        }
        
        public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hMonitor)
        {
            var factory = GetInteropFactory();
            Guid guid = GraphicsCaptureItemGuid;
            factory.CreateForMonitor(hMonitor, ref guid, out IntPtr ptr);
            return GraphicsCaptureItem.FromAbi(ptr);
        }
    }

    public static class Direct3DInterop
    {
        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        public static extern void CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        public static global::Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice CreateDirect3DDevice(ID3D11Device d3dDevice)
        {
            var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pUnknown);
            dxgiDevice.Dispose();

            // In .NET 8 (CsWinRT), we cannot cast __ComObject directly to projected interfaces.
            // We must use MarshalInterface.FromAbi to create the CsWinRT wrapper.
            var winrtDevice = WinRT.MarshalInterface<global::Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice>.FromAbi(pUnknown);
            
            // FromAbi AddsRef, so we need to release the original pointer we got from CreateDirect3D11DeviceFromDXGIDevice
            Marshal.Release(pUnknown);
            
            return winrtDevice;
        }
    }

    public class ScreenCapturer : IDisposable
    {
        private ID3D11Device _d3dDevice;
        private ID3D11DeviceContext _d3dContext;
        private global::Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice _winrtDevice;
        private GraphicsCaptureItem _captureItem;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;

        public event Action<ID3D11Texture2D>? FrameCaptured;

        public ID3D11Device D3DDevice => _d3dDevice;
        public ID3D11DeviceContext D3DContext => _d3dContext;
        public int Width => _captureItem.Size.Width;
        public int Height => _captureItem.Size.Height;

        public ScreenCapturer(WindowInfo windowInfo)
        {
            // 1. Initialize D3D11 Device
            D3D11.D3D11CreateDevice(
                null,
                Vortice.Direct3D.DriverType.Hardware,
                Vortice.Direct3D11.DeviceCreationFlags.BgraSupport,
                null,
                out _d3dDevice,
                out _d3dContext).CheckError();

            _winrtDevice = Direct3DInterop.CreateDirect3DDevice(_d3dDevice);

            // 2. Create Capture Item
            if (windowInfo.IsScreen)
            {
                // Just use the primary monitor for now
                const int MONITOR_DEFAULTTOPRIMARY = 1;
                [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
                var hMonitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
                _captureItem = CaptureHelper.CreateItemForMonitor(hMonitor);
            }
            else
            {
                _captureItem = CaptureHelper.CreateItemForWindow(windowInfo.Handle);
            }

            // 3. Create Frame Pool
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice,
                global::Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                _captureItem.Size);

            _framePool.FrameArrived += OnFrameArrived;

            // 4. Create and start session
            _session = _framePool.CreateCaptureSession(_captureItem);
            _session.StartCapture();
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            // COM Interop to get the native ID3D11Texture2D
            // In .NET 8 (CsWinRT), frame.Surface is a managed wrapper. We need the native COM object.
            
            IDirect3DDxgiInterfaceAccess? surfaceInterop = null;
            if (frame.Surface is WinRT.IWinRTObject winrtObj)
            {
                // Extract the native COM pointer from the CsWinRT wrapper
                IntPtr nativePtr = winrtObj.NativeObject.ThisPtr;
                // Get a raw System.__ComObject that we can cast to our custom ComImport interface
                object rawComObject = Marshal.GetObjectForIUnknown(nativePtr);
                surfaceInterop = (IDirect3DDxgiInterfaceAccess)rawComObject;
            }
            else
            {
                // Fallback (should not be reached in .NET 8 CsWinRT)
                surfaceInterop = (IDirect3DDxgiInterfaceAccess)frame.Surface;
            }

            Guid resourceGuid = typeof(ID3D11Texture2D).GUID;
            var texturePtr = surfaceInterop.GetInterface(ref resourceGuid);

            using var texture = new ID3D11Texture2D(texturePtr);
            
            FrameCaptured?.Invoke(texture);
        }

        public void Dispose()
        {
            _session?.Dispose();
            _framePool?.Dispose();
            _winrtDevice?.Dispose();
            _d3dContext?.Dispose();
            _d3dDevice?.Dispose();
        }
    }
}

namespace Windows.Win32.System.WinRT
{
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    public interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }
}

