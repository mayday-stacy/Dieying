using System.Collections;
using System.Globalization;
using System.Resources;

namespace Composa.App;

/// <summary>
/// Display text only. English keys remain stable in commands, settings and document data; formatting
/// continues to use the person's numeric culture. The application selects the language at startup.
/// </summary>
public static class L10n
{
    private static string language = "en";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> chinese = new(ReadChinese);

    /// <summary>The resolved display language, either en or zh-CN.</summary>
    public static string CurrentLanguage => language;

    /// <summary>Select English, Simplified Chinese, or the system UI language. Unknown settings follow the system.</summary>
    public static void SetLanguage(string? value)
    {
        language = value?.ToLowerInvariant() switch
        {
            "en" => "en",
            "zh-cn" => "zh-CN",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en"
        };
    }

    /// <summary>Translate known interface text; unknown text is returned unchanged. Do not pass user data.</summary>
    public static string Text(string english) =>
        language == "zh-CN" && chinese.Value.TryGetValue(english, out var translated) ? translated : english;

    /// <summary>Translate a template before inserting data, leaving paths, names and numeric culture intact.</summary>
    public static string Format(string englishFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Text(englishFormat), args);

    private static IReadOnlyDictionary<string, string> ReadChinese()
    {
        // ResX compilation treats names case-insensitively. Match that behavior for labels such as
        // "Text color" (tooltip) and "Text Color" (dialog), instead of silently leaving one untranslated.
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(L10n).Assembly;
        // Separate catalogs allow the shell, dialogs and other panels to be maintained independently.
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith("Composa.App.Localization.", StringComparison.Ordinal) && n.EndsWith(".resources", StringComparison.Ordinal))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
                if (entry.Key is string key && entry.Value is string value) result.TryAdd(key, value);
        }
        return result;
    }
}
