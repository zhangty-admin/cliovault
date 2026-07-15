using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipVault.Views;

/// <summary>
/// 分组标签输入对话框 — 简洁轻量
/// </summary>
public class TagInputDialog : Window
{
    private readonly TextBox _inputBox;
    public string TagText => _inputBox.Text.Trim();

    public TagInputDialog(string previewText, string currentTag)
    {
        Title = "设置分组";
        Width = 360;
        Height = 180;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        var outerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5C)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20)
        };

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        // 预览文本
        var previewBlock = new TextBlock
        {
            Text = previewText.Length > 50 ? previewText[..50] + "..." : previewText,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xCC)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.7
        };
        panel.Children.Add(previewBlock);

        // 提示文字
        var hintBlock = new TextBlock
        {
            Text = "输入分组名称，多个用逗号分隔：",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(hintBlock);

        // 输入框
        _inputBox = new TextBox
        {
            Text = currentTag,
            FontSize = 14,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x40)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x70)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        _inputBox.KeyDown += InputBox_KeyDown;
        panel.Children.Add(_inputBox);

        // 按钮区
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var clearBtn = CreateButton("清除标签", false);
        clearBtn.Click += (_, _) =>
        {
            _inputBox.Text = "";
            DialogResult = true;
            Close();
        };
        btnPanel.Children.Add(clearBtn);

        var cancelBtn = CreateButton("取消", false);
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        cancelBtn.Margin = new Thickness(8, 0, 0, 0);
        btnPanel.Children.Add(cancelBtn);

        var okBtn = CreateButton("确定", true);
        okBtn.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        okBtn.Margin = new Thickness(8, 0, 0, 0);
        btnPanel.Children.Add(okBtn);

        panel.Children.Add(btnPanel);

        outerBorder.Child = panel;
        Content = outerBorder;

        Loaded += (_, _) =>
        {
            _inputBox.Focus();
            _inputBox.SelectAll();
        };
    }

    private Button CreateButton(string text, bool isPrimary)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(14, 5, 14, 5),
            Cursor = Cursors.Hand,
            Background = isPrimary
                ? new SolidColorBrush(Color.FromRgb(0x5B, 0x7C, 0xFA))
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x40)),
            Foreground = isPrimary
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xCC)),
            BorderThickness = new Thickness(0)
        };
        return btn;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            Close();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
