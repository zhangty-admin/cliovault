using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ClipVault.Helpers.Win32;

namespace ClipVault.Helpers;

/// <summary>
/// 隐藏消息窗口 — 常驻后台，用于接收 WM_CLIPBOARDUPDATE 和 WM_HOTKEY 等系统消息。
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private HwndSource? _source;
    private IntPtr _hwnd = IntPtr.Zero;
    private static readonly string LogPath = System.IO.Path.Combine(
        AppContext.BaseDirectory, "debug.log");

    internal static void Log(string message)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(LogPath);
            if (dir != null && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public event Action? ClipboardChanged;
    public event Action<int>? HotkeyPressed;

    public void Create()
    {
        if (_source != null) return;

        Log("MessageWindow.Create() started");

        var parameters = new HwndSourceParameters("ClipVaultMessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0
        };
        parameters.SetPosition(0, 0);

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _hwnd = _source.Handle;

        Log($"HWND created: {_hwnd}");

        if (!User32.AddClipboardFormatListener(_hwnd))
        {
            var error = Marshal.GetLastWin32Error();
            Log($"AddClipboardFormatListener FAILED: error {error}");
            throw new InvalidOperationException(
                $"注册剪贴板监听失败 (Win32 Error: {error})");
        }

        Log("AddClipboardFormatListener OK");
    }

    public IntPtr WindowHandle => _hwnd;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Constants.WM_CLIPBOARDUPDATE)
        {
            Log("WM_CLIPBOARDUPDATE received");
            ClipboardChanged?.Invoke();
            handled = true;
        }
        else if (msg == Win32Constants.WM_HOTKEY)
        {
            Log($"WM_HOTKEY received, id={wParam.ToInt32()}");
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        else if (msg == Win32Constants.WM_DESTROY)
        {
            User32.RemoveClipboardFormatListener(hwnd);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            User32.RemoveClipboardFormatListener(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _source?.Dispose();
        _source = null;
    }
}
