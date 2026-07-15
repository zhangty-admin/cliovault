using System.Text.Json.Serialization;

namespace ClipVault.Models;

/// <summary>
/// 历史记录保留周期
/// </summary>
public enum RetentionPeriod
{
    /// <summary>
    /// 保留 3 天
    /// </summary>
    ThreeDays,

    /// <summary>
    /// 保留 7 天
    /// </summary>
    SevenDays,

    /// <summary>
    /// 保留 1 个月（30 天）
    /// </summary>
    OneMonth,

    /// <summary>
    /// 保留 3 个月（90 天）
    /// </summary>
    ThreeMonths,

    /// <summary>
    /// 永不自动删除
    /// </summary>
    Never
}

/// <summary>
/// 应用设置（可 JSON 序列化）
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 历史记录保留周期，默认 7 天
    /// </summary>
    public RetentionPeriod RetentionPeriod { get; set; } = RetentionPeriod.SevenDays;

    /// <summary>
    /// 快捷键修饰符（MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8 的组合）
    /// </summary>
    public uint HotkeyModifiers { get; set; } = 0x0006; // Ctrl+Shift

    /// <summary>
    /// 快捷键虚拟键码（默认 'V' = 0x56）
    /// </summary>
    public uint HotkeyVirtualKey { get; set; } = 0x56;

    public bool EnableSensitiveContentFilter { get; set; } = true;

    /// <summary>
    /// 设置文件版本（用于未来迁移）
    /// </summary>
    public int Version { get; set; } = 2;
}
