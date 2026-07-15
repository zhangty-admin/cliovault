using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClipVault.Models;
using ClipVault.Services;

namespace ClipVault.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private SettingsService? _settingsService;
    private ClipboardStore? _clipboardStore;
    private ImportExportService? _importExportService;

    [ObservableProperty]
    private bool _autoStartEnabled;

    [ObservableProperty]
    private string _hotkeyDisplay = string.Empty;

    /// <summary>
    /// 是否正在录制快捷键
    /// </summary>
    [ObservableProperty]
    private bool _isRecordingHotkey;

    [ObservableProperty]
    private bool _sensitiveFilterEnabled = true;

    [ObservableProperty]
    private CleanupPreview? _cleanupPreview;

    private HotkeyConfig _currentHotkey;

    /// <summary>
    /// 可选的保留周期列表（供下拉框绑定）
    /// </summary>
    public ObservableCollection<RetentionOption> RetentionOptions { get; } = new()
    {
        new(RetentionPeriod.ThreeDays,   "保留 3 天"),
        new(RetentionPeriod.SevenDays,   "保留 7 天"),
        new(RetentionPeriod.OneMonth,    "保留 1 个月"),
        new(RetentionPeriod.ThreeMonths, "保留 3 个月"),
        new(RetentionPeriod.Never,       "永不删除"),
    };

    private RetentionOption _selectedRetention = null!;
    public RetentionOption SelectedRetention
    {
        get => _selectedRetention;
        set
        {
            if (value == null || Equals(_selectedRetention, value))
                return;

            var preview = BuildCleanupPreview(value.Period);
            CleanupPreview = preview;

            if (preview.HasActiveCleanup)
            {
                var confirmed = ConfirmRetentionChange?.Invoke(preview) ?? true;
                if (!confirmed)
                {
                    OnPropertyChanged();
                    RefreshCleanupPreview();
                    return;
                }
            }

            if (SetProperty(ref _selectedRetention, value))
            {
                _settingsService?.UpdateRetentionPeriod(value.Period);
                RefreshCleanupPreview();
            }
        }
    }

    /// <summary>
    /// 无参构造（设计器/XAML 用）
    /// </summary>
    public SettingsViewModel()
    {
        _currentHotkey = HotkeyConfig.Default;
        // 直接赋值字段，避免触发 OnAutoStartEnabledChanged
        _autoStartEnabled = AutoStartService.IsEnabled();
        HotkeyDisplay = _currentHotkey.DisplayString;
        _selectedRetention = RetentionOptions[1]; // 默认 7 天
    }

    /// <summary>
    /// 注入 SettingsService 的构造
    /// </summary>
    public SettingsViewModel(SettingsService settingsService) : this()
    {
        Initialize(settingsService, null, null);
    }

    public SettingsViewModel(SettingsService settingsService, ClipboardStore clipboardStore) : this()
    {
        Initialize(settingsService, clipboardStore, null);
    }

    public SettingsViewModel(SettingsService settingsService, ClipboardStore clipboardStore, PersistenceService persistenceService) : this()
    {
        Initialize(settingsService, clipboardStore, new ImportExportService(persistenceService));
    }

    private void Initialize(SettingsService settingsService, ClipboardStore? clipboardStore, ImportExportService? importExportService)
    {
        _settingsService = settingsService;
        _clipboardStore = clipboardStore;
        _importExportService = importExportService;

        // 从磁盘加载当前设置
        var current = settingsService.Current.RetentionPeriod;
        _selectedRetention = RetentionOptions.FirstOrDefault(o => o.Period == current)
                             ?? RetentionOptions[1];
        OnPropertyChanged(nameof(SelectedRetention));

        // 从磁盘加载已保存的快捷键
        var s = settingsService.Current;
        _currentHotkey = new HotkeyConfig
        {
            Modifiers = s.HotkeyModifiers,
            VirtualKey = s.HotkeyVirtualKey,
            Id = 9001
        };
        HotkeyDisplay = _currentHotkey.DisplayString;
        SensitiveFilterEnabled = s.EnableSensitiveContentFilter;
        RefreshCleanupPreview();
    }

    partial void OnSensitiveFilterEnabledChanged(bool value)
    {
        _settingsService?.UpdateSensitiveContentFilter(value);
    }

    public void RefreshCleanupPreview()
    {
        CleanupPreview = BuildCleanupPreview(SelectedRetention.Period);
    }

    private CleanupPreview BuildCleanupPreview(RetentionPeriod period)
    {
        return _clipboardStore?.PreviewCleanup(period, DateTime.Now)
               ?? new CleanupPreview { Period = period };
    }

    partial void OnAutoStartEnabledChanged(bool value)
    {
        try
        {
            if (value)
                AutoStartService.Enable();
            else
                AutoStartService.Disable();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoStart failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 开始录制快捷键
    /// </summary>
    public void StartRecordingHotkey()
    {
        IsRecordingHotkey = true;
        HotkeyDisplay = "请按下快捷键...";
    }

    /// <summary>
    /// 取消录制快捷键
    /// </summary>
    public void CancelRecordingHotkey()
    {
        IsRecordingHotkey = false;
        HotkeyDisplay = _currentHotkey.DisplayString;
    }

    /// <summary>
    /// 应用新快捷键（从按键捕获结果）
    /// </summary>
    public bool ApplyNewHotkey(uint modifiers, uint virtualKey)
    {
        // 至少需要一个修饰键
        if ((modifiers & (Win32Modifiers.MOD_ALT | Win32Modifiers.MOD_CONTROL |
                          Win32Modifiers.MOD_SHIFT | Win32Modifiers.MOD_WIN)) == 0)
            return false;

        var config = new HotkeyConfig
        {
            Modifiers = modifiers,
            VirtualKey = virtualKey,
            Id = 9001
        };

        _currentHotkey = config;
        HotkeyDisplay = config.DisplayString;
        IsRecordingHotkey = false;

        // 持久化
        _settingsService?.UpdateHotkey(modifiers, virtualKey);

        // 通知 App 重新注册
        HotkeyChanged?.Invoke(config);
        return true;
    }

    /// <summary>
    /// 获取当前快捷键配置
    /// </summary>
    public HotkeyConfig CurrentHotkey => _currentHotkey;

    public void ExportData(string zipPath)
    {
        _clipboardStore?.Flush();
        _importExportService?.ExportTo(zipPath);
    }

    public ImportSummary PreviewImport(string zipPath)
    {
        return _importExportService?.Preview(zipPath) ?? default;
    }

    public void ImportData(string zipPath)
    {
        _importExportService?.ImportFrom(zipPath);
    }

    [RelayCommand]
    public void ExportData()
    {
        RequestExportData?.Invoke();
    }

    [RelayCommand]
    public void ImportData()
    {
        RequestImportData?.Invoke();
    }

    public event Action<HotkeyConfig>? HotkeyChanged;
    public event Func<CleanupPreview, bool>? ConfirmRetentionChange;
    public event Action? RequestExportData;
    public event Action? RequestImportData;
}

/// <summary>
/// 保留周期下拉选项
/// </summary>
public record RetentionOption(RetentionPeriod Period, string DisplayName);
