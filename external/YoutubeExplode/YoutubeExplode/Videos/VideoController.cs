using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using JsonExtensions.Reading;
using YoutubeExplode.Bridge;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Utils;

namespace YoutubeExplode.Videos;

internal class VideoController(HttpClient http)
{
    private string? _visitorData;

    protected HttpClient Http { get; } = http;

    private async ValueTask<string> ResolveVisitorDataAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(_visitorData))
            return _visitorData;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.youtube.com/sw.js_data"
        );

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        request.Headers.Add(
            "User-Agent",
            "com.google.android.youtube/20.10.38 (Linux; U; ANDROID 11) gzip"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // TODO: move this to a bridge wrapper
        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
        if (jsonString.StartsWith(")]}'"))
            jsonString = jsonString[4..];

        var json = Json.Parse(jsonString);

        // This is just an ordered (but unstructured) blob of data
        var value = json[0][2][0][0][13].GetStringOrNull();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new YoutubeExplodeException("Failed to resolve visitor data.");
        }

        return _visitorData = value;
    }

    public async ValueTask<VideoWatchPage> GetVideoWatchPageAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    )
    {
        for (var retriesRemaining = 5; ; retriesRemaining--)
        {
            var watchPage = VideoWatchPage.TryParse(
                await Http.GetStringAsync(
                    $"https://www.youtube.com/watch?v={videoId}&bpctr=9999999999",
                    cancellationToken
                )
            );

            if (watchPage is null)
            {
                if (retriesRemaining > 0)
                    continue;

                throw new YoutubeExplodeException(
                    "Video watch page is broken. Please try again in a few minutes."
                );
            }

            if (!watchPage.IsAvailable)
                throw new VideoUnavailableException($"Video '{videoId}' is not available.");

            return watchPage;
        }
    }

    private async ValueTask<PlayerResponse> GetPlayerResponseForIosAsync(
        VideoId videoId,
        string visitorData,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        );

        request.Content = new StringContent(
            // lang=json
            $$"""
            {
              "videoId": {{Json.Encode(videoId)}},
              "contentCheckOk": true,
              "racyCheckOk": true,
              "context": {
                "client": {
                  "clientName": "IOS",
                  "clientVersion": "20.11.6",
                  "deviceMake": "Apple",
                  "deviceModel": "iPhone16,2",
                  "osName": "iOS",
                  "osVersion": "18.1.0.22B83",
                  "visitorData": {{Json.Encode(visitorData)}},
                  "hl": "en",
                  "gl": "US",
                  "utcOffsetMinutes": 0
                }
              }
            }
            """
        );

        request.Headers.Add(
            "User-Agent",
            "com.google.ios.youtube/20.11.6 (iPhone16,2; U; CPU iOS 18_1_0 like Mac OS X)"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var playerResponse = PlayerResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (!playerResponse.IsAvailable)
            throw new VideoUnavailableException($"Video '{videoId}' is not available.");

        if (!playerResponse.IsPlayable)
            throw new VideoUnplayableException($"Video '{videoId}' is unplayable.");

        return playerResponse;
    }

    private async ValueTask<PlayerResponse> GetPlayerResponseForVisionOsAsync(
        VideoId videoId,
        string visitorData,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        );

        request.Content = new StringContent(
            // lang=json
            $$"""
            {
              "videoId": {{Json.Encode(videoId)}},
              "contentCheckOk": true,
              "racyCheckOk": true,
              "context": {
                "client": {
                  "clientName": "VISIONOS",
                  "clientVersion": "1.02",
                  "deviceMake": "Apple",
                  "deviceModel": "RealityDevice17,1",
                  "osName": "visionOS",
                  "osVersion": "26.5.23O471",
                  "visitorData": {{Json.Encode(visitorData)}},
                  "hl": "en",
                  "gl": "US",
                  "utcOffsetMinutes": 0
                }
              }
            }
            """
        );

        request.Headers.Add(
            "User-Agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var playerResponse = PlayerResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (!playerResponse.IsAvailable)
            throw new VideoUnavailableException($"Video '{videoId}' is not available.");

        if (!playerResponse.IsPlayable)
            throw new VideoUnplayableException($"Video '{videoId}' is unplayable.");

        return playerResponse;
    }

    private async ValueTask<PlayerResponse> GetPlayerResponseForAndroidAsync(
        VideoId videoId,
        string visitorData,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        );

        request.Content = new StringContent(
            // lang=json
            $$"""
            {
              "videoId": {{Json.Encode(videoId)}},
              "contentCheckOk": true,
              "racyCheckOk": true,
              "context": {
                "client": {
                  "clientName": "ANDROID",
                  "clientVersion": "20.10.38",
                  "androidSdkVersion": 30,
                  "osName": "Android",
                  "osVersion": "11",
                  "visitorData": {{Json.Encode(visitorData)}},
                  "hl": "en",
                  "gl": "US",
                  "utcOffsetMinutes": 0
                }
              }
            }
            """
        );
        request.Headers.Add(
            "User-Agent",
            "com.google.android.youtube/20.10.38 (Linux; U; Android 11) gzip"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var playerResponse = PlayerResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (!playerResponse.IsAvailable)
            throw new VideoUnavailableException($"Video '{videoId}' is not available.");

        if (!playerResponse.IsPlayable)
            throw new VideoUnplayableException($"Video '{videoId}' is unplayable.");

        return playerResponse;
    }

    private async ValueTask<PlayerResponse> GetPlayerResponseForTvAsync(
        VideoId videoId,
        string visitorData,
        string? signatureTimestamp,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/player"
        );

        request.Content = new StringContent(
            // lang=json
            $$"""
            {
                "videoId": {{Json.Encode(videoId)}},
                "context": {
                    "client": {
                        "clientName": "TVHTML5_SIMPLY_EMBEDDED_PLAYER",
                        "clientVersion": "2.0",
                        "visitorData": {{Json.Encode(visitorData)}},
                        "hl": "en",
                        "gl": "US",
                        "utcOffsetMinutes": 0
                    },
                    "thirdParty": {
                        "embedUrl": "https://www.youtube.com"
                    }
                },
                "playbackContext": {
                    "contentPlaybackContext": {
                        "signatureTimestamp": {{Json.Encode(signatureTimestamp)}}
                    }
                }
            }
            """
        );
        request.Headers.Add(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4389.114 Safari/537.36"
        );

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var playerResponse = PlayerResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );

        if (!playerResponse.IsAvailable)
            throw new VideoUnavailableException($"Video '{videoId}' is not available.");

        if (!playerResponse.IsPlayable)
            throw new VideoUnplayableException($"Video '{videoId}' is unplayable.");

        return playerResponse;
    }

    public async ValueTask<IReadOnlyList<PlayerResponse>> GetPlayerResponsesAsync(
        VideoId videoId,
        string? signatureTimestamp = null,
        CancellationToken cancellationToken = default
    )
    {
        var visitorData = await ResolveVisitorDataAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(signatureTimestamp))
        {
            return
            [
                await GetPlayerResponseForTvAsync(
                    videoId,
                    visitorData,
                    signatureTimestamp,
                    cancellationToken
                ),
            ];
        }

        Exception? lastError = null;
        var results = new List<PlayerResponse>();

        // iOS still returns downloadable adaptive video (1080p+). Android/VisionOS often
        // only expose muxed 360p plus SABR adaptive formats, which we still want as audio.
        foreach (
            var getter in new Func<CancellationToken, ValueTask<PlayerResponse>>[]
            {
                token => GetPlayerResponseForIosAsync(videoId, visitorData, token),
                token => GetPlayerResponseForAndroidAsync(videoId, visitorData, token),
                token => GetPlayerResponseForVisionOsAsync(videoId, visitorData, token),
            }
        )
        {
            try
            {
                results.Add(await getter(cancellationToken));
            }
            catch (Exception ex) when (ex is VideoUnplayableException or VideoUnavailableException)
            {
                lastError = ex;
            }
        }

        if (results.Count > 0)
            return results;

        throw lastError
            ?? new YoutubeExplodeException($"Failed to resolve player response for video '{videoId}'.");
    }

    public async ValueTask<IReadOnlyList<PlayerResponse>> GetPlayerResponsesAsync(
        VideoId videoId,
        CancellationToken cancellationToken
    ) => await GetPlayerResponsesAsync(videoId, null, cancellationToken);

    public async ValueTask<PlayerResponse> GetPlayerResponseAsync(
        VideoId videoId,
        string? signatureTimestamp = null,
        CancellationToken cancellationToken = default
    ) => (await GetPlayerResponsesAsync(videoId, signatureTimestamp, cancellationToken))[0];

    public async ValueTask<PlayerResponse> GetPlayerResponseAsync(
        VideoId videoId,
        CancellationToken cancellationToken = default
    ) => await GetPlayerResponseAsync(videoId, null, cancellationToken);
}
