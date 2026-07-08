using System.Runtime.InteropServices;
using ClipVault.Helpers;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>
/// 全局快捷键服务 — 注册/注销热键，冲突检测
/// </summary>
public class HotkeyService
{
    private readonly MessageWindow _messageWindow;
    private HotkeyConfig? _currentConfig;

    /// <summary>
    /// 热键按下时触发
    /// </summary>
    public event Action? HotkeyPressed;

    /// <summary>
    /// 快捷键注册失败时触发（如被其他程序占用）
    /// </summary>
    public event Action<string>? RegistrationFailed;

    public HotkeyService(MessageWindow messageWindow)
    {
        _messageWindow = messageWindow;
        _messageWindow.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>
    /// 注册全局热键
    /// </summary>
    /// <returns>注册成功返回 true</returns>
    public bool Register(HotkeyConfig config)
    {
        Unregister();

        var hwnd = _messageWindow.WindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            RegistrationFailed?.Invoke("消息窗口未初始化");
            return false;
        }

        var modifiers = config.Modifiers | Win32Modifiers.MOD_NOREPEAT;
        var result = Helpers.Win32.User32.RegisterHotKey(hwnd, config.Id, modifiers, config.VirtualKey);

        if (!result)
        {
            var error = Marshal.GetLastWin32Error();
            var message = error == 1409
                ? $"快捷键 {config.DisplayString} 已被其他程序占用"
                : $"注册快捷键失败 (错误码: {error})";
            RegistrationFailed?.Invoke(message);
            return false;
        }

        _currentConfig = config;
        return true;
    }

    /// <summary>
    /// 注销当前热键
    /// </summary>
    public void Unregister()
    {
        if (_currentConfig == null) return;

        var hwnd = _messageWindow.WindowHandle;
        if (hwnd != IntPtr.Zero)
        {
            Helpers.Win32.User32.UnregisterHotKey(hwnd, _currentConfig.Id);
        }
        _currentConfig = null;
    }

    private void OnHotkeyPressed(int id)
    {
        if (_currentConfig != null && id == _currentConfig.Id)
        {
            HotkeyPressed?.Invoke();
        }
    }
}
