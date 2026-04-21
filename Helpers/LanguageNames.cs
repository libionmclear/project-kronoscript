namespace MyStoryTold.Helpers;

public static class LanguageNames
{
    public static readonly (string Code, string Name)[] Supported = new (string, string)[]
    {
        ("en", "English"),
        ("it", "Italian"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("pt", "Portuguese"),
        ("nl", "Dutch"),
        ("pl", "Polish"),
        ("ru", "Russian"),
        ("tr", "Turkish"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("zh-Hans", "Chinese (Simplified)")
    };

    public static string NameFor(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "English";
        foreach (var (c, n) in Supported)
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) return n;
        return code;
    }
}
