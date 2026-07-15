using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ClipVault.Models;

/// <summary>
/// 单条剪贴板记录
/// </summary>
public class ClipboardItem : INotifyPropertyChanged
{
    private bool _isPinned;
    private List<string> _tags = new();
    private string? _text;
    private string? _smartType;

    public Guid Id { get; init; } = Guid.NewGuid();

    public ClipboardItemType Type { get; init; }

    public string? Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewText));
                OnPropertyChanged(nameof(ContentHash));
            }
        }
    }

    public BitmapSource? Image { get; set; }

    public IReadOnlyList<string>? FilePaths { get; init; }

    public DateTime CapturedAt { get; init; } = DateTime.Now;

    public string? SmartType
    {
        get => _smartType;
        set
        {
            if (_smartType != value)
            {
                _smartType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeIcon));
                OnPropertyChanged(nameof(SmartTypeText));
            }
        }
    }

    /// <summary>
    /// 是否置顶
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 自定义分组标签（null 或空表示未分组）。保留给旧代码和旧数据兼容，返回第一个标签。
    /// </summary>
    public string? Tag
    {
        get => _tags.FirstOrDefault();
        set
        {
            Tags = string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : new List<string> { value.Trim() };
        }
    }

    public List<string> Tags
    {
        get => _tags;
        set
        {
            var normalized = NormalizeTags(value);
            if (_tags.SequenceEqual(normalized))
                return;

            _tags = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Tag));
            OnPropertyChanged(nameof(TagsText));
            OnPropertyChanged(nameof(HasTags));
        }
    }

    public bool HasTags => _tags.Count > 0;

    public string TagsText => string.Join(" · ", _tags);

    public string SmartTypeText => string.IsNullOrWhiteSpace(SmartType) ? "普通" : SmartType;

    /// <summary>
    /// 预览文本（用于搜索和显示）
    /// </summary>
    public string PreviewText
    {
        get
        {
            return Type switch
            {
                ClipboardItemType.Text => Text ?? string.Empty,
                ClipboardItemType.Rtf => Text ?? string.Empty,
                ClipboardItemType.Html => Text ?? string.Empty,
                ClipboardItemType.Image => "[图片]",
                ClipboardItemType.Files => FilePaths is { Count: > 0 }
                    ? string.Join("\n", FilePaths.Select(p => System.IO.Path.GetFileName(p)))
                    : "[文件]",
                _ => "[未知]"
            };
        }
    }

    /// <summary>
    /// 时间显示文本（包含日期）
    /// </summary>
    public string TimeText
    {
        get
        {
            var now = DateTime.Now;
            if (CapturedAt.Date == now.Date)
                return CapturedAt.ToString("今天 HH:mm");
            if (CapturedAt.Date == now.AddDays(-1).Date)
                return CapturedAt.ToString("昨天 HH:mm");
            if (CapturedAt.Year == now.Year)
                return CapturedAt.ToString("MM-dd HH:mm");
            return CapturedAt.ToString("yyyy-MM-dd HH:mm");
        }
    }

    /// <summary>
    /// 类型图标文字
    /// </summary>
    public string TypeIcon
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SmartType))
            {
                return SmartType switch
                {
                    "URL" => "🔗",
                    "Email" => "✉",
                    "手机号" => "☎",
                    "JSON" => "{}",
                    "代码" => "</>",
                    "命令" => ">_",
                    "SQL" => "DB",
                    "颜色" => "#",
                    _ => "📝"
                };
            }

            return Type switch
            {
                ClipboardItemType.Text => "📝",
                ClipboardItemType.Image => "🖼",
                ClipboardItemType.Files => "📁",
                ClipboardItemType.Rtf => "📄",
                ClipboardItemType.Html => "🌐",
                _ => "❓"
            };
        }
    }

    /// <summary>
    /// 计算内容的哈希值用于去重
    /// </summary>
    public string ContentHash
    {
        get
        {
            return Type switch
            {
                ClipboardItemType.Text => $"T:{Text}",
                ClipboardItemType.Rtf  => $"R:{Text}",
                ClipboardItemType.Html => $"H:{Text}",
                ClipboardItemType.Image => $"I:{Image?.PixelWidth}x{Image?.PixelHeight}",
                ClipboardItemType.Files => FilePaths is { Count: > 0 }
                    ? "F:" + string.Join("|", FilePaths)
                    : "F:empty",
                _ => Id.ToString()
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags == null) return new List<string>();
        return tags
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
