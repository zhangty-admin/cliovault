using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ClipVault.Helpers;
using ClipVault.Models;
using ClipVault.ViewModels;

namespace ClipVault.Views;

/// <summary>
/// 标签转可见性转换器（非空标签显示，空标签隐藏）
/// </summary>
public class TagToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 剪贴板卡片 UserControl
/// </summary>
public partial class ClipboardItemCard : UserControl
{
    private Point _groupDragStart;

    public static readonly DependencyProperty IsGroupReorderEnabledProperty = DependencyProperty.Register(
        nameof(IsGroupReorderEnabled), typeof(bool), typeof(ClipboardItemCard), new PropertyMetadata(false));

    public bool IsGroupReorderEnabled
    {
        get => (bool)GetValue(IsGroupReorderEnabledProperty);
        set => SetValue(IsGroupReorderEnabledProperty, value);
    }
    public static readonly RoutedEvent ItemSelectedEvent = EventManager.RegisterRoutedEvent(
        nameof(ItemSelected), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ClipboardItemCard));

    public static readonly RoutedEvent PinToggledEvent = EventManager.RegisterRoutedEvent(
        nameof(PinToggled), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ClipboardItemCard));

    public static readonly RoutedEvent DeleteRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(DeleteRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ClipboardItemCard));

    public static readonly RoutedEvent ItemDoubleClickedEvent = EventManager.RegisterRoutedEvent(
        nameof(ItemDoubleClicked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ClipboardItemCard));

    public static readonly RoutedEvent TagSetRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(TagSetRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ClipboardItemCard));

    public event RoutedEventHandler ItemSelected
    {
        add => AddHandler(ItemSelectedEvent, value);
        remove => RemoveHandler(ItemSelectedEvent, value);
    }

    public event RoutedEventHandler PinToggled
    {
        add => AddHandler(PinToggledEvent, value);
        remove => RemoveHandler(PinToggledEvent, value);
    }

    public event RoutedEventHandler DeleteRequested
    {
        add => AddHandler(DeleteRequestedEvent, value);
        remove => RemoveHandler(DeleteRequestedEvent, value);
    }

    public event RoutedEventHandler ItemDoubleClicked
    {
        add => AddHandler(ItemDoubleClickedEvent, value);
        remove => RemoveHandler(ItemDoubleClickedEvent, value);
    }

    public event RoutedEventHandler TagSetRequested
    {
        add => AddHandler(TagSetRequestedEvent, value);
        remove => RemoveHandler(TagSetRequestedEvent, value);
    }

    public ClipboardItemCard()
    {
        InitializeComponent();
    }

    private void Card_DoubleClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ItemDoubleClickedEvent, this));
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(PinToggledEvent, this));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(DeleteRequestedEvent, this));
    }

    private void Tag_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(TagSetRequestedEvent, this));
    }

    // ===== 右键菜单事件 =====

    /// <summary>
    /// 右键菜单打开时动态填充分组子菜单
    /// </summary>
    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var item = DataContext as ClipboardItem;

        // 编辑项仅对文本类型可见
        if (item != null)
        {
            bool canEdit = item.Type is ClipboardItemType.Text
                or ClipboardItemType.Rtf
                or ClipboardItemType.Html;
            EditMenuItem.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
            EditSeparator.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        }

        // 从顶层 PopupWindow 的 DataContext 获取 VM 的 Tags 列表
        var tags = GetAvailableTags();

        TagMenuItem.Items.Clear();

        // 如果当前项已有分组，显示「取消全部分组」
        if (item != null && item.HasTags)
        {
            var removeTagItem = new MenuItem
            {
                Header = "✕ 取消全部分组",
                FontWeight = FontWeights.Normal,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("SecondaryTextBrush")
            };
            removeTagItem.Click += (_, _) => RaiseTagSet(null);
            TagMenuItem.Items.Add(removeTagItem);

            if (tags.Count > 0)
                TagMenuItem.Items.Add(new Separator());
        }

        // 已有分组列表
        foreach (var tag in tags)
        {
            var currentTag = tag;
            var tagItem = new MenuItem
            {
                Header = tag,
                FontWeight = FontWeights.Normal
            };

            // 当前项已绑定的分组高亮显示，点击可取消。
            if (DataContext is ClipboardItem ci && ci.Tags.Contains(currentTag))
            {
                tagItem.Header = "✓ " + currentTag;
                tagItem.FontWeight = FontWeights.Bold;
            }

            tagItem.Click += (_, _) => ToggleTag(currentTag);
            TagMenuItem.Items.Add(tagItem);
        }

        // 新建分组
        if (tags.Count > 0 || (DataContext is ClipboardItem ci2 && ci2.HasTags))
            TagMenuItem.Items.Add(new Separator());

        var newTagItem = new MenuItem
        {
            Header = "＋ 新建分组…",
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("AccentBrush")
        };
        newTagItem.Click += (_, _) => RaiseEvent(new RoutedEventArgs(TagSetRequestedEvent, this));
        TagMenuItem.Items.Add(newTagItem);
    }

    /// <summary>
    /// 向上遍历可视化树，从 PopupWindow 的 DataContext 获取 Tags
    /// </summary>
    private List<string> GetAvailableTags()
    {
        try
        {
            var window = Window.GetWindow(this);
            if (window?.DataContext is PopupViewModel vm)
                return vm.Tags.ToList();
        }
        catch { }
        return new List<string>();
    }

    /// <summary>
    /// 触发分组设置事件（通过冒泡到 PopupWindow）
    /// </summary>
    private void RaiseTagSet(string? tag)
    {
        if (DataContext is not ClipboardItem item) return;
        if (Window.GetWindow(this)?.DataContext is PopupViewModel vm)
        {
            vm.ApplyTag(item, tag);
        }
    }

    private void GroupDragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _groupDragStart = e.GetPosition(this);
        e.Handled = true;
    }

    private void GroupDragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsGroupReorderEnabled || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed
            || DataContext is not ClipboardItem item)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _groupDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _groupDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        e.Handled = true;
        DragDrop.DoDragDrop(this, item, DragDropEffects.Move);
    }

    private void Card_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = IsGroupReorderEnabled && e.Data.GetDataPresent(typeof(ClipboardItem))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Card_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!IsGroupReorderEnabled
            || e.Data.GetData(typeof(ClipboardItem)) is not ClipboardItem draggedItem
            || DataContext is not ClipboardItem targetItem
            || Window.GetWindow(this)?.DataContext is not PopupViewModel vm)
            return;

        vm.ReorderGroupItem(draggedItem, targetItem);
        e.Handled = true;
    }

    private void ToggleTag(string tag)
    {
        if (DataContext is not ClipboardItem item) return;
        if (Window.GetWindow(this)?.DataContext is PopupViewModel vm)
        {
            vm.ToggleTag(item, tag);
        }
    }

    private void Menu_Pin_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(PinToggledEvent, this));
    }

    private void Menu_Delete_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(DeleteRequestedEvent, this));
    }

    private void Menu_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ClipboardItem item) return;
        if (item.Type is not (ClipboardItemType.Text or ClipboardItemType.Rtf or ClipboardItemType.Html)) return;
        if (Window.GetWindow(this)?.DataContext is not PopupViewModel vm) return;

        var dialog = new EditContentDialog(item)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            vm.UpdateText(item, dialog.EditedText);
        }
    }
}
