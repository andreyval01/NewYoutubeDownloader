using System.Globalization;
using System.Windows.Markup;

namespace YoutubePlaylistDownloader;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{

    public App()
    {
        DispatcherUnhandledException += NyApp_DispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        base.OnStartup(e);
        GlobalConsts.NyLoadConsts();
        GlobalConsts.NyCreateTempFolder();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GlobalConsts.NySaveConsts();
        if (GlobalConsts.UpdateOnExit && !string.IsNullOrWhiteSpace(GlobalConsts.UpdateSetupLocation) && GlobalConsts.UpdateFinishedDownloading)
        {
            Process.Start(GlobalConsts.UpdateSetupLocation);
        }
        else
        {
            GlobalConsts.NyCleanTempFolder();
        }
        base.OnExit(e);
    }

    async void NyApp_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        await GlobalConsts.NyShowMessage((string)FindResource("Error"), (string)FindResource("ErrorMessage"));
        await GlobalConsts.NyLog($"{e.Exception}", "Unhandled exception");
        e.Handled = true;
    }
}
