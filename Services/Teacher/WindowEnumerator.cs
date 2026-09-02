using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClassRoom_Control.Services.Teacher;

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
    public bool IsScreen { get; set; }
}

public static class WindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);
    
    [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "GetClassLong")]
    private static extern IntPtr GetClassLong32(IntPtr hWnd, int nIndex);

    private const int GW_OWNER = 4;
    private const uint WM_GETICON = 0x007F;
    private const int ICON_SMALL2 = 2;
    private const int GCL_HICON = -14;

    public static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        // Always add the entire screen as the first option
        windows.Add(new WindowInfo
        {
            Handle = IntPtr.Zero, // Zero handle implies full screen
            Title = "Весь экран (Основной монитор)",
            IsScreen = true
        });

        EnumWindows((hWnd, lParam) =>
        {
            if (IsWindowVisible(hWnd) && GetWindow(hWnd, GW_OWNER) == IntPtr.Zero)
            {
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    var builder = new StringBuilder(length + 1);
                    GetWindowText(hWnd, builder, builder.Capacity);
                    var title = builder.ToString();

                    // Filter out some system windows
                    if (title != "Program Manager" && title != "Settings" && !title.Contains("ClassRoom Control"))
                    {
                        var info = new WindowInfo
                        {
                            Handle = hWnd,
                            Title = title,
                            Icon = GetWindowIcon(hWnd),
                            IsScreen = false
                        };
                        windows.Add(info);
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static ImageSource? GetWindowIcon(IntPtr hWnd)
    {
        try
        {
            IntPtr hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);
            
            if (hIcon == IntPtr.Zero)
            {
                hIcon = IntPtr.Size == 8 ? GetClassLongPtr(hWnd, GCL_HICON) : GetClassLong32(hWnd, GCL_HICON);
            }

            if (hIcon != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(hIcon);
                return Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
        }
        catch { }
        return null;
    }
}
