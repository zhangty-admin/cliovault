using System.Windows;
using System.Windows.Input;
using ClipVault.Models;

namespace ClipVault.Views;

/// <summary>
/// 编辑剪贴板文本内容的对话框
/// </summary>
public partial class EditContentDialog : Window
{
    /// <summary>
    /// 编辑后的文本（对话框关闭后读取）
    /// </summary>
    public string EditedText { get; private set; } = string.Empty;

    public EditContentDialog(ClipboardItem item)
    {
        InitializeComponent();

        // 加载当前内容到编辑器
        ContentEditor.Text = item.Text ?? string.Empty;
        EditedText = ContentEditor.Text;

        // 加载完成后聚焦并全选
        ContentEditor.Loaded += (_, _) =>
        {
            ContentEditor.Focus();
            ContentEditor.SelectAll();
        };
    }

    /// <summary>
    /// Ctrl+Enter 保存，Esc 取消
    /// </summary>
    private void ContentEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            Close();
        }
    }

    private void Save_Click(object sender, MouseButtonEventArgs e)
    {
        CommitSave();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CommitSave()
    {
        EditedText = ContentEditor.Text;
        DialogResult = true;
        Close();
    }
}
