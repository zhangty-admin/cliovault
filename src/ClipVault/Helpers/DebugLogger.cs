using System.IO;

namespace ClipVault.Helpers;

/// <summary>
/// 调试日志器 — 写入 debug.log，带高精度时间戳
/// </summary>
public static class DebugLogger
{
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "debug.log");

    private static readonly object _lock = new();

    /// <summary>
    /// 写入一行日志
    /// </summary>
    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
        }
        catch { }
    }
}
