namespace YoutubePlaylistDownloader.Utilities;

internal sealed class SessionSnapshot
{
    [JsonProperty("state")]
    public string State { get; set; }

    [JsonProperty("resume")]
    public string Resume { get; set; }

    [JsonProperty("downloads")]
    public int Downloads { get; set; }

    [JsonProperty("transcodes")]
    public int Transcodes { get; set; }

    [JsonProperty("utc")]
    public string Utc { get; set; }
}

internal static class SessionStore
{
    static readonly object Sync = new();
    static readonly string FilePath = Path.Combine(AppPaths.AppDataDirectory, "Session.json");
    static readonly JsonSerializerSettings Json = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static SessionSnapshot Load()
    {
        lock (Sync)
        {
            if (!File.Exists(FilePath))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<SessionSnapshot>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                try
                {
                    var backup = FilePath + ".bak";
                    if (!File.Exists(backup))
                        File.Copy(FilePath, backup);
                }
                catch
                {
                }

                GlobalConsts.NyLog(ex.ToString(), "Session").Wait();
                return null;
            }
        }
    }

    public static void Write(string state, string resume, int downloads, int transcodes)
    {
        lock (Sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                var json = JsonConvert.SerializeObject(new SessionSnapshot
                {
                    State = state,
                    Resume = resume,
                    Downloads = Math.Max(0, downloads),
                    Transcodes = Math.Max(0, transcodes),
                    Utc = DateTime.UtcNow.ToString("o")
                }, Json);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, null);
                else
                    File.Move(temp, FilePath);
            }
            catch (Exception ex)
            {
                GlobalConsts.NyLog(ex.ToString(), "Session write").Wait();
            }
        }
    }
}
