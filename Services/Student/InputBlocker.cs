using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;
using Microsoft.Win32;

namespace ClassRoom_Control.Services.Student;

public class InputBlocker : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_SPACE = 0x20;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static IntPtr _keyboardHook = IntPtr.Zero;
    private static IntPtr _mouseHook = IntPtr.Zero;
    private static HookProc? _keyboardProc;
    private static HookProc? _mouseProc;

    private static readonly object _lock = new();
    private static bool _isLocked;

    // Watchdog timer: automatically unlock if not refreshed within 20 seconds
    private static readonly System.Timers.Timer _watchdogTimer = new(20_000);

    static InputBlocker()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        _watchdogTimer.Elapsed += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine("[Watchdog] Timeout reached without heartbeat. Unlocking input.");
            SetLock(false);
        };
        _watchdogTimer.AutoReset = false;

        // Ensure cleanup when process exits or crashes
        AppDomain.CurrentDomain.ProcessExit += (s, e) => EmergencyUnlock();
        AppDomain.CurrentDomain.UnhandledException += (s, e) => EmergencyUnlock();
    }

    public static bool IsLocked => _isLocked;

    public static void RefreshWatchdog()
    {
        if (_isLocked)
        {
            _watchdogTimer.Stop();
            _watchdogTimer.Start();
        }
    }

    public static void SetLock(bool enable)
    {
        lock (_lock)
        {
            if (_isLocked == enable) return;
            _isLocked = enable;

            if (enable)
            {
                InstallHooks();
                SetTaskbarVisible(false);
                SetTaskMgrDisabled(true);
                _watchdogTimer.Start();
            }
            else
            {
                _watchdogTimer.Stop();
                UninstallHooks();
                SetTaskbarVisible(true);
                SetTaskMgrDisabled(false);
            }
        }
    }

    private static void InstallHooks()
    {
        if (_keyboardHook == IntPtr.Zero)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var modHandle = GetModuleHandle(curModule?.ModuleName);

            if (_keyboardProc != null)
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, modHandle, 0);

            if (_mouseProc != null)
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, modHandle, 0);
        }
    }

    private static void UninstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isLocked)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;
            bool altDown = (kbd.flags & 0x20) != 0;

            // Block Windows key
            if (vk == VK_LWIN || vk == VK_RWIN)
                return (IntPtr)1;

            // Block Alt+Tab, Alt+Esc, Alt+F4, Alt+Space
            if (altDown && (vk == VK_TAB || vk == VK_ESCAPE || vk == VK_F4 || vk == VK_SPACE))
                return (IntPtr)1;

            // Block Ctrl+Esc (opens Start menu)
            if (vk == VK_ESCAPE && (GetKeyState(0x11) & 0x8000) != 0) // VK_CONTROL
                return (IntPtr)1;

            // Block everything else while full lock is enabled
            return (IntPtr)1;
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isLocked)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
                msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
                msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP ||
                msg == WM_MOUSEWHEEL)
            {
                // Suppress mouse click events
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private static void SetTaskbarVisible(bool visible)
    {
        int cmd = visible ? SW_SHOW : SW_HIDE;

        // Primary taskbar
        var hTaskbar = FindWindow("Shell_TrayWnd", null);
        if (hTaskbar != IntPtr.Zero)
        {
            ShowWindow(hTaskbar, cmd);
        }

        // Secondary monitors taskbars
        var hSecondary = FindWindow("Shell_SecondaryTrayWnd", null);
        if (hSecondary != IntPtr.Zero)
        {
            ShowWindow(hSecondary, cmd);
        }
    }

    private static void SetTaskMgrDisabled(bool disabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            if (key != null)
            {
                if (disabled)
                {
                    key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue("DisableTaskMgr", false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update DisableTaskMgr in registry: {ex.Message}");
        }
    }

    public static void EmergencyUnlock()
    {
        try
        {
            SetLock(false);
        }
        catch { }
    }

    public void Dispose()
    {
        SetLock(false);
    }
}
