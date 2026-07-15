using System.IO;
using System.Text.Json;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>
/// 设置服务 — 读写 settings.json，支持运行时变更通知
/// </summary>
public class SettingsService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipVault");

    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private AppSettings _current;

    /// <summary>
    /// 当前设置（只读快照）
    /// </summary>
    public AppSettings Current => _current;

    /// <summary>
    /// 设置变更事件
    /// </summary>
    public event Action<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        _current = Load();
    }

    /// <summary>
    /// 从磁盘加载设置
    /// </summary>
    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                    return settings;
            }
        }
        catch { }

        return new AppSettings(); // 默认值
    }

    /// <summary>
    /// 更新保留周期并持久化
    /// </summary>
    public void UpdateRetentionPeriod(RetentionPeriod period)
    {
        if (_current.RetentionPeriod == period)
            return;

        _current.RetentionPeriod = period;
        Save();
        SettingsChanged?.Invoke(_current);
    }

    /// <summary>
    /// 更新快捷键并持久化
    /// </summary>
    public void UpdateHotkey(uint modifiers, uint virtualKey)
    {
        _current.HotkeyModifiers = modifiers;
        _current.HotkeyVirtualKey = virtualKey;
        Save();
        SettingsChanged?.Invoke(_current);
    }

    public void UpdateSensitiveContentFilter(bool enabled)
    {
        if (_current.EnableSensitiveContentFilter == enabled)
            return;

        _current.EnableSensitiveContentFilter = enabled;
        Save();
        SettingsChanged?.Invoke(_current);
    }

    /// <summary>
    /// 保存到磁盘
    /// </summary>
    private void Save()
    {
        try
        {
            if (!Directory.Exists(DataDir))
                Directory.CreateDirectory(DataDir);

            var json = JsonSerializer.Serialize(_current, JsonOptions);

            // 写临时文件再替换，防止写入中途损坏
            var tempFile = SettingsFile + ".tmp";
            File.WriteAllText(tempFile, json);

            if (File.Exists(SettingsFile))
                File.Delete(SettingsFile);
            File.Move(tempFile, SettingsFile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置保存失败: {ex.Message}");
        }
    }
}
