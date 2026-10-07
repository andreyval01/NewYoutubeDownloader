using System.Globalization;

namespace YoutubePlaylistDownloader;

static class AppLanguage
{
    public static string FromSystem()
    {
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name;
        var two = culture.TwoLetterISOLanguageName;

        if (name.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            return "Português (BR)";
        if (two == "nl")
            return "Dutch (NL)";
        if (two == "zh")
            return "中文";
        if (two == "he")
            return "עברית";
        if (two == "ar")
            return "العربية";

        var match = two switch
        {
            "ru" => "Русский",
            "en" => "English",
            "de" => "Deutsch",
            "es" => "Español",
            "fr" => "Français",
            "it" => "Italiano",
            "pl" => "Polski",
            "ro" => "Română",
            "tr" => "Türkçe",
            _ => null
        };
        return string.IsNullOrWhiteSpace(match) ? "English" : match;
    }
}
