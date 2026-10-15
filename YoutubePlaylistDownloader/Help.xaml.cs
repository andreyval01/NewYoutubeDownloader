namespace YoutubePlaylistDownloader;

/// <summary>
/// Interaction logic for Help.xaml
/// </summary>
public partial class Help : UserControl
{
    public Help()
    {
        InitializeComponent();
        VersionText.Text = $"{FindResource("Title")} v{GlobalConsts.VERSION}";
        GlobalConsts.NyHideHelpButton();
        GlobalConsts.NyShowHomeButton();
        GlobalConsts.NyShowSettingsButton();
        GlobalConsts.NyShowAboutButton();
    }

    void OpenGitHub(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        if (e.Uri != null)
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
