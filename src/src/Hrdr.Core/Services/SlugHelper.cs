using System.Text;
using System.Text.RegularExpressions;

namespace Hrdr.Core.Services;

public static partial class SlugHelper
{
    public static string FromName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        normalized = RemoveDiacritics(normalized);
        normalized = NonSlugChars().Replace(normalized, "-");
        normalized = MultiDash().Replace(normalized, "-").Trim('-');
        return string.IsNullOrEmpty(normalized) ? "project" : normalized;
    }

    private static string RemoveDiacritics(string text)
    {
        var formD = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultiDash();
}
