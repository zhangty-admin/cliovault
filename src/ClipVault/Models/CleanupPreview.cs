namespace ClipVault.Models;

public sealed class CleanupPreview
{
    public RetentionPeriod Period { get; init; }
    public DateTime? Cutoff { get; init; }
    public int ExpiredUntaggedCount { get; init; }
    public int ProtectedByPinCount { get; init; }
    public int ProtectedByTagCount { get; init; }
    public int RecycleBinExpiredCount { get; init; }
    public DateTime? OldestAffectedAt { get; init; }

    public bool HasActiveCleanup => ExpiredUntaggedCount > 0;

    public string SummaryText
    {
        get
        {
            var cutoffText = Cutoff.HasValue ? Cutoff.Value.ToString("yyyy-MM-dd HH:mm") : "无";
            return Period == RetentionPeriod.Never
                ? $"活动历史不会自动清理；回收站中 {RecycleBinExpiredCount} 条已超过 7 天"
                : $"截止 {cutoffText}，将移入回收站 {ExpiredUntaggedCount} 条；置顶保护 {ProtectedByPinCount} 条，分组保护 {ProtectedByTagCount} 条";
        }
    }

    public string DetailText
    {
        get
        {
            var oldestText = OldestAffectedAt.HasValue ? OldestAffectedAt.Value.ToString("yyyy-MM-dd HH:mm") : "无";
            return $"最早受影响记录：{oldestText}；回收站待永久清理：{RecycleBinExpiredCount} 条";
        }
    }
}
