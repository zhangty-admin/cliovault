using System.Runtime.InteropServices;
using System.Text;
using ClipVault.Helpers.Win32;

namespace ClipVault.Helpers;

/// <summary>
/// 使用 Win32 API 直接写剪贴板，绕过 WPF/OLE 层的 1 秒超时。
/// OpenClipboard 失败时立即返回 false（而非阻塞 1 秒）。
/// </summary>
public static class FastClipboard
{
    /// <summary>
    /// 快速设置 Unicode 文本到剪贴板。
    /// 成功返回 true，剪贴板被占用返回 false（立即，无阻塞）。
    /// </summary>
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // 尝试打开剪贴板（立即返回，无内部重试）
        if (!User32.OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            // 清空剪贴板
            if (!User32.EmptyClipboard())
                return false;

            // 分配全局内存（Unicode 文本 + null 终止符）
            // 字节数 = (字符数 + 1) * 2
            int byteCount = (text.Length + 1) * 2;
            IntPtr hGlobal = User32.GlobalAlloc(
                Win32Constants.GMEM_MOVEABLE,
                (UIntPtr)byteCount);

            if (hGlobal == IntPtr.Zero)
                return false;

            // 锁定内存，写入文本
            IntPtr pGlobal = User32.GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero)
            {
                User32.GlobalFree(hGlobal);
                return false;
            }

            Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
            // 最后 2 字节已经是零（GlobalAlloc 返回零初始化内存）
            User32.GlobalUnlock(hGlobal);

            // 设置剪贴板数据（所有权转移给系统，不要 GlobalFree）
            IntPtr result = User32.SetClipboardData(
                Win32Constants.CF_UNICODETEXT, hGlobal);

            return result != IntPtr.Zero;
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    /// <summary>
    /// 带快速重试的文本写入（每次重试间隔仅 10ms，最多 3 次）
    /// </summary>
    public static bool TrySetTextWithRetry(string text, int maxAttempts = 3)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            if (TrySetText(text))
                return true;

            if (i < maxAttempts - 1)
                System.Threading.Thread.Sleep(10);
        }
        return false;
    }
}
