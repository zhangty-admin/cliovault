using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipVault.Helpers;
using ClipVault.Models;
using ClipVault.Services;
using ClipVault.ViewModels;
using ClipVault.Views;
using H.NotifyIcon;

namespace ClipVault;

/// <summary>
/// 应用程序入口 — 协调所有服务的生命周期
/// </summary>
public partial class App : Application
{
    private const int HotkeyDebounceMs = 1500;

    // 核心服务
    private SingleInstanceService? _singleInstance;
    private MessageWindow? _messageWindow;
    private ClipboardMonitor? _clipboardMonitor;
    private ClipboardStore? _clipboardStore;
    private HotkeyService? _hotkeyService;
    private SettingsService? _settingsService;
    private CleanupService? _cleanupService;

    // ViewModel
    private PopupViewModel? _popupViewModel;
    private SettingsViewModel? _settingsViewModel;

    // 窗口
    private PopupWindow? _popupWindow;
    private SettingsWindow? _settingsWindow;

    // 托盘
    private TaskbarIcon? _taskbarIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常处理
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        MessageWindow.Log("===== App.OnStartup started =====");

        try
        {
            // 单实例检查
            _singleInstance = new SingleInstanceService();
            if (!_singleInstance.TryAcquire())
            {
                MessageWindow.Log("Another instance running, shutting down");
                Shutdown();
                return;
            }

            // 初始化核心服务
            _messageWindow = new MessageWindow();
            _messageWindow.Create();
            MessageWindow.Log($"MessageWindow created, HWND={_messageWindow.WindowHandle}");

            var persistence = new PersistenceService();
            _settingsService = new SettingsService();
            _clipboardStore = new ClipboardStore(maxCapacity: 500, persistence);

            _clipboardMonitor = new ClipboardMonitor(_messageWindow);
            _clipboardMonitor.ClipboardChanged += OnClipboardChanged;
            _clipboardMonitor.Start();
            MessageWindow.Log("ClipboardMonitor started");

            _hotkeyService = new HotkeyService(_messageWindow);
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _hotkeyService.RegistrationFailed += OnHotkeyRegistrationFailed;

            // 从设置加载快捷键（如果已保存则用保存的，否则用默认）
            var s = _settingsService.Current;
            var hotkeyConfig = new HotkeyConfig
            {
                Modifiers = s.HotkeyModifiers,
                VirtualKey = s.HotkeyVirtualKey,
                Id = 9001
            };
            var hotkeyRegistered = _hotkeyService.Register(hotkeyConfig);
            MessageWindow.Log($"Hotkey registered ({hotkeyConfig.DisplayString}): {hotkeyRegistered}");
            if (!hotkeyRegistered)
            {
                MessageBox.Show($"快捷键 {hotkeyConfig.DisplayString} 注册失败！\n可能被其他程序占用。",
                    "ClipVault 提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 初始化 ViewModel
            _popupViewModel = new PopupViewModel(_clipboardStore, _clipboardMonitor);
            _settingsViewModel = new SettingsViewModel(_settingsService);
            _settingsViewModel.HotkeyChanged += OnHotkeyChanged;

            // 初始化自动清理服务（启动时立即执行一次 + 每小时定时）
            _cleanupService = new CleanupService(_clipboardStore, _settingsService, persistence);
            _cleanupService.CleanupCompleted += OnCleanupCompleted;
            _cleanupService.Start();
            MessageWindow.Log("CleanupService started");

            // 初始化窗口
            _popupWindow = new PopupWindow
            {
                DataContext = _popupViewModel
            };
            MessageWindow.Log("PopupWindow created");

            // 初始化系统托盘
            SetupTrayIcon();

            MessageWindow.Log("===== App.OnStartup complete =====");
        }
        catch (Exception ex)
        {
            MessageWindow.Log($"FATAL: {ex}");
            MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.StackTrace}",
                "ClipVault 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// 剪贴板内容变化 — 存入 Store
    /// </summary>
    private void OnClipboardChanged(ClipboardItem item)
    {
        Dispatcher.Invoke(() =>
        {
            _clipboardStore?.Add(item);
        });
    }

    private DateTime _lastHotkeyTime = DateTime.MinValue;

    /// <summary>
    /// 快捷键按下 — 切换面板显隐（带 1500ms 防抖，防止按键弹起时重复触发）
    /// </summary>
    private void OnHotkeyPressed()
    {
        var now = DateTime.Now;
        var elapsed = (now - _lastHotkeyTime).TotalMilliseconds;
        MessageWindow.Log($"Hotkey pressed! (elapsed since last = {elapsed:F0}ms)");

        if (elapsed < HotkeyDebounceMs)
        {
            MessageWindow.Log("  Ignored: debounce");
            return;
        }
        _lastHotkeyTime = now;

        Dispatcher.Invoke(() =>
        {
            if (_popupWindow == null)
            {
                MessageWindow.Log("  _popupWindow is null!");
                return;
            }

            MessageWindow.Log($"  IsVisible={_popupWindow.IsVisible}, _isClosing=N/A, Opacity={_popupWindow.Opacity}");

            if (_popupWindow.IsVisible)
            {
                MessageWindow.Log("  → HideWithAnimation");
                _popupWindow.HideWithAnimation();
            }
            else
            {
                MessageWindow.Log("  → ShowWithAnimation");
                _popupWindow.ShowWithAnimation();
            }
        });
    }

    /// <summary>
    /// 快捷键注册失败
    /// </summary>
    private void OnHotkeyRegistrationFailed(string message)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(message, "快捷键注册失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    /// <summary>
    /// 用户在设置中修改了快捷键 — 重新注册
    /// </summary>
    private void OnHotkeyChanged(HotkeyConfig config)
    {
        Dispatcher.Invoke(() =>
        {
            var success = _hotkeyService?.Register(config) ?? false;
            MessageWindow.Log($"Hotkey re-registered ({config.DisplayString}): {success}");
            if (!success)
            {
                MessageBox.Show($"快捷键 {config.DisplayString} 注册失败！\n可能被其他程序占用，请尝试其他组合。",
                    "ClipVault 提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    /// <summary>
    /// 设置系统托盘图标和菜单
    /// </summary>
    private void SetupTrayIcon()
    {
        try
        {
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "ClipVault - 剪贴板管理工具",
                IconSource = new GeneratedIconSource
                {
                    Text = "📋",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0x6E, 0xF5)),
                    FontSize = 38,
                    FontWeight = FontWeights.Bold
                }
            };

            // 右键菜单
            var menu = new ContextMenu();
            menu.Style = (Style)FindResource("DarkContextMenuStyle");

            var showItem = new MenuItem { Header = "显示剪贴板面板" };
            showItem.Click += (_, _) => OnHotkeyPressed();
            menu.Items.Add(showItem);

            menu.Items.Add(new Separator());

            var autoStartItem = new MenuItem
            {
                Header = "开机自启动",
                IsCheckable = true,
                IsChecked = AutoStartService.IsEnabled()
            };
            autoStartItem.Click += (_, _) =>
            {
                if (autoStartItem.IsChecked)
                    AutoStartService.Enable();
                else
                    AutoStartService.Disable();
            };
            menu.Items.Add(autoStartItem);

            var settingsItem = new MenuItem { Header = "设置..." };
            settingsItem.Click += (_, _) => ShowSettings();
            menu.Items.Add(settingsItem);

            var aboutItem = new MenuItem { Header = "关于 ClipVault" };
            aboutItem.Click += (_, _) =>
            {
                MessageBox.Show("ClipVault v1.0.0\nWindows 剪贴板管理工具\n\n快捷键: Ctrl+Shift+V",
                    "关于", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            menu.Items.Add(aboutItem);

            menu.Items.Add(new Separator());

            var exitItem = new MenuItem { Header = "退出" };
            exitItem.Click += (_, _) => ShutdownApp();
            menu.Items.Add(exitItem);

            _taskbarIcon.ContextMenu = menu;

            // 单击托盘图标弹出面板
            _taskbarIcon.TrayLeftMouseUp += (_, _) => OnHotkeyPressed();

            // 关键！在代码中创建 TaskbarIcon 必须调用 ForceCreate 才能显示
            _taskbarIcon.ForceCreate();

            MessageWindow.Log($"TrayIcon ForceCreate done. IsCreated={_taskbarIcon.IsCreated}");
        }
        catch (Exception ex)
        {
            MessageWindow.Log($"TrayIcon creation failed: {ex}");
        }
    }

    /// <summary>
    /// 显示设置窗口
    /// </summary>
    private void ShowSettings()
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settingsViewModel!);
        _settingsWindow.Show();
    }

    /// <summary>
    /// 自动清理完成回调
    /// </summary>
    private void OnCleanupCompleted(int removedItems, int removedImages)
    {
        MessageWindow.Log($"Cleanup: removed {removedItems} items, {removedImages} orphaned images");
    }

    /// <summary>
    /// 关闭应用
    /// </summary>
    private void ShutdownApp()
    {
        _cleanupService?.Stop();
        _clipboardStore?.Flush();  // 确保退出前保存到磁盘
        _hotkeyService?.Unregister();
        _clipboardMonitor?.Dispose();
        _messageWindow?.Dispose();
        _taskbarIcon?.Dispose();
        _singleInstance?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShutdownApp();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageWindow.Log($"DispatcherUnhandledException: {e.Exception}");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        MessageWindow.Log($"CurrentDomain_UnhandledException: {e.ExceptionObject}");
    }
}
