using System.Windows.Threading;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>
/// 自动清理服务 — 按保留周期过期非置顶项，清理孤儿图片
/// 触发时机：启动时、定时（每小时）、设置变更时
/// </summary>
public class CleanupService
{
    private readonly ClipboardStore _store;
    private readonly SettingsService _settings;
    private readonly PersistenceService _persistence;
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// 清理完成后回调（用于日志/通知）
    /// </summary>
    public event Action<int, int>? CleanupCompleted;

    public CleanupService(ClipboardStore store, SettingsService settings, PersistenceService persistence)
    {
        _store = store;
        _settings = settings;
        _persistence = persistence;

        // 每小时检查一次
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(1)
        };
        _timer.Tick += (_, _) => RunCleanup();

        // 监听设置变更
        _settings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// 启动定时清理
    /// </summary>
    public void Start()
    {
        // 启动时立即执行一次
        RunCleanup();
        _timer.Start();
    }

    /// <summary>
    /// 停止定时清理
    /// </summary>
    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>
    /// 设置变更时，如果保留周期改短了，立即执行清理
    /// </summary>
    private void OnSettingsChanged(AppSettings settings)
    {
        // 设置变了就检查一次（可能从 Never 改成 3 天，需要立即清理）
        RunCleanup();
    }

    /// <summary>
    /// 执行一次清理
    /// </summary>
    /// <returns>清理的记录数和图片数</returns>
    public void RunCleanup()
    {
        var retention = _settings.Current.RetentionPeriod;
        var removedFromRecycleBin = _store.PurgeRecycleBin(DateTime.Now.AddDays(-7));

        // Never 只表示不清理活动历史；回收站和孤儿图片仍按各自规则清理。
        var removedItems = retention == RetentionPeriod.Never
            ? 0
            : _store.PurgeExpired(GetCutoffDate(retention));
        // 清理孤儿图片
        var validImages = _store.Items
            // 图片预览可能属于 Image、Files、Rtf 或 Html，不能只按 Type 判断。
            .Where(i => i.Image != null)
            .Select(i => $"{i.Id:N}.png")
            .Concat(_store.RecycleBin
                .Where(x => x.Item.Image != null)
                .Select(x => $"{x.Item.Id:N}.png"))
            .ToHashSet();

        var removedImages = _persistence.CleanupOrphanedImages(validImages);

        if (removedItems > 0 || removedImages > 0)
        {
            CleanupCompleted?.Invoke(removedItems, removedImages);
        }

        Helpers.MessageWindow.Log(
            $"[Cleanup] movedToRecycleBin={removedItems}, " +
            $"purgedFromRecycleBin={removedFromRecycleBin}, orphanedImages={removedImages}");
    }

    /// <summary>
    /// 将保留周期转为截止日期
    /// </summary>
    private static DateTime GetCutoffDate(RetentionPeriod period)
    {
        var now = DateTime.Now;
        return period switch
        {
            RetentionPeriod.ThreeDays    => now.AddDays(-3),
            RetentionPeriod.SevenDays    => now.AddDays(-7),
            RetentionPeriod.OneMonth     => now.AddDays(-30),
            RetentionPeriod.ThreeMonths  => now.AddDays(-90),
            _ => DateTime.MinValue  // Never 不会走到这里
        };
    }
}
