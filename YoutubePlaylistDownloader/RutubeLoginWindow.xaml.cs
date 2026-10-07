using System.Net;
using Microsoft.Web.WebView2.Core;
using Cookie = System.Net.Cookie;

namespace YoutubePlaylistDownloader;

public partial class RutubeLoginWindow : MetroWindow
{
    public bool SessionSaved { get; private set; }

    public RutubeLoginWindow()
    {
        InitializeComponent();
        Loaded += RutubeLoginWindow_Loaded;
    }

    private async void RutubeLoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppPaths.FolderName,
            "WebView2-Rutube"
        );
        Directory.CreateDirectory(userData);
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Navigate("https://rutube.ru/login/");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveButton.IsEnabled = false;
            StatusText.Text = (string)FindResource("CapturingSession");
            var rutube = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://rutube.ru");
            var www = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.rutube.ru");
            var studio = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://studio.rutube.ru");
            var mapped = rutube.Concat(www).Concat(studio).Select(MapCookie).Where(c => c is not null).ToList();
            RutubeSession.Current.SaveCookies(mapped);
            if (!RutubeSession.Current.IsSignedIn)
            {
                StatusText.Text = (string)FindResource("RutubeLoginNotDetected");
                SaveButton.IsEnabled = true;
                return;
            }

            SessionSaved = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            await GlobalConsts.NyLog(ex.ToString(), "RutubeLoginWindow.Save");
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
