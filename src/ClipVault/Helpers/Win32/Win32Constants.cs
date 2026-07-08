namespace ClipVault.Helpers.Win32;

/// <summary>
/// Win32 消息和修饰键常量
/// </summary>
internal static class Win32Constants
{
    // Windows 消息
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_DESTROY = 0x0002;

    // 虚拟键码
    public const byte VK_CONTROL = 0x11;
    public const byte VK_V = 0x56;

    // keybd_event 标志
    public const uint KEYEVENTF_KEYUP = 0x0002;

    // 剪贴板格式
    public const uint CF_UNICODETEXT = 13;

    // GlobalAlloc 标志
    public const uint GMEM_MOVEABLE = 0x0002;

    // MonitorFromWindow 标志
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
}
