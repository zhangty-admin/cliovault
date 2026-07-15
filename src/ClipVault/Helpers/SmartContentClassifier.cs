using System.Text.Json;
using System.Text.RegularExpressions;
using ClipVault.Models;

namespace ClipVault.Helpers;

public static partial class SmartContentClassifier
{
    public static void Apply(ClipboardItem item)
    {
        item.SmartType = Detect(item);
    }

    public static string? Detect(ClipboardItem item)
    {
        if (item.Type == ClipboardItemType.Files)
            return item.FilePaths?.Count == 1 ? "文件" : "多文件";
        if (item.Type == ClipboardItemType.Image)
            return "图片";

        var text = item.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (UrlRegex().IsMatch(text)) return "URL";
        if (EmailRegex().IsMatch(text)) return "Email";
        if (PhoneRegex().IsMatch(text)) return "手机号";
        if (ColorRegex().IsMatch(text)) return "颜色";
        if (LooksLikeJson(text)) return "JSON";
        if (SqlRegex().IsMatch(text)) return "SQL";
        if (CommandRegex().IsMatch(text)) return "命令";
        if (CodeRegex().IsMatch(text)) return "代码";

        return null;
    }

    private static bool LooksLikeJson(string text)
    {
        if (!(text.StartsWith('{') && text.EndsWith('}')) &&
            !(text.StartsWith('[') && text.EndsWith(']')))
            return false;

        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^(?:\+?86[- ]?)?1[3-9]\d{9}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"\b(select|insert|update|delete|create|alter|drop)\b.+\b(from|where|table|into|set)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SqlRegex();

    [GeneratedRegex(@"^\s*(git|npm|pnpm|yarn|dotnet|mvn|gradle|docker|kubectl|ssh|scp|curl|wget|python|node|java)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CommandRegex();

    [GeneratedRegex(@"(\b(class|public|private|function|const|let|var|return|using|namespace|import|export)\b|[{};]\s*$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CodeRegex();
}
