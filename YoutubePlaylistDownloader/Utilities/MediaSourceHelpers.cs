namespace YoutubePlaylistDownloader.Utilities;

internal static partial class MediaSourceHelpers
{
    public static bool TryGetExternalSource(string text, out MediaSourceKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = NormalizeUrl(text).TrimEnd();
        if (RutubeUrlRegex().IsMatch(value))
        {
            kind = MediaSourceKind.Rutube;
            return true;
        }

        if (VkVideoUrlRegex().IsMatch(value))
        {
            kind = MediaSourceKind.VkVideo;
            return true;
        }

        return false;
    }

    public static bool IsExternalUrl(string text) => TryGetExternalSource(text, out _);

    public static bool IsPlaylistCatalog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return RutubePlaylistCatalogRegex().IsMatch(NormalizeUrl(text).TrimEnd());
    }

    public static string NormalizeUrl(string text)
    {
        var value = text.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        return value;
    }

    public static string NormalizeThumbnailUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";
        if (url.StartsWith("//", StringComparison.Ordinal))
            return "https:" + url;
        return url;
    }

    [GeneratedRegex(
        @"^https?://(?:www\.)?rutube\.ru/(?:video(?:/private)?/[0-9a-f]{32}|play/embed/[0-9a-f]{32}|plst/\d+|(?:channel|person|u)/[\w\-]+)(?:[/?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RutubeUrlRegex();

    [GeneratedRegex(
        @"^https?://(?:www\.)?rutube\.ru/(?:channel|person|u)/[\w\-]+/playlists/?(?:[?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RutubePlaylistCatalogRegex();

    [GeneratedRegex(
        @"^https?://(?:(?:www|m)\.)?(?:vkvideo\.ru/(?:video-?\d+_\d+|clip-?\d+_\d+|playlist/-?\d+_\d+|@[\w.\-]+)|vk\.(?:com|ru)/(?:(?:video|clip)-?\d+_\d+|video/@[\w.\-]+|video/playlist/-?\d+_\d+|videos-?\d+|video(?:ext)?\.php|(?:[^?\s]+)?\?(?:[^#\s]*&)?z=(?:video|clip)-?\d+_\d+))(?:[/?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VkVideoUrlRegex();
}
