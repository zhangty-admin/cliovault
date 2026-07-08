namespace ClipVault.Models;

/// <summary>
/// 可 JSON 序列化的剪贴板记录 DTO
/// </summary>
public class ClipboardItemDto
{
    public Guid Id { get; set; }

    public ClipboardItemType Type { get; set; }

    public string? Text { get; set; }

    /// <summary>
    /// 图片文件相对路径（相对于 images 目录）
    /// </summary>
    public string? ImagePath { get; set; }

    public List<string>? FilePaths { get; set; }

    public DateTime CapturedAt { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>
    /// 自定义分组标签
    /// </summary>
    public string? Tag { get; set; }
}

/// <summary>
/// 历史记录文件根结构
/// </summary>
public class ClipboardHistoryData
{
    public int Version { get; set; } = 2;

    public List<ClipboardItemDto> Items { get; set; } = new();

    /// <summary>
    /// 独立分组标签（不依赖于具体项目的分组）
    /// </summary>
    public List<string> Tags { get; set; } = new();

    public List<RecycleBinEntryDto> RecycleBin { get; set; } = new();
}

public class RecycleBinEntryDto
{
    public ClipboardItemDto Item { get; set; } = new();

    public DateTime DeletedAt { get; set; }
}
