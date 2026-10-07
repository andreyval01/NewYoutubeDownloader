using System.Net;
using Newtonsoft.Json.Linq;

namespace YoutubePlaylistDownloader.Utilities;

internal static partial class PlaylistLinkScanner
{
    public sealed class FoundPlaylist
    {
        public string Url { get; init; }
        public string Title { get; init; }
    }

    public sealed class ScanResult
    {
        public string PageTitle { get; init; }
        public string Thumbnail { get; init; }
        public IReadOnlyList<FoundPlaylist> Playlists { get; init; } = [];
    }

    public static async Task<IReadOnlyList<FoundPlaylist>> FindAsync(string pageUrl, CancellationToken token)
        => (await ScanAsync(pageUrl, token).ConfigureAwait(false)).Playlists;

    public static async Task<ScanResult> ScanAsync(string pageUrl, CancellationToken token)
    {
        var source = MediaSourceHelpers.NormalizeUrl(pageUrl);
        var results = new Dictionary<string, FoundPlaylist>(StringComparer.OrdinalIgnoreCase);
        var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageTitle = "";
        var thumbnail = "";

        if (TryGetRutubeChannelId(source, out var sourceChannelId))
            channelIds.Add(sourceChannelId);

        if (TryGetRutubePlaylistId(source, out var playlistId))
        {
            var userId = await TryGetRutubePlaylistOwnerAsync(playlistId, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(userId))
                channelIds.Add(userId);
        }

        foreach (var channelId in channelIds.ToList())
        {
            token.ThrowIfCancellationRequested();
            var api = await ScanRutubeUserPlaylistsAsync(channelId, results, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageTitle) && !string.IsNullOrWhiteSpace(api.Title))
                pageTitle = api.Title;
            if (string.IsNullOrWhiteSpace(thumbnail) && !string.IsNullOrWhiteSpace(api.Thumbnail))
                thumbnail = api.Thumbnail;
        }

        var firstHtml = await ScanPageAsync(source, results, pages, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(pageTitle) && !string.IsNullOrWhiteSpace(firstHtml))
            pageTitle = ExtractChannelName(firstHtml, sourceChannelId);

        foreach (var channelId in channelIds.ToList())
        {
            token.ThrowIfCancellationRequested();
            var html = await ScanPageAsync($"https://rutube.ru/channel/{channelId}/playlists/", results, pages, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pageTitle) && !string.IsNullOrWhiteSpace(html))
                pageTitle = ExtractChannelName(html, channelId);
        }

        var currentKey = CanonicalKey(source);
        if (!string.IsNullOrWhiteSpace(currentKey))
            results.Remove(currentKey);

        return new ScanResult
        {
            PageTitle = pageTitle,
            Thumbnail = thumbnail,
            Playlists = results.Values.ToList()
        };
    }

    private static async Task<string> ScanPageAsync(
        string url,
        Dictionary<string, FoundPlaylist> results,
        HashSet<string> pages,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(url) || !pages.Add(url))
            return "";

        string html;
        try
        {
            html = await DownloadStringAsync(url, token).ConfigureAwait(false);
        }
        catch
        {
            return "";
        }

        if (string.IsNullOrWhiteSpace(html))
            return "";

        CollectFromHtml(html, results);
        return html;
    }

    private static async Task<(string Title, string Thumbnail)> ScanRutubeUserPlaylistsAsync(
        string userId,
        Dictionary<string, FoundPlaylist> results,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ("", "");

        var title = "";
        var thumbnail = "";
        var url = $"https://rutube.ru/api/playlist/user/{userId}/";
        for (var page = 0; page < 20 && !string.IsNullOrWhiteSpace(url); page++)
        {
            token.ThrowIfCancellationRequested();
            string json;
            try
            {
                json = await DownloadStringAsync(url, token).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(json))
                break;

            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch
            {
                break;
            }

            if (obj["results"] is JArray items)
            {
                foreach (var item in items)
                {
                    var id = item?["id"]?.ToString();
                    var playlistTitle = item?["title"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    Add(results, $"https://rutube.ru/plst/{id}/", playlistTitle);

                    if (string.IsNullOrWhiteSpace(title))
                        title = item?["author"]?["name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(thumbnail))
                        thumbnail = MediaSourceHelpers.NormalizeThumbnailUrl(item?["author"]?["avatar_url"]?.ToString() ?? "");
                }
            }

            var hasNext = obj["has_next"]?.Type == JTokenType.Boolean && obj.Value<bool>("has_next");
            var next = obj["next"]?.ToString();
            url = hasNext && !string.IsNullOrWhiteSpace(next) ? next : "";
        }

        return (title, thumbnail);
    }

    private static void CollectFromHtml(string html, Dictionary<string, FoundPlaylist> results)
    {
        foreach (Match match in RutubePlaylistWithTitleRegex().Matches(html))
        {
            var id = FirstGroup(match, 1, 4);
            var title = FirstGroup(match, 2, 3);
            if (!string.IsNullOrWhiteSpace(id))
                Add(results, $"https://rutube.ru/plst/{id}/", Decode(title));
        }

        foreach (Match match in RutubePlaylistInnerTextRegex().Matches(html))
        {
            Add(results, $"https://rutube.ru/plst/{match.Groups[1].Value}/", Decode(match.Groups[2].Value));
        }

        foreach (Match match in RutubePlaylistJsonRegex().Matches(html))
        {
            Add(results, $"https://rutube.ru/plst/{match.Groups[1].Value}/", UnescapeJson(match.Groups[2].Value));
        }

        foreach (Match match in RutubePlaylistIdRegex().Matches(html))
        {
            Add(results, $"https://rutube.ru/plst/{match.Groups[1].Value}/", "");
        }

        foreach (Match match in YoutubePlaylistIdRegex().Matches(html))
        {
            var id = match.Groups[1].Value;
            if (id.Equals("WL", StringComparison.OrdinalIgnoreCase)
                || id.Equals("LL", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("RD", StringComparison.OrdinalIgnoreCase))
                continue;
            Add(results, $"https://www.youtube.com/playlist?list={id}", "");
        }

        foreach (Match match in VkPlaylistIdRegex().Matches(html))
        {
            Add(results, $"https://vkvideo.ru/playlist/{match.Groups[1].Value}", "");
        }
    }

    private static async Task<string> TryGetRutubePlaylistOwnerAsync(string playlistId, CancellationToken token)
    {
        try
        {
            var json = await DownloadStringAsync($"https://rutube.ru/api/playlist/custom/{playlistId}/", token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return "";
            var obj = JObject.Parse(json);
            var userId = obj["user_id"]?.ToString();
            return string.IsNullOrWhiteSpace(userId) ? "" : userId;
        }
        catch
        {
            return "";
        }
    }

    private static async Task<string> DownloadStringAsync(string url, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/json");
        using var response = await client.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return "";
        return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }

    private static void Add(Dictionary<string, FoundPlaylist> results, string url, string title)
    {
        var key = CanonicalKey(url);
        if (string.IsNullOrWhiteSpace(key))
            return;

        title = (title ?? "").Replace('\n', ' ').Trim();
        if (results.TryGetValue(key, out var existing))
        {
            if (!string.IsNullOrWhiteSpace(title)
                && (string.IsNullOrWhiteSpace(existing.Title) || title.Length > existing.Title.Length))
                results[key] = new FoundPlaylist { Url = existing.Url, Title = title };
            return;
        }

        results[key] = new FoundPlaylist { Url = key, Title = title };
    }

    private static string CanonicalKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";
        url = MediaSourceHelpers.NormalizeUrl(url);
        if (TryGetRutubePlaylistId(url, out var rutubeId))
            return $"https://rutube.ru/plst/{rutubeId}/";
        if (TryGetYoutubePlaylistId(url, out var youtubeId))
            return $"https://www.youtube.com/playlist?list={youtubeId}";
        if (TryGetVkPlaylistId(url, out var vkId))
            return $"https://vkvideo.ru/playlist/{vkId}";
        return "";
    }

    private static bool TryGetRutubeChannelId(string url, out string id)
    {
        var match = RutubeChannelIdRegex().Match(url);
        id = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private static string ExtractChannelName(string html, string channelId)
    {
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            var named = Regex.Match(html, $"\"id\":{Regex.Escape(channelId)},\"name\":\"((?:\\\\.|[^\"\\\\])*)\"");
            if (named.Success)
                return UnescapeJson(named.Groups[1].Value);
        }

        var title = Regex.Match(html, @"<title>([^<]+)</title>", RegexOptions.IgnoreCase);
        if (!title.Success)
            return "";
        var value = Decode(title.Groups[1].Value);
        var cut = value.IndexOf(" – ", StringComparison.Ordinal);
        if (cut < 0)
            cut = value.IndexOf(" - ", StringComparison.Ordinal);
        if (cut > 0)
            value = value[..cut];
        return value.Trim();
    }

    private static string UnescapeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        try
        {
            return JsonConvert.DeserializeObject<string>("\"" + value + "\"") ?? Decode(value);
        }
        catch
        {
            return Decode(value);
        }
    }

    public static bool TryGetRutubePlaylistId(string url, out string id)
    {
        var match = RutubePlaylistIdRegex().Match(url ?? "");
        id = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    public static async Task<string> TryGetRutubePlaylistTitleAsync(string playlistId, CancellationToken token)
    {
        try
        {
            var json = await DownloadStringAsync($"https://rutube.ru/api/playlist/custom/{playlistId}/", token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return "";
            var title = JObject.Parse(json)["title"]?.ToString();
            return string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
        }
        catch
        {
            return "";
        }
    }

    private static bool TryGetYoutubePlaylistId(string url, out string id)
    {
        var match = YoutubePlaylistIdRegex().Match(url);
        id = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private static bool TryGetVkPlaylistId(string url, out string id)
    {
        var match = VkPlaylistIdRegex().Match(url);
        id = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private static string FirstGroup(Match match, params int[] indexes)
    {
        foreach (var index in indexes)
        {
            if (index <= match.Groups.Count && match.Groups[index].Success && match.Groups[index].Length > 0)
                return match.Groups[index].Value;
        }
        return "";
    }

    private static string Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return WebUtility.HtmlDecode(value).Trim();
    }

    [GeneratedRegex(@"rutube\.ru/plst/(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RutubePlaylistIdRegex();

    [GeneratedRegex(@"href\s*=\s*[""'](?:https?:)?(?://(?:www\.)?rutube\.ru)?/plst/(\d+)/?[""'][^>]*title\s*=\s*[""']([^""']*)[""']|title\s*=\s*[""']([^""']*)[""'][^>]*href\s*=\s*[""'](?:https?:)?(?://(?:www\.)?rutube\.ru)?/plst/(\d+)/?[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RutubePlaylistWithTitleRegex();

    [GeneratedRegex(@"href\s*=\s*[""'](?:https?:)?(?://(?:www\.)?rutube\.ru)?/plst/(\d+)/?[^""']*[""'][^>]*>([^<]{1,200})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RutubePlaylistInnerTextRegex();

    [GeneratedRegex(@"""id""\s*:\s*(\d{3,})\s*,\s*""title""\s*:\s*""((?:\\.|[^""\\])*)""\s*,\s*""videos_count""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RutubePlaylistJsonRegex();

    [GeneratedRegex(@"rutube\.ru/channel/(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RutubeChannelIdRegex();

    [GeneratedRegex(@"(?:youtube\.com/playlist\?list=|youtube\.com/watch\?[^""'\s<>]*list=|youtu\.be/[^""'\s<>]*list=)([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YoutubePlaylistIdRegex();

    [GeneratedRegex(@"(?:vkvideo\.ru|vk\.com)/playlist/(-?\d+_\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VkPlaylistIdRegex();
}
