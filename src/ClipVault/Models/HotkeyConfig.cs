namespace ClipVault.Models;

/// <summary>
/// 快捷键配置模型
/// </summary>
public class HotkeyConfig
{
    public uint Modifiers { get; set; }

    public uint VirtualKey { get; set; }

    public int Id { get; set; }

    /// <summary>
    /// 默认配置：Ctrl+Shift+V
    /// </summary>
    public static HotkeyConfig Default => new()
    {
        Modifiers = Win32Modifiers.MOD_CONTROL | Win32Modifiers.MOD_SHIFT,
        VirtualKey = 0x56, // 'V'
        Id = 9001
    };

    /// <summary>
    /// 用户可读的快捷键描述
    /// </summary>
    public string DisplayString
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & Win32Modifiers.MOD_WIN) != 0) parts.Add("Win");
            if ((Modifiers & Win32Modifiers.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & Win32Modifiers.MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & Win32Modifiers.MOD_SHIFT) != 0) parts.Add("Shift");
            parts.Add(GetKeyName(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    private static string GetKeyName(uint vk)
    {
        // 常用虚拟键码映射
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
            >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A-Z
            >= 0x70 and <= 0x7B => $"F{vk - 0x6F}", // F1-F12
            _ => $"Key(0x{vk:X})"
        };
    }
}

/// <summary>
/// Win32 修饰键常量
/// </summary>
public static class Win32Modifiers
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
}
