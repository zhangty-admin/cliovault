using System.Text.RegularExpressions;
using ClipVault.Models;

namespace ClipVault.Helpers;

public static partial class SensitiveContentFilter
{
    public static bool ShouldSkip(ClipboardItem item)
    {
        if (item.Type is not (ClipboardItemType.Text or ClipboardItemType.Rtf or ClipboardItemType.Html))
            return false;

        var text = item.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return LooksLikeSecretAssignment(text)
            || PrivateKeyRegex().IsMatch(text)
            || TokenRegex().IsMatch(text)
            || IdCardRegex().IsMatch(text)
            || BankCardRegex().IsMatch(text);
    }

    private static bool LooksLikeSecretAssignment(string text)
    {
        if (text.Length > 512)
            return false;

        return SecretAssignmentRegex().IsMatch(text);
    }

    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|private[_-]?key)\b\s*[:=]\s*['""]?[^'""\s]{6,}")]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"\b(?:sk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9]{20,}|xox[baprs]-[A-Za-z0-9-]{20,})\b")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\b\d{17}[\dXx]\b")]
    private static partial Regex IdCardRegex();

    [GeneratedRegex(@"\b(?:\d[ -]?){15,19}\b")]
    private static partial Regex BankCardRegex();
}
