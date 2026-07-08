using System.Windows;
using ClipVault.Helpers;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>
/// 剪贴板监听服务 — 注册/注销监听、读取内容并抛出事件。
/// 包含回环抑制逻辑（防止程序主动写入剪贴板时触发回环）。
/// </summary>
public class ClipboardMonitor : IDisposable
{
    private readonly MessageWindow _messageWindow;
    private bool _isSettingClipboard;

    /// <summary>
    /// 剪贴板内容变化时触发
    /// </summary>
    public event Action<ClipboardItem>? ClipboardChanged;

    /// <summary>
    /// 回环抑制：设为 true 后，下一次 WM_CLIPBOARDUPDATE 将被忽略
    /// </summary>
    public bool SuppressNext { get; set; }

    public ClipboardMonitor(MessageWindow messageWindow)
    {
        _messageWindow = messageWindow;
        _messageWindow.ClipboardChanged += OnClipboardChanged;
    }

    /// <summary>
    /// 启动监听
    /// </summary>
    public void Start()
    {
        // MessageWindow 已由调用方创建，无需额外操作
    }

    /// <summary>
    /// 程序主动设置剪贴板内容时调用，避免回环
    /// </summary>
    public void SetClipboardSafely(Action setAction)
    {
        try
        {
            _isSettingClipboard = true;
            SuppressNext = true;
            setAction();
        }
        finally
        {
            // 延迟重置标志，确保下一次 WM_CLIPBOARDUPDATE 已被处理
            // 使用 Dispatcher 低优先级延迟
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _isSettingClipboard = false;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnClipboardChanged()
    {
        MessageWindow.Log("[Monitor] OnClipboardChanged triggered");

        if (_isSettingClipboard || SuppressNext)
        {
            MessageWindow.Log($"[Monitor] Suppressed: _isSettingClipboard={_isSettingClipboard}, SuppressNext={SuppressNext}");
            SuppressNext = false;
            return;
        }

        var item = ClipboardFormatDetector.ReadFromClipboard();
        if (item != null)
        {
            MessageWindow.Log($"[Monitor] Read OK: Type={item.Type}, HasImage={item.Image != null}, Hash={item.ContentHash}");
            ClipboardChanged?.Invoke(item);
        }
        else
        {
            MessageWindow.Log("[Monitor] ReadFromClipboard returned NULL");
        }
    }

    public void Dispose()
    {
        _messageWindow.ClipboardChanged -= OnClipboardChanged;
    }
}
