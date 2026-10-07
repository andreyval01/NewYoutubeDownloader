namespace YoutubePlaylistDownloader;

/// <summary>
/// Interaction logic for About.xaml
/// </summary>
public partial class About : UserControl
{
    public About()
    {
        InitializeComponent();
        GlobalConsts.NyHideAboutButton();
        GlobalConsts.NyShowHomeButton();
        GlobalConsts.NyShowSettingsButton();
        GlobalConsts.NyShowHelpButton();

        AboutRun.Text = $"{FindResource("AboutTheProgram")}{GlobalConsts.VERSION}";
    }
}
