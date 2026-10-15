internal static class UiStrings
{
    public static string Lang { get; private set; } = "en";

    public static void Init(string uiLanguage)
    {
        var key = (uiLanguage ?? "auto").Trim().ToLowerInvariant();
        if (key is "en" or "zh")
        {
            Lang = key;
            return;
        }
        // auto
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Lang = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
    }

    public static string T(string en, string zh) => Lang == "zh" ? zh : en;
}