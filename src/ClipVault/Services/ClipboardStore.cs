using System.Collections.ObjectModel;
using System.Windows.Threading;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>
/// 剪贴板内存存储 — 去重、Pin 排序、容量上限 FIFO 淘汰、自动持久化
/// </summary>
public class ClipboardStore
{
    private readonly ObservableCollection<ClipboardItem> _items = new();
    private readonly ObservableCollection<RecycleBinEntry> _recycleBin = new();
    private readonly List<string> _standaloneTags = new();
    private readonly object _lock = new();
    private string? _lastContentHash;
    private readonly int _maxCapacity;
    private readonly PersistenceService? _persistence;
    private DispatcherTimer? _saveTimer;

    /// <summary>
    /// 只读视图（供 UI 绑定）
    /// </summary>
    public ReadOnlyObservableCollection<ClipboardItem> Items { get; }
    public ReadOnlyObservableCollection<RecycleBinEntry> RecycleBin { get; }

    public ClipboardStore(int maxCapacity = 500, PersistenceService? persistence = null)
    {
        _maxCapacity = maxCapacity;
        _persistence = persistence;
        Items = new ReadOnlyObservableCollection<ClipboardItem>(_items);
        RecycleBin = new ReadOnlyObservableCollection<RecycleBinEntry>(_recycleBin);

        // 启动时加载持久化数据
        if (_persistence != null)
        {
            LoadFromDisk();
        }
    }

    /// <summary>
    /// 从磁盘加载历史记录到内存
    /// </summary>
    private void LoadFromDisk()
    {
        try
        {
            var (loaded, tags, recycleBin) = _persistence!.LoadAll();
            // 加载独立分组
            foreach (var t in tags)
                _standaloneTags.Add(t);

            // 加载时先加置顶项，再加非置顶项，保持排序
            foreach (var item in loaded.Where(x => x.IsPinned))
                _items.Add(item);
            foreach (var item in loaded.Where(x => !x.IsPinned))
                _items.Add(item);
            foreach (var entry in recycleBin.OrderByDescending(x => x.DeletedAt))
                _recycleBin.Add(entry);

            // 恢复 lastContentHash 以避免第一条重复
            if (_items.Count > 0)
                _lastContentHash = _items[_items.Count - 1].ContentHash;
        }
        catch { }
    }

    /// <summary>
    /// 添加剪贴板记录（自动去重）
    /// </summary>
    public bool Add(ClipboardItem item)
    {
        lock (_lock)
        {
            Helpers.MessageWindow.Log($"[Store] Add: Type={item.Type}, HasImage={item.Image != null}, Hash={item.ContentHash}");
            if (_lastContentHash != null && item.ContentHash == _lastContentHash)
            {
                Helpers.MessageWindow.Log("[Store] DEDUP skipped");
                return false;
            }
            _lastContentHash = item.ContentHash;

            // 插入到所有置顶项之后
            int insertIndex = FindInsertIndexAfterPinned();

            _items.Insert(insertIndex, item);
            TrimToCapacity();
            ScheduleSave();
            return true;
        }
    }

    /// <summary>
    /// 查找所有置顶项之后的插入位置
    /// </summary>
    private int FindInsertIndexAfterPinned()
    {
        int index = 0;
        foreach (var existing in _items)
        {
            if (existing.IsPinned)
                index++;
            else
                break;
        }
        return index;
    }

    /// <summary>
    /// 将指定记录移到最前（置顶项之后）
    /// </summary>
    public void MoveToFront(Guid itemId)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item == null) return;
            if (_items.IndexOf(item) == 0) return;

            _items.Remove(item);

            if (item.IsPinned)
                _items.Insert(0, item);
            else
                _items.Insert(FindInsertIndexAfterPinned(), item);

            ScheduleSave();
        }
    }

    /// <summary>
    /// 置顶/取消置顶某条记录
    /// </summary>
    public void TogglePin(Guid itemId)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item == null) return;

            item.IsPinned = !item.IsPinned;

            _items.Remove(item);

            if (item.IsPinned)
                _items.Insert(0, item);
            else
                _items.Insert(FindInsertIndexAfterPinned(), item);

            ScheduleSave();
        }
    }

    /// <summary>
    /// 删除单条记录并移入回收站
    /// </summary>
    public void Remove(Guid itemId)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item != null)
            {
                _items.Remove(item);
                _recycleBin.Insert(0, new RecycleBinEntry { Item = item });
                ScheduleSave();
            }
        }
    }

    /// <summary>
    /// 清空所有记录
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var item in _items)
                _recycleBin.Insert(0, new RecycleBinEntry { Item = item });
            _items.Clear();
            _lastContentHash = null;
            ScheduleSave();
        }
    }

    /// <summary>
    /// 延迟保存（防抖：2秒内多次变更只写一次磁盘）
    /// </summary>
    private void ScheduleSave()
    {
        if (_persistence == null) return;

        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer!.Stop();
                _persistence!.Save(_items, _standaloneTags, _recycleBin);
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// 立即保存到磁盘（应用退出时调用）
    /// </summary>
    public void Flush()
    {
        if (_persistence == null) return;
        _saveTimer?.Stop();
        _persistence.Save(_items, _standaloneTags, _recycleBin);
    }

    /// <summary>
    /// 清除早于指定日期的非置顶、无分组标签的记录
    /// 置顶项和有分组标签的记录永不过期
    /// </summary>
    /// <param name="cutoff">截止日期，早于此日期的记录将被移除</param>
    /// <returns>实际移除的记录数</returns>
    public int PurgeExpired(DateTime cutoff)
    {
        lock (_lock)
        {
            var protectedByTag = _items.Count(i => i.HasTags);
            var protectedByPin = _items.Count(i => i.IsPinned);
            var toRemove = _items
                .Where(i => !i.IsPinned
                         && !i.HasTags
                         && i.CapturedAt < cutoff)
                .ToList();

            Helpers.MessageWindow.Log(
                $"[Cleanup] cutoff={cutoff:O}, total={_items.Count}, " +
                $"protectedByTag={protectedByTag}, protectedByPin={protectedByPin}, " +
                $"expiredUntagged={toRemove.Count}");

            foreach (var item in toRemove)
            {
                _items.Remove(item);
                _recycleBin.Insert(0, new RecycleBinEntry { Item = item });
            }

            if (toRemove.Count > 0)
                ScheduleSave();

            return toRemove.Count;
        }
    }

    public CleanupPreview PreviewCleanup(RetentionPeriod period, DateTime now)
    {
        lock (_lock)
        {
            var recycleBinCutoff = now.AddDays(-7);
            var recycleBinExpiredCount = _recycleBin.Count(x => x.DeletedAt < recycleBinCutoff);

            if (period == RetentionPeriod.Never)
            {
                return new CleanupPreview
                {
                    Period = period,
                    Cutoff = null,
                    ProtectedByPinCount = _items.Count(i => i.IsPinned),
                    ProtectedByTagCount = _items.Count(i => i.HasTags),
                    RecycleBinExpiredCount = recycleBinExpiredCount
                };
            }

            var cutoff = GetCutoffDate(period, now);
            var expired = _items
                .Where(i => !i.IsPinned
                         && !i.HasTags
                         && i.CapturedAt < cutoff)
                .ToList();

            return new CleanupPreview
            {
                Period = period,
                Cutoff = cutoff,
                ExpiredUntaggedCount = expired.Count,
                ProtectedByPinCount = _items.Count(i => i.IsPinned),
                ProtectedByTagCount = _items.Count(i => i.HasTags),
                RecycleBinExpiredCount = recycleBinExpiredCount,
                OldestAffectedAt = expired.Count == 0 ? null : expired.Min(i => i.CapturedAt)
            };
        }
    }

    public bool Restore(Guid itemId)
    {
        lock (_lock)
        {
            var entry = _recycleBin.FirstOrDefault(x => x.Item.Id == itemId);
            if (entry == null) return false;

            _recycleBin.Remove(entry);
            var item = entry.Item;
            var insertIndex = item.IsPinned ? 0 : FindInsertIndexAfterPinned();
            _items.Insert(insertIndex, item);
            ScheduleSave();
            return true;
        }
    }

    public bool DeletePermanently(Guid itemId)
    {
        lock (_lock)
        {
            var entry = _recycleBin.FirstOrDefault(x => x.Item.Id == itemId);
            if (entry == null) return false;
            _recycleBin.Remove(entry);
            ScheduleSave();
            return true;
        }
    }

    public int EmptyRecycleBin()
    {
        lock (_lock)
        {
            var count = _recycleBin.Count;
            if (count == 0) return 0;
            _recycleBin.Clear();
            ScheduleSave();
            return count;
        }
    }

    public int PurgeRecycleBin(DateTime cutoff)
    {
        lock (_lock)
        {
            var expired = _recycleBin.Where(x => x.DeletedAt < cutoff).ToList();
            foreach (var entry in expired)
                _recycleBin.Remove(entry);
            if (expired.Count > 0)
                ScheduleSave();
            return expired.Count;
        }
    }

    /// <summary>
    /// 获取所有分组标签（按存储顺序，不自动排序）
    /// </summary>
    public List<string> GetAllTags()
    {
        lock (_lock)
        {
            // 先加已有顺序的标签，再追加项目标签中不在列表的新标签
            var result = new List<string>(_standaloneTags);
            var itemTags = _items
                .SelectMany(x => x.Tags)
                .Distinct();

            foreach (var t in itemTags)
            {
                if (!result.Contains(t))
                    result.Add(t);
            }

            return result;
        }
    }

    /// <summary>
    /// 创建独立分组标签（不关联具体项目）
    /// </summary>
    public bool CreateTag(string tag)
    {
        var trimmed = tag?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        lock (_lock)
        {
            if (_standaloneTags.Contains(trimmed)) return false;
            if (_items.Any(x => x.Tags.Contains(trimmed))) return false;

            _standaloneTags.Add(trimmed);
            ScheduleSave();
            return true;
        }
    }

    /// <summary>
    /// 重新排序所有标签（拖拽后调用）
    /// 只更新顺序，不删除原有标签
    /// </summary>
    public void ReorderTags(List<string> newOrder)
    {
        lock (_lock)
        {
            // 按新顺序重建 _standaloneTags，保留原有成员
            var existing = new HashSet<string>(_standaloneTags);
            _standaloneTags.Clear();
            foreach (var t in newOrder)
            {
                if (existing.Contains(t))
                    _standaloneTags.Add(t);
            }
            // 补回不在 newOrder 中的原有标签（安全保护）
            foreach (var t in existing)
            {
                if (!_standaloneTags.Contains(t))
                    _standaloneTags.Add(t);
            }
            ScheduleSave();
        }
    }

    /// <summary>
    /// 为指定记录设置分组标签
    /// </summary>
    public void SetTag(Guid itemId, string? tag)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item == null) return;

            item.Tags = ParseTags(tag);
            foreach (var itemTag in item.Tags)
            {
                if (!_standaloneTags.Contains(itemTag) && !_items.Any(x => !ReferenceEquals(x, item) && x.Tags.Contains(itemTag)))
                    _standaloneTags.Add(itemTag);
            }
            ScheduleSave();
        }
    }

    /// <summary>
    /// 返回指定分组的当前显示顺序。
    /// </summary>
    public List<ClipboardItem> OrderItemsForTag(string tag, IEnumerable<ClipboardItem> items)
    {
        lock (_lock)
        {
            return items.ToList();
        }
    }

    /// <summary>
    /// 调整一个分组内记录的顺序，同时保留非该分组记录原有位置。
    /// </summary>
    public void ReorderGroupItems(string tag, IReadOnlyList<Guid> orderedItemIds)
    {
        lock (_lock)
        {
            var groupIndexes = _items
                .Select((item, index) => (item, index))
                .Where(x => x.item.Tags.Contains(tag))
                .Select(x => x.index)
                .ToList();
            var groupItems = _items.Where(x => x.Tags.Contains(tag)).ToDictionary(x => x.Id);
            var reordered = orderedItemIds
                .Where(groupItems.ContainsKey)
                .Distinct()
                .Select(id => groupItems[id])
                .ToList();
            if (reordered.Count != groupIndexes.Count)
                return;

            for (var i = 0; i < groupIndexes.Count; i++)
                _items[groupIndexes[i]] = reordered[i];
            ScheduleSave();
        }
    }

    public void ToggleTag(Guid itemId, string tag)
    {
        var trimmed = tag.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item == null) return;

            var tags = item.Tags.ToList();
            if (tags.Contains(trimmed))
                tags.Remove(trimmed);
            else
                tags.Add(trimmed);

            item.Tags = tags;
            ScheduleSave();
        }
    }

    /// <summary>
    /// 编辑指定记录的文本内容
    /// </summary>
    public void UpdateText(Guid itemId, string newText)
    {
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == itemId);
            if (item == null) return;

            item.Text = newText;
            ScheduleSave();
        }
    }

    /// <summary>
    /// 删除指定分组标签（将该标签下所有项的标签清除）
    /// </summary>
    public void DeleteTag(string tag)
    {
        lock (_lock)
        {
            // 清除项目上的标签
            foreach (var item in _items.Where(x => x.Tags.Contains(tag)))
            {
                item.Tags = item.Tags.Where(x => x != tag).ToList();
            }
            // 清除独立标签
            _standaloneTags.Remove(tag);
            ScheduleSave();
        }
    }

    private void TrimToCapacity()
    {
        while (_items.Count > _maxCapacity)
        {
            var removed = false;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                // 置顶或已分组记录属于用户明确保留的数据，不参与容量淘汰。
                if (!_items[i].IsPinned && !_items[i].HasTags)
                {
                    var item = _items[i];
                    _items.RemoveAt(i);
                    _recycleBin.Insert(0, new RecycleBinEntry { Item = item });
                    removed = true;
                    break;
                }
            }

            // 全部剩余记录都受保护时允许暂时超过容量上限。
            if (!removed)
                break;
        }
    }

    private static DateTime GetCutoffDate(RetentionPeriod period, DateTime now)
    {
        return period switch
        {
            RetentionPeriod.ThreeDays => now.AddDays(-3),
            RetentionPeriod.SevenDays => now.AddDays(-7),
            RetentionPeriod.OneMonth => now.AddDays(-30),
            RetentionPeriod.ThreeMonths => now.AddDays(-90),
            _ => DateTime.MinValue
        };
    }

    private static List<string> ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        return value
            .Split(new[] { ',', '，', ';', '；', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
