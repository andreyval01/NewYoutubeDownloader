using System.Net;
using System.Security.Cryptography;

namespace YoutubePlaylistDownloader.Utilities;

internal sealed class RutubeSession
{
    private static readonly object Gate = new();
    private static RutubeSession current;

    public static RutubeSession Current
    {
        get
        {
            lock (Gate)
                return current ??= NyLoad();
        }
    }

    public bool IsSignedIn { get; private set; }
    public string StatusText { get; private set; } = "";

    private readonly string encryptedPath;
    private readonly string plainCookiesPath;
    private string netscapeText = "";

    private RutubeSession()
    {
        var appData = AppPaths.AppDataDirectory;
        Directory.CreateDirectory(appData);
        encryptedPath = Path.Combine(appData, "rutube-session.bin");
        plainCookiesPath = Path.Combine(appData, "rutube-cookies.txt");
    }

    public string CookiesFilePath => plainCookiesPath;

    public static event Action Changed;

    private static RutubeSession NyLoad()
    {
        var session = new RutubeSession();
        session.TryLoadEncrypted();
        return session;
    }

    private void TryLoadEncrypted()
    {
        try
        {
            if (!File.Exists(encryptedPath))
            {
                SetSignedOut();
                return;
            }

            var protectedBytes = File.ReadAllBytes(encryptedPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            netscapeText = Encoding.UTF8.GetString(bytes);
            IsSignedIn = LooksSignedIn(netscapeText);
            if (IsSignedIn)
            {
                File.WriteAllText(plainCookiesPath, netscapeText);
                StatusText = "Rutube";
            }
            else
                SetSignedOut();
        }
        catch
        {
            SetSignedOut();
        }
    }

    public void SaveNetscape(string text)
    {
        netscapeText = FilterRutubeCookies(text);
        IsSignedIn = LooksSignedIn(netscapeText);
        if (!IsSignedIn)
        {
            Clear();
            return;
        }

        File.WriteAllText(plainCookiesPath, netscapeText);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(netscapeText),
            null,
            DataProtectionScope.CurrentUser
        );
        File.WriteAllBytes(encryptedPath, protectedBytes);
        StatusText = "Rutube";
        Changed?.Invoke();
    }

    public void SaveCookies(IEnumerable<Cookie> cookies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        foreach (var cookie in cookies)
        {
            if (cookie is null || string.IsNullOrWhiteSpace(cookie.Name))
                continue;
            var domain = cookie.Domain ?? "";
            if (!IsRutubeRelated(domain))
                continue;
            var host = domain.StartsWith('.') ? domain : "." + domain.TrimStart('.');
            var secure = cookie.Secure ? "TRUE" : "FALSE";
            var expires = cookie.Expires == DateTime.MinValue
                ? "0"
                : new DateTimeOffset(cookie.Expires).ToUnixTimeSeconds().ToString();
            var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
            sb.Append(host).Append('\t').Append("TRUE").Append('\t').Append(path).Append('\t')
                .Append(secure).Append('\t').Append(expires).Append('\t')
                .Append(cookie.Name).Append('\t').Append(cookie.Value).Append('\n');
        }

        SaveNetscape(sb.ToString());
    }

    public void Clear()
    {
        netscapeText = "";
        IsSignedIn = false;
        StatusText = "";
        try { if (File.Exists(encryptedPath)) File.Delete(encryptedPath); } catch { }
        try { if (File.Exists(plainCookiesPath)) File.Delete(plainCookiesPath); } catch { }
        Changed?.Invoke();
    }

    public bool TryImportFirefox(string ytDlpPath, out string error)
    {
        error = null;
        var profiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla",
            "Firefox",
            "Profiles"
        );
        if (!Directory.Exists(profiles))
        {
            error = "Firefox";
            return false;
        }

        var temp = Path.Combine(Path.GetTempPath(), "ypd-ff-rutube-cookies.txt");
        try
        {
            if (File.Exists(temp))
                File.Delete(temp);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments =
                        "--cookies-from-browser firefox --cookies \""
                        + temp
                        + "\" --skip-download --no-playlist --ignore-no-formats-error --no-warnings --no-progress -- "
                        + "https://rutube.ru/",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
            };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120000))
            {
                try { process.Kill(true); } catch { }
                error = "Firefox";
                return false;
            }

            Task.WaitAll(stdoutTask, stderrTask);
            var stderr = stderrTask.Result;

            if (!File.Exists(temp))
            {
                error = string.IsNullOrWhiteSpace(stderr) ? "Firefox" : stderr.Trim();
                return false;
            }

            SaveNetscape(File.ReadAllText(temp));
            if (!IsSignedIn)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? "Firefox" : stderr.Trim();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private void SetSignedOut()
    {
        IsSignedIn = false;
        StatusText = "";
        netscapeText = "";
    }

    private static bool LooksSignedIn(string netscape)
    {
        foreach (var line in netscape.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 7 || !IsRutubeRelated(parts[0]))
                continue;
            var name = parts[5];
            if (name.Equals("jwt", StringComparison.OrdinalIgnoreCase)
                || name.Equals("sessionid", StringComparison.OrdinalIgnoreCase)
                || name.Equals("eapi", StringComparison.OrdinalIgnoreCase)
                || name.Equals("rt_sso", StringComparison.OrdinalIgnoreCase)
                || name.Contains("auth", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("user", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(parts[6]);
            }
        }

        return false;
    }

    private static bool IsRutubeRelated(string domain)
    {
        domain = domain.TrimStart('.').ToLowerInvariant();
        return domain == "rutube.ru"
            || domain.EndsWith(".rutube.ru", StringComparison.Ordinal);
    }

    private static string FilterRutubeCookies(string text)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
                continue;
            var parts = line.Split('\t');
            if (parts.Length < 7)
                continue;
            if (IsRutubeRelated(parts[0]))
                sb.AppendLine(line);
        }
        return sb.ToString();
    }
}
