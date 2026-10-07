namespace YoutubePlaylistDownloader;


/// <summary>
/// Interaction logic for Settings.xaml
/// </summary>
public partial class Settings : UserControl
{
    public Settings()
    {
        InitializeComponent();
        NyFillAccents();
        NyFillLanguages();

        if (GlobalConsts.settings.Theme == "Dark") NightModeCheckBox.IsChecked = true;
        SaveDownloadOptionsCheckBox.IsChecked = GlobalConsts.settings.SaveDownloadOptions;
        LimitConversionsCheckBox.IsChecked = GlobalConsts.settings.LimitConversions;
        ConfirmOnExitCheckBox.IsChecked = GlobalConsts.settings.ConfirmExit;
        ActualConversionTextBox.Text = GlobalConsts.settings.ActualConversionsLimit.ToString();
        ActualConversionTextBox.TextChanged += NyActualConversionTextBox_TextChanged;

        NightModeCheckBox.Checked += NyNightModeCheckBox_Checked;
        NightModeCheckBox.Unchecked += NyNightModeCheckBox_Unchecked;
        RefreshYouTubeAccountStatus();
        RefreshRutubeAccountStatus();

        GlobalConsts.NyHideSettingsButton();
        GlobalConsts.NyShowHomeButton();
        GlobalConsts.NyShowAboutButton();
        GlobalConsts.NyShowHelpButton();
    }

    private void NyFillLanguages()
    {
        var languages = ((string)FindResource("LanguageList")).Split(';');
        LanguageComboBox.ItemsSource = languages;
        LanguageComboBox.SelectedItem = GlobalConsts.settings.Language;
    }

    private void NyFillAccents()
    {
        var accents = ThemeManager.Current.ColorSchemes;
        comboBox.ItemsSource = accents;
        comboBox.SelectedItem = ((IEnumerable<string>)comboBox.ItemsSource).FirstOrDefault(x => x == GlobalConsts.settings.Accent);
    }

    private void NyNightModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        ThemeManager.Current.ChangeTheme(Application.Current, $"Dark.{GlobalConsts.settings.Accent}");
        ThemeManager.Current.ChangeTheme(Application.Current, $"Light.{GlobalConsts.settings.Accent}");
        GlobalConsts.settings.Theme = "Light";
    }

    private void NyNightModeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        ThemeManager.Current.ChangeTheme(Application.Current, $"Light.{GlobalConsts.settings.Accent}");
        ThemeManager.Current.ChangeTheme(Application.Current, $"Dark.{GlobalConsts.settings.Accent}");
        GlobalConsts.settings.Theme = "Dark";
    }

    private void NyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ThemeManager.Current.ChangeTheme(Application.Current, $"{GlobalConsts.settings.Theme}.{comboBox.SelectedItem}");
        GlobalConsts.settings.Accent = (string)comboBox.SelectedItem;
    }

    private void NyExit_Click(object sender, RoutedEventArgs e)
    {
        GlobalConsts.NySaveConsts();
        GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
    }

    private void NyLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        GlobalConsts.NyChangeLanguage((string)LanguageComboBox.SelectedItem);
        FlowDirection = (FlowDirection)FindResource("FlowDirection");
    }

    private void NySaveDownloadOptionsCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.settings.SaveDownloadOptions = SaveDownloadOptionsCheckBox.IsChecked.Value;
    }

    private void NySaveDownloadOptionsCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.settings.SaveDownloadOptions = SaveDownloadOptionsCheckBox.IsChecked.Value;
    }

    private void NyLimitConversionsCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.settings.LimitConversions = LimitConversionsCheckBox.IsChecked.Value;
    }

    private void NyLimitConversionsCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.settings.LimitConversions = LimitConversionsCheckBox.IsChecked.Value;
    }

    private void NyActualConversionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(ActualConversionTextBox.Text, out var actual) && actual > 0 && actual < GlobalConsts.settings.MaximumConversionsCount)
        {
            ActualConversionTextBox.Background = null;
            var delta = actual - GlobalConsts.ConversionsLocker.CurrentCount;
            if (delta > 0)
                GlobalConsts.ConversionsLocker.Release(delta);
            else
            {
                delta = Math.Abs(delta);
                for (var i = 0; i < delta; i++)
                {
                    GlobalConsts.ConversionsLocker.Wait();
                }
            }
            GlobalConsts.settings.ActualConversionsLimit = actual;
        }
        else
        {
            ActualConversionTextBox.Background = GlobalConsts.ErrorBrush;
        }
    }

    private void NyConfirmOnExitCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.settings.ConfirmExit = ConfirmOnExitCheckBox.IsChecked.Value;
    }

    private void NyConfirmOnExitCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.settings.ConfirmExit = ConfirmOnExitCheckBox.IsChecked.Value;
    }

    private void RefreshYouTubeAccountStatus()
    {
        YouTubeAccountStatus.Text = YoutubeSession.Current.IsSignedIn
            ? (string)FindResource("YouTubeSignedIn")
            : (string)FindResource("YouTubeSignedOut");
    }

    private void SignInYouTube_Click(object sender, RoutedEventArgs e)
    {
        var window = new YoutubeLoginWindow
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
        RefreshYouTubeAccountStatus();
    }

    private async void ImportFirefox_Click(object sender, RoutedEventArgs e)
    {
        if (!YtDlpDownloader.IsAvailable)
        {
            await GlobalConsts.NyShowMessage((string)FindResource("Error"), "yt-dlp.exe");
            return;
        }

        if (!YoutubeSession.Current.TryImportFirefox(YtDlpDownloader.ExePath, out var error)
            || !YoutubeSession.Current.IsSignedIn)
        {
            await GlobalConsts.NyShowMessage(
                (string)FindResource("Error"),
                (string)FindResource("FirefoxCookiesNotFound") + (string.IsNullOrWhiteSpace(error) ? "" : "\n" + error)
            );
            RefreshYouTubeAccountStatus();
            return;
        }

        RefreshYouTubeAccountStatus();
    }

    private void SignOutYouTube_Click(object sender, RoutedEventArgs e)
    {
        YoutubeSession.Current.Clear();
        RefreshYouTubeAccountStatus();
    }

    private void RefreshRutubeAccountStatus()
    {
        RutubeAccountStatus.Text = RutubeSession.Current.IsSignedIn
            ? (string)FindResource("RutubeSignedIn")
            : (string)FindResource("RutubeSignedOut");
    }

    private void SignInRutube_Click(object sender, RoutedEventArgs e)
    {
        var window = new RutubeLoginWindow
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
        RefreshRutubeAccountStatus();
    }

    private async void ImportFirefoxRutube_Click(object sender, RoutedEventArgs e)
    {
        if (!YtDlpDownloader.IsAvailable)
        {
            await GlobalConsts.NyShowMessage((string)FindResource("Error"), "yt-dlp.exe");
            return;
        }

        if (!RutubeSession.Current.TryImportFirefox(YtDlpDownloader.ExePath, out var error)
            || !RutubeSession.Current.IsSignedIn)
        {
            await GlobalConsts.NyShowMessage(
                (string)FindResource("Error"),
                (string)FindResource("FirefoxRutubeCookiesNotFound") + (string.IsNullOrWhiteSpace(error) ? "" : "\n" + error)
            );
            RefreshRutubeAccountStatus();
            return;
        }

        RefreshRutubeAccountStatus();
    }

    private void SignOutRutube_Click(object sender, RoutedEventArgs e)
    {
        RutubeSession.Current.Clear();
        RefreshRutubeAccountStatus();
    }
}
