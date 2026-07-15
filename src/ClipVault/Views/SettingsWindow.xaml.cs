using System.Windows;
using System.Windows.Input;
using ClipVault.Models;
using ClipVault.ViewModels;
using Microsoft.Win32;

namespace ClipVault.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ConfirmRetentionChange += ConfirmRetentionChange;
        _viewModel.RequestExportData += ExportData;
        _viewModel.RequestImportData += ImportData;
        Closed += SettingsWindow_Closed;

        // 录制模式下捕获键盘
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ConfirmRetentionChange -= ConfirmRetentionChange;
            _viewModel.RequestExportData -= ExportData;
            _viewModel.RequestImportData -= ImportData;
        }
    }

    private bool ConfirmRetentionChange(CleanupPreview preview)
    {
        var message = $"切换后将立即把 {preview.ExpiredUntaggedCount} 条未置顶、未分组的历史记录移入回收站。\n\n" +
                      "置顶和分组记录不会被自动清理。是否继续？";
        return MessageBox.Show(message, "确认自动清理设置", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void ExportData()
    {
        if (_viewModel == null) return;

        var dialog = new SaveFileDialog
        {
            Title = "导出 ClipVault 数据",
            Filter = "ClipVault 备份 (*.zip)|*.zip",
            FileName = $"ClipVault-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _viewModel.ExportData(dialog.FileName);
            MessageBox.Show("导出完成。", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportData()
    {
        if (_viewModel == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "导入 ClipVault 数据",
            Filter = "ClipVault 备份 (*.zip)|*.zip"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var summary = _viewModel.PreviewImport(dialog.FileName);
            var message = $"备份内容：历史文件={BoolText(summary.HasHistory)}，设置={BoolText(summary.HasSettings)}，图片={summary.ImageCount} 张。\n\n" +
                          "导入会覆盖当前本地数据，导入前会自动备份当前数据。导入后请重启 ClipVault 让数据重新加载。是否继续？";
            if (MessageBox.Show(message, "确认导入", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _viewModel.ImportData(dialog.FileName);
            MessageBox.Show("导入完成，请退出并重新启动 ClipVault。", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string BoolText(bool value) => value ? "有" : "无";

    private void ChangeHotkeyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (_viewModel.IsRecordingHotkey)
        {
            // 已在录制中，点击相当于取消
            _viewModel.CancelRecordingHotkey();
            ChangeHotkeyBtn.Content = "修改";
            HotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        }
        else
        {
            _viewModel.StartRecordingHotkey();
            ChangeHotkeyBtn.Content = "取消";
            HotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
            HotkeyBorder.Focus();
        }
    }

    private void ResetHotkeyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // 如果正在录制，先取消
        if (_viewModel.IsRecordingHotkey)
        {
            _viewModel.CancelRecordingHotkey();
            ChangeHotkeyBtn.Content = "修改";
            HotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        }

        // 重置为默认 Ctrl+Shift+V
        var def = HotkeyConfig.Default;
        _viewModel.ApplyNewHotkey(def.Modifiers, def.VirtualKey);
    }

    private void HotkeyBorder_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsRecordingHotkey)
        {
            HotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        }
    }

    private void HotkeyBorder_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsRecordingHotkey)
        {
            // 失去焦点时取消录制
            _viewModel.CancelRecordingHotkey();
            ChangeHotkeyBtn.Content = "修改";
            HotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        }
    }

    private void HotkeyBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 点击 Border 获取焦点
        HotkeyBorder.Focus();
    }

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null || !_viewModel.IsRecordingHotkey)
            return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 忽略单独的修饰键
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin)
            return;

        // Esc 取消录制
        if (key == Key.Escape)
        {
            _viewModel.CancelRecordingHotkey();
            ChangeHotkeyBtn.Content = "修改";
            HotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
            return;
        }

        // 收集修饰键
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Win32Modifiers.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   mods |= Win32Modifiers.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))     mods |= Win32Modifiers.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Win32Modifiers.MOD_WIN;

        // 转换 Key → Win32 VK
        var vk = KeyToVirtualKey(key);

        if (vk != 0 && mods != 0)
        {
            var success = _viewModel.ApplyNewHotkey(mods, vk);
            if (success)
            {
                ChangeHotkeyBtn.Content = "修改";
                HotkeyBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
            }
            else
            {
                // 没有修饰键，提示
                _viewModel.HotkeyDisplay = "需要至少一个修饰键 (Ctrl/Alt/Shift/Win)";
            }
        }
    }

    /// <summary>
    /// WPF Key → Win32 Virtual Key Code
    /// </summary>
    private static uint KeyToVirtualKey(Key key)
    {
        // KeyInterop.VirtualKeyFromKey 返回 Win32 VK
        return (uint)KeyInterop.VirtualKeyFromKey(key);
    }
}
