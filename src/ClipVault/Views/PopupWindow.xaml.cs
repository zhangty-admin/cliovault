using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipVault.Helpers;
using ClipVault.Helpers.Win32;
using ClipVault.Models;
using ClipVault.ViewModels;

namespace ClipVault.Views;

/// <summary>
/// 浮动面板窗口 — 无边框、置顶、半透明、弹出/收起动画
/// </summary>
public partial class PopupWindow : Window
{
    private bool _isClosing = false;
    private bool _isPasting = false;
    private bool _isAnimating = false;  // 动画进行中
    private DateTime _lastShownTime;   // 上次 Show 时间

    // 窗口布局常量
    private const double WindowHeight = 380;
    private const double BottomMargin = 20;
    private const double FadeInMs = 150;
    private const double FadeOutMs = 120;
    private const double CardScrollAmount = 252;
    private const double TagScrollAmount = 60;
    private const double LoadMoreThreshold = 500;
    private const int PasteTargetFocusDelayMs = 200;
    private const int PasteClipboardSyncDelayMs = 300;
    private const int JumpServerDirectInputMaxLength = 100;

    private ClipboardItem? _pendingPasteItem = null;
    private string _pasteTargetWindowTitle = string.Empty;

    // ===== 卡片列表拖拽滑动字段 =====
    private bool _isCardDragging = false;
    private Point _cardDragStartPoint;
    private double _cardDragStartOffset;
    private DateTime _cardDragStartTime;
    private double _cardDragVelocity;
    private DispatcherTimer? _cardInertiaTimer;
    private const double CardDragThreshold = 5.0;
    private const double CardFriction = 0.92;

    // ===== 标签拖拽排序字段 =====
    private Point _dragStartPoint;
    private string? _draggingTag = null;
    private bool _isDragging = false;
    private Border? _draggingBorder = null;
    private double _lastSwapX = double.NaN;  // 上次交换时的鼠标 X（TagBar 坐标）

    public PopupWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => PositionAtBottom();

        // 监听 Activated 事件
        Activated += (_, _) => { };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is PopupViewModel vm)
            {
                vm.RequestPaste += OnRequestPaste;
                vm.RequestSetTag += OnRequestSetTag;
                vm.RequestOpenRecycleBin += OnRequestOpenRecycleBin;
            }
        };
    }

    /// <summary>
    /// 在 Show() 之前调用：捕获鼠标当前所在的显示器信息（物理像素）
    /// 必须在 Show/Activate 之前调用，否则前台窗口是 PopupWindow 自己
    /// </summary>
    /// <remarks>
    /// 优先用鼠标光标位置判断显示器（符合“鼠标点哪个屏就弹哪个屏”的直觉）；
    /// 获取失败时回退到前台窗口所在显示器。
    /// </remarks>
    private MONITORINFO? CaptureActiveMonitor()
    {
        try
        {
            IntPtr monitor = IntPtr.Zero;

            // ★ 优先：鼠标光标所在显示器 — 用户“点击屏幕2”后鼠标就在屏幕2上
            if (User32.GetCursorPos(out POINT pt))
            {
                monitor = User32.MonitorFromPoint(pt, Win32Constants.MONITOR_DEFAULTTONEAREST);
            }

            // 回退：以前台窗口所在显示器判断（前台窗口不等于鼠标所在屏）
            if (monitor == IntPtr.Zero)
            {
                IntPtr hwnd = User32.GetForegroundWindow();
                if (hwnd != IntPtr.Zero &&
                    hwnd != new System.Windows.Interop.WindowInteropHelper(this).Handle)
                {
                    monitor = User32.MonitorFromWindow(hwnd, Win32Constants.MONITOR_DEFAULTTONEAREST);
                }
            }

            if (monitor == IntPtr.Zero) return null;

            var info = new MONITORINFO();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>();

            if (User32.GetMonitorInfo(monitor, ref info))
            {
                return info;
            }
        }
        catch { }
        // 异常时返回 null
        return null;
    }

    /// <summary>
    /// 将物理像素的 MONITORINFO 转换为 WPF DIP 坐标的 Rect
    /// 必须在窗口已 Show 之后调用（需要 PresentationSource 做 DPI 转换）
    /// </summary>
    private Rect MonitorToDipRect(MONITORINFO info)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            double m11 = source.CompositionTarget.TransformToDevice.M11;
            double m22 = source.CompositionTarget.TransformToDevice.M22;

            return new Rect(
                info.rcWork.Left / m11,
                info.rcWork.Top / m22,
                info.rcWork.Width / m11,
                info.rcWork.Height / m22);
        }
        // 回退：假设 100% 缩放
        return new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height);
    }

    private void PositionAtBottom(MONITORINFO? monitorInfo)
    {
        Rect workArea;
        if (monitorInfo != null)
        {
            workArea = MonitorToDipRect(monitorInfo.Value);
        }
        else
        {
            workArea = SystemParameters.WorkArea;
        }
        // 固定高度，宽度 = 屏幕工作区宽度，贴底部
        Height = WindowHeight;
        Width = workArea.Width;
        Left = workArea.Left;
        Top = workArea.Bottom - WindowHeight - BottomMargin;
    }

    private void PositionAtBottom()
    {
        PositionAtBottom(null);
    }

    /// <summary>
    /// 强制窗口置顶
    /// </summary>
    private void ForceBringToFront()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            User32.SetWindowPos(hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0,
                User32.SWP_NOMOVE | User32.SWP_NOSIZE);
            User32.SetForegroundWindow(hwnd);
            User32.BringWindowToTop(hwnd);
        }
        catch { }
    }

    public void ShowWithAnimation()
    {
        try
        {
            _isClosing = false;
            _isPasting = false;

            var activeMonitor = CaptureActiveMonitor();
            _pasteTargetWindowTitle = GetForegroundWindowTitle();
            Show();
            Activate();

            BeginAnimation(OpacityProperty, null);
            Opacity = 1;

            PositionAtBottom(activeMonitor);

            SearchBox.Focus();
            SearchBox.SelectAll();

            ForceBringToFront();

            // 淡入动画
            Opacity = 0;
            _isAnimating = true;
            _lastShownTime = DateTime.Now;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeInMs));
            fadeIn.Completed += (_, _) =>
            {
                _isAnimating = false;
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[WINDOW] ShowWithAnimation 异常: {ex}");
        }
    }

    /// <summary>
    /// 双击粘贴：先隐藏窗口并写入剪贴板，等待目标窗口恢复焦点后再粘贴。
    /// 浏览器远程桌面还需要把本机剪贴板同步到远端，不能在写入后立即发送 Ctrl+V。
    /// </summary>
    private async void OnRequestPaste(ClipboardItem item)
    {
        _pendingPasteItem = item;
        _isPasting = true;

        Hide();

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Input);
            await Task.Delay(PasteTargetFocusDelayMs);

            if (DataContext is not PopupViewModel vm || _pendingPasteItem == null)
                return;

            var pasteItem = _pendingPasteItem;
            _pendingPasteItem = null;

            if (vm.CopyToClipboardFast(pasteItem))
            {
                if (IsJumpServerTextPaste(pasteItem))
                {
                    if (!DirectTextInput.TrySend(pasteItem.Text ?? string.Empty))
                    {
                        MessageBox.Show("向 JumpServer 输入文本失败，请稍后重试。",
                            "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    await Task.Delay(PasteClipboardSyncDelayMs);
                    DoPaste();
                }
            }
            else
            {
                MessageBox.Show("写入剪贴板失败，已取消粘贴。请稍后重试。",
                    "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _isPasting = false;
        }
    }

    private void OnRequestOpenRecycleBin()
    {
        if (DataContext is not PopupViewModel vm) return;
        var window = new RecycleBinWindow(vm) { Owner = this };
        window.ShowDialog();
    }

    /// <summary>
    /// 普通隐藏路径（Esc / Deactivated）
    /// </summary>
    public void HideWithAnimation()
    {
        if (_isClosing || !IsVisible) return;
        _isClosing = true;

        BeginAnimation(OpacityProperty, null);

        _isAnimating = true;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FadeOutMs));
        fadeOut.Completed += (_, _) =>
        {
            _isAnimating = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Hide();
            _isClosing = false;
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideWithAnimation();
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 正在粘贴/动画中/关闭中 不响应
        if (_isPasting || _isAnimating || _isClosing)
            return;

        // 动画完成后的 Deactivated = 用户点击了外部，直接关闭
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isClosing || _isPasting || _isAnimating) return;
            HideWithAnimation();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 执行 Ctrl+V 模拟粘贴
    /// </summary>
    private static void DoPaste()
    {
        User32.keybd_event(Win32Constants.VK_CONTROL, 0, 0, IntPtr.Zero);
        User32.keybd_event(Win32Constants.VK_V, 0, 0, IntPtr.Zero);
        User32.keybd_event(Win32Constants.VK_V, 0, Win32Constants.KEYEVENTF_KEYUP, IntPtr.Zero);
        User32.keybd_event(Win32Constants.VK_CONTROL, 0, Win32Constants.KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    private bool IsJumpServerTextPaste(ClipboardItem item)
    {
        bool isText = item.Type is ClipboardItemType.Text or ClipboardItemType.Rtf or ClipboardItemType.Html;
        int textLength = item.Text?.Length ?? 0;
        return isText
            && textLength <= JumpServerDirectInputMaxLength
            && _pasteTargetWindowTitle.Contains("JumpServer", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetForegroundWindowTitle()
    {
        IntPtr hwnd = User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return string.Empty;

        int length = User32.GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;

        var title = new StringBuilder(length + 1);
        return User32.GetWindowText(hwnd, title, title.Capacity) > 0 ? title.ToString() : string.Empty;
    }

    /// <summary>
    /// 鼠标滚轮 → 横向滚动
    /// </summary>
    private void CardScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double scrollAmount = e.Delta > 0 ? -CardScrollAmount : CardScrollAmount;
        CardScrollViewer.ScrollToHorizontalOffset(CardScrollViewer.HorizontalOffset + scrollAmount);
        e.Handled = true;
    }

    // ===== 卡片列表拖拽滑动（惯性平滑滚动） =====

    /// <summary>
    /// 鼠标按下：记录起点，准备拖拽
    /// </summary>
    private void CardScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 停止惯性动画
        _cardInertiaTimer?.Stop();

        _cardDragStartPoint = e.GetPosition(CardScrollViewer);
        _cardDragStartOffset = CardScrollViewer.HorizontalOffset;
        _cardDragStartTime = DateTime.Now;
        _cardDragVelocity = 0;
        _isCardDragging = false;
    }

    /// <summary>
    /// 鼠标移动：超过阈值后拖拽滑动
    /// </summary>
    private void CardScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(CardScrollViewer);
        var diff = pos.X - _cardDragStartPoint.X;

        if (!_isCardDragging)
        {
            if (Math.Abs(diff) > CardDragThreshold)
            {
                _isCardDragging = true;
            }
            else
            {
                return;
            }
        }

        // 拖拽中：实时跟随鼠标
        var newOffset = _cardDragStartOffset - diff;
        newOffset = Math.Max(0, Math.Min(newOffset, CardScrollViewer.ScrollableWidth));
        CardScrollViewer.ScrollToHorizontalOffset(newOffset);

        // 记录速度（用于惯性）
        var elapsed = (DateTime.Now - _cardDragStartTime).TotalMilliseconds;
        if (elapsed > 0)
        {
            _cardDragVelocity = -diff / elapsed * 15;  // px per frame (~15ms)
        }
    }

    /// <summary>
    /// 鼠标松开：启动惯性滑动
    /// </summary>
    private void CardScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isCardDragging)
        {
            return;
        }

        _isCardDragging = false;

        // 速度太小就不做惯性
        if (Math.Abs(_cardDragVelocity) < 1) return;

        // 启动惯性计时器
        _cardInertiaTimer?.Stop();
        _cardInertiaTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        var sv = CardScrollViewer;
        _cardInertiaTimer.Tick += (_, _) =>
        {
            _cardDragVelocity *= CardFriction;

            if (Math.Abs(_cardDragVelocity) < 0.5)
            {
                _cardInertiaTimer!.Stop();
                return;
            }

            var newOffset = sv.HorizontalOffset + _cardDragVelocity;
            newOffset = Math.Max(0, Math.Min(newOffset, sv.ScrollableWidth));
            sv.ScrollToHorizontalOffset(newOffset);
        };
        _cardInertiaTimer.Start();
    }

    /// <summary>
    /// 标签栏滚轮 → 横向滚动
    /// </summary>
    private void TagBarScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller) return;

        double scrollAmount = e.Delta > 0 ? -TagScrollAmount : TagScrollAmount;
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + scrollAmount);
        e.Handled = true;
    }

    /// <summary>
    /// 滚动位置变化 → 检测是否需要加载更多卡片
    /// </summary>
    private void CardScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 拖拽滑动中不触发加载（避免惯性滑动时频繁加载）
        if (_isCardDragging) return;
        if (DataContext is not PopupViewModel vm) return;

        // 距离末尾小于 500px 时触发加载
        double remaining = CardScrollViewer.ScrollableWidth - CardScrollViewer.HorizontalOffset;
        if (remaining < LoadMoreThreshold)
        {
            vm.LoadMore();
        }
    }

    // ===== 标签栏事件 =====

    /// <summary>
    /// 点击标签 chip → 按标签筛选
    /// </summary>
    private void TagChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not string tag) return;
        if (DataContext is not PopupViewModel vm) return;

        // 如果点击的是当前已选中的标签，则取消选中
        if (vm.SelectedTag == tag)
        {
            vm.SelectTagCommand.Execute(null);
            HighlightSelectedTag(null);
        }
        else
        {
            vm.SelectTagCommand.Execute(tag);
            HighlightSelectedTag(tag);
        }
    }

    /// <summary>
    /// 点击标签上的 ✕ → 删除标签
    /// </summary>
    private void TagDelete_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not string tag) return;
        if (DataContext is not PopupViewModel vm) return;

        vm.DeleteTagCommand.Execute(tag);
    }

    /// <summary>
    /// 点击 "全部" → 清除标签筛选
    /// </summary>
    private void AllTag_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PopupViewModel vm) return;
        vm.SelectTagCommand.Execute(null);
        HighlightSelectedTag(null);
    }

    // ===== 新建分组输入框事件 =====

    private const string PlaceholderText = "请输入分类名";
    private bool _isPlaceholderActive = false;

    /// <summary>
    /// 点击 ＋ 按钮 → 显示输入框并聚焦
    /// </summary>
    private void AddTag_Click(object sender, MouseButtonEventArgs e)
    {
        NewTagInputBox.Visibility = Visibility.Visible;
        NewTagInput.Focus();   // 先聚焦（此时 _isPlaceholderActive=false，GotFocus 不做任何事）
        ShowPlaceholder();     // 聚焦后再显示提示字
    }

    /// <summary>
    /// 输入框获得焦点 → 清除提示字（仅当从外部点击回来时）
    /// </summary>
    private void NewTagInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_isPlaceholderActive)
            ClearPlaceholder();
    }

    /// <summary>
    /// 用户开始输入 → 先清掉提示字再处理按键
    /// </summary>
    private void NewTagInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 有提示字时，任何输入键都先清除提示字
        if (_isPlaceholderActive && e.Key != Key.Escape)
        {
            ClearPlaceholder();
        }
    }

    /// <summary>
    /// 输入框失去焦点 → 保存分组
    /// </summary>
    private void NewTagInput_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitNewTag();
    }

    /// <summary>
    /// 按 Enter 确认 / Esc 取消
    /// </summary>
    private void NewTagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitNewTag();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelNewTag();
        }
    }

    /// <summary>
    /// 确认保存新分组
    /// </summary>
    private void CommitNewTag()
    {
        var name = _isPlaceholderActive ? string.Empty : NewTagInput.Text?.Trim();
        NewTagInputBox.Visibility = Visibility.Collapsed;

        if (string.IsNullOrEmpty(name)) return;
        if (DataContext is not PopupViewModel vm) return;

        vm.CreateTag(name);
    }

    /// <summary>
    /// 取消新建分组
    /// </summary>
    private void CancelNewTag()
    {
        NewTagInput.Text = string.Empty;
        _isPlaceholderActive = false;
        NewTagInputBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 显示提示字
    /// </summary>
    private void ShowPlaceholder()
    {
        NewTagInput.Text = PlaceholderText;
        NewTagInput.Foreground = (Brush)FindResource("SecondaryTextBrush");
        _isPlaceholderActive = true;
    }

    /// <summary>
    /// 清除提示字
    /// </summary>
    private void ClearPlaceholder()
    {
        NewTagInput.Text = string.Empty;
        NewTagInput.Foreground = (Brush)FindResource("PrimaryTextBrush");
        _isPlaceholderActive = false;
    }

    // ===== 标签拖拽排序（实时交换 + 滑动动画 + 浮动幻影） =====

    /// <summary>
    /// 鼠标按下：记录起点 + 识别哪个标签被拖拽
    /// </summary>
    private void TagBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _draggingTag = GetTagAtPosition(e.GetPosition(TagBar));
        _draggingBorder = _draggingTag != null ? GetBorderForTag(_draggingTag) : null;
    }

    /// <summary>
    /// 鼠标移动（TagBar 级）：超过阈值后启动拖拽
    /// </summary>
    private void TagBar_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_draggingTag == null || _isDragging) return;

        var pos = e.GetPosition(this);
        var diff = _dragStartPoint - pos;

        // 移动超过 4px 才触发拖拽
        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
        {
            _isDragging = true;

            // 通知 ViewModel 拖拽开始，禁止标签刷新
            if (DataContext is PopupViewModel vm)
                vm.SetReorderingTags(true);

            // 原始标签变半透明（留在原位但变淡）
            if (_draggingBorder != null)
                _draggingBorder.Opacity = 0.2;

            // 显示拖拽幻影
            DragGhostBorder.Opacity = 1;
            DragGhostText.Text = _draggingTag;
            UpdateDragGhostPosition(pos);

            // 切换到窗口级事件
            TagBar.PreviewMouseMove -= TagBar_PreviewMouseMove;
            this.PreviewMouseMove += Window_DragMove;
            this.PreviewMouseLeftButtonUp += Window_DragDrop;
        }
    }

    /// <summary>
    /// 窗口级拖拽移动：更新幻影 + 防抖交换
    /// </summary>
    private void Window_DragMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _draggingTag == null) return;

        var pos = e.GetPosition(this);

        // 更新幻影位置
        UpdateDragGhostPosition(pos);

        if (DataContext is not PopupViewModel vm) return;

        var fromIndex = vm.Tags.IndexOf(_draggingTag);
        if (fromIndex < 0) return;

        var tagBarPos = e.GetPosition(TagBar);

        // 防抖：距离上次交换点必须移动超过 10px（任意方向均可）
        if (!double.IsNaN(_lastSwapX))
        {
            var movedFromLast = Math.Abs(tagBarPos.X - _lastSwapX);
            if (movedFromLast < 10) return;
        }

        // 找到鼠标下方的标签（排除正在拖拽的标签）
        var hoveredIndex = GetHoveredChipIndex(tagBarPos, fromIndex);

        if (hoveredIndex >= 0 && hoveredIndex != fromIndex)
        {
            _lastSwapX = tagBarPos.X;

            // 执行交换（瞬间，无动画，避免闪烁）
            vm.Tags.Move(fromIndex, hoveredIndex);
            TagBar.UpdateLayout();
        }
    }

    /// <summary>
    /// 窗口级鼠标松开：完成拖拽
    /// </summary>
    private void Window_DragDrop(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        // 取消窗口级事件
        this.PreviewMouseMove -= Window_DragMove;
        this.PreviewMouseLeftButtonUp -= Window_DragDrop;
        TagBar.PreviewMouseMove += TagBar_PreviewMouseMove;

        // 幻影淡出
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) =>
        {
            DragGhostPopup.Visibility = Visibility.Collapsed;
            DragGhostBorder.Opacity = 1;
        };
        DragGhostBorder.BeginAnimation(OpacityProperty, fadeOut);

        // 恢复原始标签不透明度
        if (_draggingBorder != null)
            _draggingBorder.Opacity = 1;

        // 持久化排序
        if (DataContext is PopupViewModel vm)
        {
            vm.SetReorderingTags(false);
            vm.ReorderTags();
        }

        // 重置防抖状态
        _lastSwapX = double.NaN;

        _isDragging = false;
        _draggingTag = null;
        _draggingBorder = null;
    }

    // ===== 拖拽辅助方法 =====

    /// <summary>
    /// 获取鼠标下方的标签索引（排除指定索引）
    /// </summary>
    private int GetHoveredChipIndex(Point pos, int excludeIndex)
    {
        for (int i = 0; i < TagBar.Items.Count; i++)
        {
            if (i == excludeIndex) continue;

            var container = TagBar.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (container == null) continue;

            var border = FindVisualChild<Border>(container);
            if (border == null) continue;

            var bounds = border.TransformToVisual(TagBar)
                              .TransformBounds(new Rect(border.RenderSize));
            if (bounds.Contains(pos))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 更新拖拽幻影位置
    /// </summary>
    private void UpdateDragGhostPosition(Point windowPos)
    {
        var screenPos = PointToScreen(windowPos);
        DragGhostPopup.HorizontalOffset = screenPos.X + 12;
        DragGhostPopup.VerticalOffset = screenPos.Y + 12;
        DragGhostPopup.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 获取指定标签对应的 Border 控件
    /// </summary>
    private Border? GetBorderForTag(string tag)
    {
        for (int i = 0; i < TagBar.Items.Count; i++)
        {
            if (TagBar.Items[i] is string t && t == tag)
            {
                var container = TagBar.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                return container != null ? FindVisualChild<Border>(container) : null;
            }
        }
        return null;
    }

    /// <summary>
    /// 根据鼠标位置找到对应的标签名
    /// </summary>
    private string? GetTagAtPosition(Point pos)
    {
        for (int i = 0; i < TagBar.Items.Count; i++)
        {
            var container = TagBar.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (container == null) continue;

            var border = FindVisualChild<Border>(container);
            if (border == null) continue;

            var bounds = border.TransformToVisual(TagBar)
                              .TransformBounds(new Rect(border.RenderSize));
            if (bounds.Contains(pos))
                return TagBar.Items[i] as string;
        }
        return null;
    }

    /// <summary>
    /// 高亮当前选中的标签 chip（其他标签恢复正常样式）
    /// </summary>
    private void HighlightSelectedTag(string? selectedTag)
    {
        if (TagBar == null) return;

        var accentBrush = (Brush)FindResource("AccentBrush");
        var surfaceBrush = (Brush)FindResource("SurfaceBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");

        // 更新 “全部” chip 的高亮状态
        if (AllTagChip != null)
        {
            if (string.IsNullOrEmpty(selectedTag))
            {
                // “全部”被选中
                AllTagChip.Background = accentBrush;
                AllTagChip.BorderBrush = accentBrush;
            }
            else
            {
                // 某个标签被选中，“全部”恢复正常
                AllTagChip.Background = surfaceBrush;
                AllTagChip.BorderBrush = borderBrush;
            }
        }

        // 遍历标签栏中的所有 chip
        for (int i = 0; i < TagBar.Items.Count; i++)
        {
            var container = TagBar.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (container == null) continue;

            var chip = FindVisualChild<Border>(container);
            if (chip == null) continue;

            var chipTag = chip.Tag as string;
            if (chipTag == selectedTag)
            {
                // 选中态：AccentBrush 背景
                chip.Background = accentBrush;
                chip.BorderBrush = accentBrush;
            }
            else
            {
                // 正常态：SurfaceBrush 背景
                chip.Background = surfaceBrush;
                chip.BorderBrush = borderBrush;
            }
        }
    }

    /// <summary>
    /// 递归查找子元素
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// 请求设置分组标签 — 弹出输入对话框
    /// </summary>
    private void OnRequestSetTag(ClipboardItem item)
    {
        // 使用简单输入对话框
        string currentTag = item.TagsText;
        var dialog = new TagInputDialog(item.PreviewText ?? "", currentTag)
        {
            Owner = this,
            Topmost = true
        };

        // 不使用 ShowDialog 以避免阻塞，使用 Show + 激活保持
        dialog.ShowDialog();

        if (dialog.DialogResult == true && DataContext is PopupViewModel vm)
        {
            vm.ApplyTag(item, dialog.TagText);
        }
    }

    // ===== 卡片事件转发 =====

    private void Card_ItemSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not ClipboardItemCard card) return;
        if (card.DataContext is not ClipboardItem item) return;
        if (DataContext is not PopupViewModel vm) return;

        vm.SelectItemCommand.Execute(item);
    }

    private void Card_ItemDoubleClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ClipboardItemCard card) return;
        if (card.DataContext is not ClipboardItem item) return;
        if (DataContext is not PopupViewModel vm) return;

        vm.PasteItemCommand.Execute(item);
    }

    private void Card_PinToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ClipboardItemCard card) return;
        if (card.DataContext is not ClipboardItem item) return;
        if (DataContext is not PopupViewModel vm) return;

        vm.TogglePinCommand.Execute(item);
    }

    private void Card_DeleteRequested(object sender, RoutedEventArgs e)
    {
        if (sender is not ClipboardItemCard card) return;
        if (card.DataContext is not ClipboardItem item) return;
        if (DataContext is not PopupViewModel vm) return;

        vm.DeleteItemCommand.Execute(item);
    }

    private void Card_TagSetRequested(object sender, RoutedEventArgs e)
    {
        if (sender is not ClipboardItemCard card) return;
        if (card.DataContext is not ClipboardItem item) return;
        if (DataContext is not PopupViewModel vm) return;

        vm.SetTagCommand.Execute(item);
    }
}
