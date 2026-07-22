using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClipVault.Helpers;
using ClipVault.Models;
using ClipVault.Services;

namespace ClipVault.ViewModels;

/// <summary>
/// 浮动面板 ViewModel — 搜索、Pin、选择粘贴、删除
/// </summary>
public partial class PopupViewModel : ViewModelBase
{
    private readonly ClipboardStore _store;
    private readonly ClipboardMonitor _monitor;

    // ===== 滚动分页加载 =====
    private List<ClipboardItem>? _allFiltered;  // 完整筛选结果
    private int _loadedCount;                  // 已加载到 UI 的数量
    private const int InitialPageSize = 15;    // 首屏加载条数
    private const int PageSize = 20;           // 每次滚动加载条数
    private bool _isReorderingTags = false;   // 拖拽排序中标记

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 当前选中的标签筛选（null 或 "全部" 表示显示所有）
    /// </summary>
    [ObservableProperty]
    private string? _selectedTag = null;

    /// <summary>
    /// 所有可用标签列表
    /// </summary>
    public ObservableCollection<string> Tags { get; } = new();

    public bool CanReorderGroupItems => !string.IsNullOrEmpty(SelectedTag)
        && string.IsNullOrWhiteSpace(SearchText);

    public ReadOnlyObservableCollection<RecycleBinEntry> RecycleBin => _store.RecycleBin;

    public int RecycleBinCount => _store.RecycleBin.Count;

    /// <summary>
    /// 空状态可见性
    /// </summary>
    public Visibility EmptyStateVisibility => FilteredItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 过滤后的剪贴板列表（供 UI 绑定）
    /// </summary>
    public RangeObservableCollection<ClipboardItem> FilteredItems { get; }

    public PopupViewModel(ClipboardStore store, ClipboardMonitor monitor)
    {
        _store = store;
        _monitor = monitor;
        FilteredItems = [];

        // 初始加载
        RefreshFilteredItems();
        RefreshTags();

        // 订阅 store 变化（防抖：连续变更合并为一次刷新）
        ((INotifyCollectionChanged)store.Items).CollectionChanged += (_, _) =>
        {
            ScheduleRefresh();
        };
        ((INotifyCollectionChanged)store.RecycleBin).CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RecycleBinCount));
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanReorderGroupItems));
        RefreshFilteredItems();
    }

    partial void OnSelectedTagChanged(string? value)
    {
        OnPropertyChanged(nameof(CanReorderGroupItems));
        RefreshFilteredItems();
    }

    /// <summary>
    /// 刷新标签列表（保持已有顺序，增删变更项）
    /// </summary>
    private void RefreshTags()
    {
        var currentTags = _store.GetAllTags();

        // 按currentTags的顺序重建集合，保持拖拽后的排序
        var toRemove = Tags.Except(currentTags).ToList();
        foreach (var t in toRemove) Tags.Remove(t);

        // 按 currentTags 顺序补充新标签
        foreach (var t in currentTags)
        {
            if (!Tags.Contains(t))
                Tags.Add(t);
        }

        // 如果选中的标签不存在了，重置为全部
        if (!string.IsNullOrEmpty(SelectedTag) && !currentTags.Contains(SelectedTag))
        {
            SelectedTag = null;
        }
    }

    private DispatcherTimer? _refreshTimer;

    /// <summary>
    /// 防抖刷新：连续变更合并为一次刷新（50ms 内多次 CollectionChanged 只执行一次）
    /// </summary>
    private void ScheduleRefresh()
    {
        // 拖拽排序期间不刷新标签，避免覆盖拖拽顺序
        if (_isReorderingTags) return;

        if (_refreshTimer == null)
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _refreshTimer.Tick += (_, _) =>
            {
                _refreshTimer!.Stop();
                RefreshFilteredItems();
                RefreshTags();
            };
        }
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>
    /// 刷新过滤后的列表（使用 ReplaceAll 批量替换，仅一次通知）
    /// </summary>
    private void RefreshFilteredItems()
    {
        var query = SearchText?.Trim().ToLowerInvariant() ?? string.Empty;
        var activeTag = string.IsNullOrEmpty(SelectedTag) ? null : SelectedTag;

        // 构建目标列表
        var desired = new List<ClipboardItem>();
        foreach (var item in _store.Items)
        {
            // 标签过滤
            if (activeTag != null && !item.Tags.Contains(activeTag))
                continue;

            // 搜索文本过滤
            if (string.IsNullOrEmpty(query) ||
                (item.PreviewText?.ToLowerInvariant().Contains(query) ?? false) ||
                item.TagsText.ToLowerInvariant().Contains(query) ||
                item.SmartTypeText.ToLowerInvariant().Contains(query))
            {
                desired.Add(item);
            }
        }

        if (activeTag != null)
            desired = _store.OrderItemsForTag(activeTag, desired);

        // 快速路径：如果数量和内容都没变，跳过
        if (desired.Count == FilteredItems.Count)
        {
            bool same = true;
            for (int i = 0; i < desired.Count; i++)
            {
                if (!ReferenceEquals(desired[i], FilteredItems[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same)
            {
                OnPropertyChanged(nameof(EmptyStateVisibility));
                return;
            }
        }

        // 清空滚动分页状态
        _allFiltered = null;

        if (desired.Count <= InitialPageSize)
        {
            // 小列表：直接全部渲染
            _allFiltered = null;
            _loadedCount = desired.Count;
            FilteredItems.ReplaceAll(desired);
        }
        else
        {
            // 大列表：首屏只渲染前 30 张，剩余等用户滚动到末尾再加载
            _allFiltered = desired;
            _loadedCount = InitialPageSize;
            FilteredItems.ReplaceAll(desired.Take(InitialPageSize).ToList());
        }

        // 更新空状态可见性
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    /// <summary>
    /// 滚动到末尾时调用：加载下一页
    /// </summary>
    public void LoadMore()
    {
        if (_allFiltered == null) return;
        if (_loadedCount >= _allFiltered.Count) return;

        var nextCount = Math.Min(_loadedCount + PageSize, _allFiltered.Count);
        for (int i = _loadedCount; i < nextCount; i++)
            FilteredItems.Add(_allFiltered[i]);
        _loadedCount = nextCount;

        // 全部加载完，清空引用
        if (_loadedCount >= _allFiltered.Count)
            _allFiltered = null;
    }

    /// <summary>
    /// 单击历史项 — 仅复制到剪贴板（不关闭面板）
    /// </summary>
    [RelayCommand]
    public void SelectItem(ClipboardItem item)
    {
        CopyToClipboard(item);
    }

    /// <summary>
    /// 双击历史项 — 立即关闭面板 + 异步复制粘贴
    /// 关键优化：先隐藏窗口（用户感知 0ms），再做复制+粘贴
    /// </summary>
    [RelayCommand]
    public void PasteItem(ClipboardItem item)
    {
        if (item == null) return;

        // 在“全部”视图下，将粘贴的项移到最前
        if (string.IsNullOrEmpty(SelectedTag))
        {
            _store.MoveToFront(item.Id);
        }

        RequestPaste?.Invoke(item);
    }

    /// <summary>
    /// 使用 Win32 API 快速写入剪贴板（失败立即返回，无 OLE 超时）
    /// </summary>
    public bool CopyToClipboardFast(ClipboardItem item)
    {
        bool success = false;

        _monitor.SetClipboardSafely(() =>
        {
            switch (item.Type)
            {
                case ClipboardItemType.Text:
                case ClipboardItemType.Rtf:
                case ClipboardItemType.Html:
                    success = FastClipboard.TrySetTextWithRetry(item.Text ?? string.Empty, maxAttempts: 3);
                    break;

                default:
                    success = CopyToClipboardViaWpf(item);
                    break;
            }
        });

        return success;
    }

    /// <summary>
    /// 回退方案：使用 WPF Clipboard（有 OLE 超时风险）
    /// </summary>
    private bool CopyToClipboardViaWpf(ClipboardItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ClipboardItemType.Image:
                    if (item.Image != null)
                    {
                        Clipboard.SetImage(item.Image);
                        return true;
                    }
                    break;
                case ClipboardItemType.Files:
                    if (item.FilePaths != null && item.FilePaths.Count > 0)
                    {
                        var collection = new System.Collections.Specialized.StringCollection();
                        collection.AddRange(item.FilePaths.ToArray());
                        Clipboard.SetFileDropList(collection);
                        return true;
                    }
                    break;
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
        return false;
    }

    /// <summary>
    /// 将剪贴板项内容安全写入系统剪贴板（带回环抑制）— 用于单击复制
    /// </summary>
    private void CopyToClipboard(ClipboardItem item)
    {
        CopyToClipboardFast(item);
    }

    /// <summary>
    /// 切换 Pin 状态
    /// </summary>
    [RelayCommand]
    public void TogglePin(ClipboardItem item)
    {
        _store.TogglePin(item.Id);
    }

    /// <summary>
    /// 删除单条记录
    /// </summary>
    [RelayCommand]
    public void DeleteItem(ClipboardItem item)
    {
        _store.Remove(item.Id);
    }

    public bool RestoreItem(RecycleBinEntry entry) => _store.Restore(entry.Item.Id);

    public bool DeletePermanently(RecycleBinEntry entry) => _store.DeletePermanently(entry.Item.Id);

    public int EmptyRecycleBin() => _store.EmptyRecycleBin();

    [RelayCommand]
    public void OpenRecycleBin()
    {
        RequestOpenRecycleBin?.Invoke();
    }

    /// <summary>
    /// 清空所有记录（保留置顶项）
    /// </summary>
    [RelayCommand]
    public void ClearAll()
    {
        // 只清除非置顶项
        var toRemove = _store.Items.Where(x => !x.IsPinned).ToList();
        foreach (var item in toRemove)
        {
            _store.Remove(item.Id);
        }
    }

    /// <summary>
    /// 为指定记录设置分组标签
    /// </summary>
    [RelayCommand]
    public void SetTag(ClipboardItem item)
    {
        // 由 View 层弹出输入框
        RequestSetTag?.Invoke(item);
    }

    /// <summary>
    /// 实际执行设置标签
    /// </summary>
    public void ApplyTag(ClipboardItem item, string? tag)
    {
        _store.SetTag(item.Id, tag);
        RefreshTags();
        RefreshFilteredItems();
    }

    public void ToggleTag(ClipboardItem item, string tag)
    {
        _store.ToggleTag(item.Id, tag);
        RefreshTags();
        RefreshFilteredItems();
    }

    /// <summary>
    /// 编辑剪贴板项的文本内容
    /// </summary>
    public void UpdateText(ClipboardItem item, string newText)
    {
        _store.UpdateText(item.Id, newText);
        RefreshFilteredItems();
    }

    /// <summary>
    /// 创建新的分组标签（不关联项目）
    /// </summary>
    public bool CreateTag(string tag)
    {
        var success = _store.CreateTag(tag);
        if (success)
        {
            RefreshTags();
        }
        return success;
    }

    /// <summary>
    /// 拖拽排序开始/结束标记
    /// </summary>
    public void SetReorderingTags(bool value)
    {
        _isReorderingTags = value;
    }

    /// <summary>
    /// 拖拽排序后调用
    /// </summary>
    public void ReorderTags()
    {
        _store.ReorderTags(Tags.ToList());
    }

    public void ReorderGroupItem(ClipboardItem draggedItem, ClipboardItem targetItem)
    {
        if (!CanReorderGroupItems || string.IsNullOrEmpty(SelectedTag) || ReferenceEquals(draggedItem, targetItem)
            || draggedItem.IsPinned != targetItem.IsPinned)
            return;

        var fullGroup = _store.OrderItemsForTag(SelectedTag,
            _store.Items.Where(x => x.Tags.Contains(SelectedTag)));
        var fromIndex = fullGroup.FindIndex(x => x.Id == draggedItem.Id);
        var targetIndex = fullGroup.FindIndex(x => x.Id == targetItem.Id);
        if (fromIndex < 0 || targetIndex < 0 || fromIndex == targetIndex)
            return;

        fullGroup.RemoveAt(fromIndex);
        fullGroup.Insert(targetIndex, draggedItem);
        _store.ReorderGroupItems(SelectedTag, fullGroup.Select(x => x.Id).ToList());
        RefreshFilteredItems();
    }

    /// <summary>
    /// 删除一个分组标签
    /// </summary>
    [RelayCommand]
    public void DeleteTag(string tag)
    {
        _store.DeleteTag(tag);
        RefreshTags();
        RefreshFilteredItems();
    }

    /// <summary>
    /// 选择标签进行筛选（传 null 或空字符串表示 "全部"）
    /// </summary>
    [RelayCommand]
    public void SelectTag(string? tag)
    {
        SelectedTag = string.IsNullOrEmpty(tag) ? null : tag;
    }

    /// <summary>
    /// 请求自动粘贴（双击后由 View 层隐藏窗口+复制+模拟 Ctrl+V）
    /// 参数：要粘贴的 ClipboardItem
    /// </summary>
    public event Action<ClipboardItem>? RequestPaste;

    /// <summary>
    /// 请求设置分组标签（由 View 层弹出输入框）
    /// </summary>
    public event Action<ClipboardItem>? RequestSetTag;

    public event Action? RequestOpenRecycleBin;
}
