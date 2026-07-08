namespace ClipVault.Models;

/// <summary>
/// 回收站中的历史记录。
/// </summary>
public sealed class RecycleBinEntry
{
    public required ClipboardItem Item { get; init; }

    public DateTime DeletedAt { get; init; } = DateTime.Now;

    public string DeletedTimeText => DeletedAt.ToString("yyyy-MM-dd HH:mm");
}
