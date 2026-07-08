using Microsoft.Win32;

namespace ClipVault.Services;

/// <summary>
/// 开机自启动服务 — 通过注册表 HKCU\...\Run 实现（无需管理员权限）
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ClipVault";

    /// <summary>
    /// 检测是否已开启自启动
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(AppName) as string;
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// 开启开机自启动
    /// </summary>
    public static void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var command = $"\"{exePath}\" --minimized";

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.SetValue(AppName, command, RegistryValueKind.String);
        }
        catch { }
    }

    /// <summary>
    /// 关闭开机自启动
    /// </summary>
    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.DeleteValue(AppName, false);
        }
        catch { }
    }
}
