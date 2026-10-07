namespace YoutubePlaylistDownloader.Objects;

public class FullPlaylist(Playlist basePlaylist, IEnumerable<IVideo> videos, string title = null, bool isPlaylist = false)
{
    public Playlist BasePlaylist { get; private set; } = basePlaylist;
    public IEnumerable<IVideo> Videos { get; private set; } = videos;

    public string Title { get; private set; } = basePlaylist?.Title ?? title;
    public bool IsPlaylist { get; private set; } = isPlaylist || basePlaylist != null;
}
