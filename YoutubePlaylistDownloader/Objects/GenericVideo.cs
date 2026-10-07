namespace YoutubePlaylistDownloader.Objects;

internal enum MediaSourceKind
{
    YouTube,
    Rutube,
    VkVideo
}

internal sealed class GenericVideo : IVideo
{
    private static readonly ChannelId DummyChannelId = new("UCGeneric00000000000000");
    private static readonly VideoId DummyVideoId = new("___________");

    public GenericVideo(
        string id,
        string url,
        string title,
        string author,
        TimeSpan? duration,
        IReadOnlyList<Thumbnail> thumbnails,
        long? viewCount,
        MediaSourceKind source)
    {
        SourceId = string.IsNullOrWhiteSpace(id) ? url : id;
        Id = DummyVideoId;
        Url = url ?? "";
        Title = string.IsNullOrWhiteSpace(title) ? SourceId : title;
        Author = new Author(DummyChannelId, author ?? "");
        Duration = duration;
        Thumbnails = thumbnails ?? [];
        ViewCount = viewCount;
        Source = source;
    }

    public string SourceId { get; }
    public MediaSourceKind Source { get; }
    public VideoId Id { get; }
    public string Url { get; }
    public string Title { get; }
    public Author Author { get; }
    public TimeSpan? Duration { get; }
    public IReadOnlyList<Thumbnail> Thumbnails { get; }
    public long? ViewCount { get; }
}

internal sealed class ExternalMediaInfo
{
    public bool IsPlaylist { get; init; }
    public string Title { get; init; }
    public string Uploader { get; init; }
    public string Thumbnail { get; init; }
    public long? ViewCount { get; init; }
    public IReadOnlyList<GenericVideo> Videos { get; init; } = [];
}
