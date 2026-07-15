using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipVault.Models;

namespace ClipVault.Helpers;

/// <summary>
/// 检测当前剪贴板内容类型，并提取为 ClipboardItem
/// </summary>
public static class ClipboardFormatDetector
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tiff", ".tif", ".webp"
    };

    /// <summary>
    /// 判断文件是否为图片文件
    /// </summary>
    public static bool IsImageFile(string path)
    {
        return ImageExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// 从系统剪贴板读取内容并构建 ClipboardItem
    /// </summary>
    public static ClipboardItem? ReadFromClipboard()
    {
        try
        {
            // 优先检测文件列表
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    var fileList = files.Cast<string>().ToList().AsReadOnly();

                    // 如果复制的全部是图片文件，加载缩略图用于预览
                    BitmapSource? thumbnail = null;
                    if (fileList.Count == 1 && IsImageFile(fileList[0]))
                    {
                        thumbnail = LoadImageThumbnail(fileList[0]);
                    }

                    return new ClipboardItem
                    {
                        Type = ClipboardItemType.Files,
                        FilePaths = fileList,
                        Image = thumbnail  // 非 null 时卡片显示缩略图
                    };
                }
            }

            // 检测图片
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    image = NormalizeClipboardImage(image);
                    return new ClipboardItem
                    {
                        Type = ClipboardItemType.Image,
                        Image = image
                    };
                }
            }

            // ===== 文本类内容 =====
            // 优先使用 UnicodeText（CF_UNICODETEXT），保证 CJK/Emoji/特殊符号无损
            // Text（CF_TEXT = ANSI）会导致中文 Unicode→ANSI→Unicode 有损往返
            string? plainText = null;
            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                plainText = Clipboard.GetText(TextDataFormat.UnicodeText);
            }
            if (string.IsNullOrEmpty(plainText) && Clipboard.ContainsText(TextDataFormat.Text))
            {
                plainText = Clipboard.GetText(TextDataFormat.Text);
            }

            // 判断格式类型
            bool hasRtf = Clipboard.ContainsText(TextDataFormat.Rtf);
            bool hasHtml = Clipboard.ContainsText(TextDataFormat.Html);
            bool hasPlainText = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                             || Clipboard.ContainsText(TextDataFormat.Text);

            // 优先级: 普通文本 > RTF > HTML
            // 如果只有纯文本（没有 RTF/HTML），则标记为 Text
            if (hasPlainText && !hasRtf && !hasHtml)
            {
                if (!string.IsNullOrEmpty(plainText))
                {
                    return new ClipboardItem
                    {
                        Type = ClipboardItemType.Text,
                        Text = plainText
                    };
                }
            }

            // 如果有 RTF 格式（如从 IDE 复制代码），用纯文本显示
            if (hasRtf)
            {
                if (!string.IsNullOrEmpty(plainText))
                {
                    // 尝试提取图片预览（剪贴板可能同时有 Image 格式）
                    BitmapSource? previewImage = TryGetClipboardImage();

                    return new ClipboardItem
                    {
                        Type = ClipboardItemType.Rtf,
                        Text = plainText,
                        Image = previewImage  // 非 null 时卡片显示图片预览
                    };
                }
            }

            // 如果有 HTML 格式，用纯文本显示
            if (hasHtml)
            {
                if (!string.IsNullOrEmpty(plainText))
                {
                    BitmapSource? previewImage = TryGetClipboardImage();

                    return new ClipboardItem
                    {
                        Type = ClipboardItemType.Html,
                        Text = plainText,
                        Image = previewImage
                    };
                }
            }

            // 回退：尝试任意文本
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    return new ClipboardItem
                    {
                        Type = ClipboardItemType.Text,
                        Text = text
                    };
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取剪贴板失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 加载图片文件的缩略图（用于卡片预览）
    /// </summary>
    private static BitmapSource? LoadImageThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 200;  // 缩略图宽度，节省内存
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();  // 跨线程安全
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 尝试从剪贴板提取图片（用于 RTF/HTML 类型的图片预览）
    /// </summary>
    private static BitmapSource? TryGetClipboardImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return null;
            var image = Clipboard.GetImage();
            if (image != null)
            {
                return NormalizeClipboardImage(image);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 将剪贴板位图转换为 WPF 可稳定渲染的 BGRA32。
    /// 某些程序写入的 CF_DIB 会携带全 0 Alpha；RGB 数据有效，但 WPF 会将其显示为全透明。
    /// 仅在整张图片的 Alpha 都为 0 时将其修正为不透明，正常的透明图片保持原 Alpha。
    /// </summary>
    internal static BitmapSource NormalizeClipboardImage(BitmapSource source)
    {
        BitmapSource bgraSource = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var stride = checked(bgraSource.PixelWidth * 4);
        var pixels = new byte[checked(stride * bgraSource.PixelHeight)];
        bgraSource.CopyPixels(pixels, stride, 0);

        var hasVisiblePixel = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasVisiblePixel = true;
                break;
            }
        }

        if (!hasVisiblePixel)
        {
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = byte.MaxValue;

            MessageWindow.Log($"[ClipboardImage] repaired zero alpha: {source.PixelWidth}x{source.PixelHeight}, format={source.Format}");
        }

        var normalized = BitmapSource.Create(
            bgraSource.PixelWidth,
            bgraSource.PixelHeight,
            bgraSource.DpiX > 0 ? bgraSource.DpiX : 96,
            bgraSource.DpiY > 0 ? bgraSource.DpiY : 96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        normalized.Freeze();
        return normalized;
    }
}
