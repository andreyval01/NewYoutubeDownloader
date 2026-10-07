namespace YoutubePlaylistDownloader;

static class AppPaths
{
    public const string FolderName = "NewYoutubeDownloader";
    const string LegacyFolderName = "Youtube Playlist Downloader";

    public static string AppDataDirectory { get; } = ResolveAppData();
    public static string TempDirectory { get; } = Path.Combine(Path.GetTempPath(), FolderName);

    static string ResolveAppData()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var current = Path.Combine(root, FolderName);
        var legacy = Path.Combine(root, LegacyFolderName);
        if (!Directory.Exists(current) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, current);
            }
            catch (Exception)
            {
                CopyMissing(legacy, current);
            }
        }

        Directory.CreateDirectory(current);
        return current;
    }

    static void CopyMissing(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(file));
            if (File.Exists(dest))
                continue;
            try { File.Copy(file, dest); } catch { }
        }

        foreach (var dir in Directory.GetDirectories(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(dir));
            if (!Directory.Exists(dest))
            {
                try
                {
                    Directory.Move(dir, dest);
                    continue;
                }
                catch { }
            }

            CopyMissing(dir, dest);
        }
    }
}
