using System.Runtime.InteropServices;
using ClipVault.Helpers.Win32;

namespace ClipVault.Helpers;

/// <summary>
/// 通过 Unicode 键盘输入直接键入文本，用于绕过浏览器远程桌面的异步剪贴板桥接。
/// </summary>
internal static class DirectTextInput
{
    public static bool TrySend(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = CreateKeyboardInput(text[i], Win32Constants.KEYEVENTF_UNICODE);
            inputs[i * 2 + 1] = CreateKeyboardInput(
                text[i], Win32Constants.KEYEVENTF_UNICODE | Win32Constants.KEYEVENTF_KEYUP);
        }

        return User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static INPUT CreateKeyboardInput(char character, uint flags)
    {
        return new INPUT
        {
            type = Win32Constants.INPUT_KEYBOARD,
            data = new INPUTUNION
            {
                keyboardInput = new KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = flags
                }
            }
        };
    }
}
