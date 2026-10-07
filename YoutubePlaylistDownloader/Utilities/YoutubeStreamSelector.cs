namespace YoutubePlaylistDownloader.Utilities;

internal static class YoutubeStreamSelector
{
    public static IStreamInfo GetBestAudioOrMuxed(StreamManifest manifest, string language)
    {
        return TrySelectAudio(manifest.GetAudioOnlyStreams(), language)
            ?? TrySelectAudio(manifest.GetAudioStreams(), language)
            ?? manifest.GetMuxedStreams().TryGetWithHighestBitrate()
            ?? throw new InvalidOperationException("No playable audio or muxed streams.");
    }

    public static IVideoStreamInfo TryGetBestVideoOnly(StreamManifest manifest, VideoQuality quality, bool preferHighestFps)
    {
        var videoList = manifest.GetVideoOnlyStreams();
        if (!videoList.Any())
            return null;

        var ordered = videoList.OrderByDescending(x => x.VideoQuality.MaxHeight);
        return (preferHighestFps
            ? ordered.ThenByDescending(x => x.VideoQuality.Framerate)
            : ordered.ThenBy(x => x.VideoQuality.Framerate)).FirstOrDefault();
    }

    public static IStreamInfo TryGetBestAudioOnly(StreamManifest manifest, string language) =>
        TrySelectAudio(manifest.GetAudioOnlyStreams(), language);

    public static MuxedStreamInfo TryGetBestMuxed(StreamManifest manifest) =>
        manifest.GetMuxedStreams().TryGetWithHighestVideoQuality() as MuxedStreamInfo
        ?? manifest.GetMuxedStreams().TryGetWithHighestBitrate() as MuxedStreamInfo;

    private static IStreamInfo TrySelectAudio(IEnumerable<IAudioStreamInfo> streams, string language)
    {
        var audio = streams.ToList();
        if (audio.Count == 0)
            return null;

        if (!string.Equals(language, "default", StringComparison.OrdinalIgnoreCase))
        {
            var inLanguage = audio
                .Where(x => x.AudioLanguage.HasValue && x.AudioLanguage.Value.Code.Contains(language))
                .ToList();
            if (inLanguage.Count > 0)
                return inLanguage.GetWithHighestBitrate();
        }

        var defaults = audio.Where(x => x.IsAudioLanguageDefault == true).ToList();
        if (defaults.Count > 0)
            return defaults.GetWithHighestBitrate();

        return audio.GetWithHighestBitrate();
    }
}
