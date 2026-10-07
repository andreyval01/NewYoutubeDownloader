using System.Net;
using Microsoft.Web.WebView2.Core;
using Cookie = System.Net.Cookie;

namespace YoutubePlaylistDownloader;

public partial class YoutubeLoginWindow : MetroWindow
{
    public bool SessionSaved { get; private set; }

    public YoutubeLoginWindow()
    {
        InitializeComponent();
        Loaded += YoutubeLoginWindow_Loaded;
    }

    private async void YoutubeLoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppPaths.FolderName,
            "WebView2"
        );
        Directory.CreateDirectory(userData);
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Navigate(
            "https://accounts.google.com/ServiceLogin?service=youtube&continue=https://www.youtube.com"
        );
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveButton.IsEnabled = false;
            StatusText.Text = (string)FindResource("CapturingSession");
            var youtube = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.youtube.com");
            var google = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.google.com");
            var mapped = youtube.Concat(google).Select(MapCookie).Where(c => c is not null).ToList();
            YoutubeSession.Current.SaveCookies(mapped);
            if (!YoutubeSession.Current.IsSignedIn)
            {
                StatusText.Text = (string)FindResource("YouTubeLoginNotDetected");
                SaveButton.IsEnabled = true;
                return;
            }

            SessionSaved = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            await GlobalConsts.NyLog(ex.ToString(), "YoutubeLoginWindow.Save");
            StatusText.Text = ex.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static Cookie MapCookie(CoreWebView2Cookie source)
    {
        if (source is null)
            return null;
        try
        {
            return new Cookie(source.Name, source.Value, source.Path, source.Domain)
            {
                Secure = source.IsSecure,
                HttpOnly = source.IsHttpOnly,
                Expires = source.Expires,
            };
        }
        catch
        {
            return null;
        }
    }
}
