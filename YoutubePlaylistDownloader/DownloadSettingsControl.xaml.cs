namespace YoutubePlaylistDownloader;

/// <summary>
/// Interaction logic for DownloadSettings.xaml
/// </summary>
public partial class DownloadSettingsControl : UserControl
{
    private readonly Dictionary<string, VideoQuality> Resolutions = new()
    {
        { "144p", YoutubeHelpers.Low144 },
        { "240p", YoutubeHelpers.Low240 },
        { "360p", YoutubeHelpers.Medium360 },
        { "480p", YoutubeHelpers.Medium480 },
        { "720p", YoutubeHelpers.High720 },
        { "1080p", YoutubeHelpers.High1080 },
        { "1440p", YoutubeHelpers.High1440 },
        { "2160p", YoutubeHelpers.High2160 },
        { "2880p", YoutubeHelpers.High2880 },
        { "3072p", YoutubeHelpers.High3072 },
        { "4320p", YoutubeHelpers.High4320 }
    };
    private readonly string[] VideoFileTypes = ["mp4", "mkv"];

    private readonly string[] FileTypes = ["mp3", "aac", "opus", "wav", "flac", "m4a", "ogg", "webm"];

    private readonly Dictionary<string, string> Languages = new() { { "aa", "Afar" }, { "ab", "Abkhazian" }, { "af", "Afrikaans" }, { "ak", "Akan" }, { "sq", "Albanian" }, { "am", "Amharic" }, { "ar", "Arabic" }, { "an", "Aragonese" }, { "hy", "Armenian" }, { "as", "Assamese" }, { "av", "Avaric" }, { "ae", "Avestan" }, { "ay", "Aymara" }, { "az", "Azerbaijani" }, { "ba", "Bashkir" }, { "bm", "Bambara" }, { "eu", "Basque" }, { "be", "Belarusian" }, { "bn", "Bengali" }, { "bh", "Bihari languages" }, { "bi", "Bislama" }, { "bs", "Bosnian" }, { "br", "Breton" }, { "bg", "Bulgarian" }, { "my", "Burmese" }, { "ca", "Catalan" }, { "ch", "Chamorro" }, { "ce", "Chechen" }, { "zh", "Chinese" }, { "cu", "Church Slavic" }, { "cv", "Chuvash" }, { "kw", "Cornish" }, { "co", "Corsican" }, { "cr", "Cree" }, { "cs", "Czech" }, { "da", "Danish" }, { "dv", "Divehi" }, { "nl", "Dutch" }, { "dz", "Dzongkha" }, { "en", "English" }, { "eo", "Esperanto" }, { "et", "Estonian" }, { "ee", "Ewe" }, { "fo", "Faroese" }, { "fj", "Fijian" }, { "fi", "Finnish" }, { "fr", "French" }, { "fy", "Western Frisian" }, { "ff", "Fulah" }, { "ka", "Georgian" }, { "de", "German" }, { "gd", "Gaelic" }, { "ga", "Irish" }, { "gl", "Galician" }, { "gv", "Manx" }, { "el", "Greek" }, { "gn", "Guarani" }, { "gu", "Gujarati" }, { "ht", "Haitian" }, { "ha", "Hausa" }, { "he", "Hebrew" }, { "hz", "Herero" }, { "hi", "Hindi" }, { "ho", "Hiri Motu" }, { "hr", "Croatian" }, { "hu", "Hungarian" }, { "ig", "Igbo" }, { "is", "Icelandic" }, { "io", "Ido" }, { "ii", "Sichuan Yi" }, { "iu", "Inuktitut" }, { "ie", "Interlingue" }, { "ia", "Interlingua" }, { "id", "Indonesian" }, { "ik", "Inupiaq" }, { "it", "Italian" }, { "jv", "Javanese" }, { "ja", "Japanese" }, { "kl", "Kalaallisut" }, { "kn", "Kannada" }, { "ks", "Kashmiri" }, { "kr", "Kanuri" }, { "kk", "Kazakh" }, { "km", "Central Khmer" }, { "ki", "Kikuyu" }, { "rw", "Kinyarwanda" }, { "ky", "Kirghiz" }, { "kv", "Komi" }, { "kg", "Kongo" }, { "ko", "Korean" }, { "kj", "Kuanyama" }, { "ku", "Kurdish" }, { "lo", "Lao" }, { "la", "Latin" }, { "lv", "Latvian" }, { "li", "Limburgan" }, { "ln", "Lingala" }, { "lt", "Lithuanian" }, { "lb", "Luxembourgish" }, { "lu", "Luba-Katanga" }, { "lg", "Ganda" }, { "mk", "Macedonian" }, { "mh", "Marshallese" }, { "ml", "Malayalam" }, { "mi", "Maori" }, { "mr", "Marathi" }, { "ms", "Malay" }, { "mg", "Malagasy" }, { "mt", "Maltese" }, { "mn", "Mongolian" }, { "na", "Nauru" }, { "nv", "Navajo" }, { "nr", "Ndebele, South" }, { "nd", "Ndebele, North" }, { "ng", "Ndonga" }, { "ne", "Nepali" }, { "nn", "Norwegian" }, { "nb", "Bokmål" }, { "no", "Norwegian" }, { "ny", "Chichewa" }, { "oc", "Occitan" }, { "oj", "Ojibwa" }, { "or", "Oriya" }, { "om", "Oromo" }, { "os", "Ossetian" }, { "pa", "Panjabi" }, { "fa", "Persian" }, { "pi", "Pali" }, { "pl", "Polish" }, { "pt", "Portuguese" }, { "ps", "Pushto" }, { "qu", "Quechua" }, { "rm", "Romansh" }, { "ro", "Romanian" }, { "rn", "Rundi" }, { "ru", "Russian" }, { "sg", "Sango" }, { "sa", "Sanskrit" }, { "si", "Sinhala" }, { "sk", "Slovak" }, { "sl", "Slovenian" }, { "se", "Northern Sami" }, { "sm", "Samoan" }, { "sn", "Shona" }, { "sd", "Sindhi" }, { "so", "Somali" }, { "st", "Sotho, Southern" }, { "es", "Spanish" }, { "sc", "Sardinian" }, { "sr", "Serbian" }, { "ss", "Swati" }, { "su", "Sundanese" }, { "sw", "Swahili" }, { "sv", "Swedish" }, { "ty", "Tahitian" }, { "ta", "Tamil" }, { "tt", "Tatar" }, { "te", "Telugu" }, { "tg", "Tajik" }, { "tl", "Tagalog" }, { "th", "Thai" }, { "bo", "Tibetan" }, { "ti", "Tigrinya" }, { "to", "Tonga (Tonga Islands)" }, { "tn", "Tswana" }, { "ts", "Tsonga" }, { "tk", "Turkmen" }, { "tr", "Turkish" }, { "tw", "Twi" }, { "ug", "Uighur" }, { "uk", "Ukrainian" }, { "ur", "Urdu" }, { "uz", "Uzbek" }, { "ve", "Venda" }, { "vi", "Vietnamese" }, { "vo", "Volapük" }, { "cy", "Welsh" }, { "wa", "Walloon" }, { "wo", "Wolof" }, { "xh", "Xhosa" }, { "yi", "Yiddish" }, { "yo", "Yoruba" }, { "za", "Zhuang" }, { "zu", "Zulu" }, };
    private readonly Dictionary<string, string> VideoLanguages = new() { { "default", "default" }, { "aa", "Afar" }, { "ab", "Abkhazian" }, { "af", "Afrikaans" }, { "ak", "Akan" }, { "sq", "Albanian" }, { "am", "Amharic" }, { "ar", "Arabic" }, { "an", "Aragonese" }, { "hy", "Armenian" }, { "as", "Assamese" }, { "av", "Avaric" }, { "ae", "Avestan" }, { "ay", "Aymara" }, { "az", "Azerbaijani" }, { "ba", "Bashkir" }, { "bm", "Bambara" }, { "eu", "Basque" }, { "be", "Belarusian" }, { "bn", "Bengali" }, { "bh", "Bihari languages" }, { "bi", "Bislama" }, { "bs", "Bosnian" }, { "br", "Breton" }, { "bg", "Bulgarian" }, { "my", "Burmese" }, { "ca", "Catalan" }, { "ch", "Chamorro" }, { "ce", "Chechen" }, { "zh", "Chinese" }, { "cu", "Church Slavic" }, { "cv", "Chuvash" }, { "kw", "Cornish" }, { "co", "Corsican" }, { "cr", "Cree" }, { "cs", "Czech" }, { "da", "Danish" }, { "dv", "Divehi" }, { "nl", "Dutch" }, { "dz", "Dzongkha" }, { "en", "English" }, { "eo", "Esperanto" }, { "et", "Estonian" }, { "ee", "Ewe" }, { "fo", "Faroese" }, { "fj", "Fijian" }, { "fi", "Finnish" }, { "fr", "French" }, { "fy", "Western Frisian" }, { "ff", "Fulah" }, { "ka", "Georgian" }, { "de", "German" }, { "gd", "Gaelic" }, { "ga", "Irish" }, { "gl", "Galician" }, { "gv", "Manx" }, { "el", "Greek" }, { "gn", "Guarani" }, { "gu", "Gujarati" }, { "ht", "Haitian" }, { "ha", "Hausa" }, { "he", "Hebrew" }, { "hz", "Herero" }, { "hi", "Hindi" }, { "ho", "Hiri Motu" }, { "hr", "Croatian" }, { "hu", "Hungarian" }, { "ig", "Igbo" }, { "is", "Icelandic" }, { "io", "Ido" }, { "ii", "Sichuan Yi" }, { "iu", "Inuktitut" }, { "ie", "Interlingue" }, { "ia", "Interlingua" }, { "id", "Indonesian" }, { "ik", "Inupiaq" }, { "it", "Italian" }, { "jv", "Javanese" }, { "ja", "Japanese" }, { "kl", "Kalaallisut" }, { "kn", "Kannada" }, { "ks", "Kashmiri" }, { "kr", "Kanuri" }, { "kk", "Kazakh" }, { "km", "Central Khmer" }, { "ki", "Kikuyu" }, { "rw", "Kinyarwanda" }, { "ky", "Kirghiz" }, { "kv", "Komi" }, { "kg", "Kongo" }, { "ko", "Korean" }, { "kj", "Kuanyama" }, { "ku", "Kurdish" }, { "lo", "Lao" }, { "la", "Latin" }, { "lv", "Latvian" }, { "li", "Limburgan" }, { "ln", "Lingala" }, { "lt", "Lithuanian" }, { "lb", "Luxembourgish" }, { "lu", "Luba-Katanga" }, { "lg", "Ganda" }, { "mk", "Macedonian" }, { "mh", "Marshallese" }, { "ml", "Malayalam" }, { "mi", "Maori" }, { "mr", "Marathi" }, { "ms", "Malay" }, { "mg", "Malagasy" }, { "mt", "Maltese" }, { "mn", "Mongolian" }, { "na", "Nauru" }, { "nv", "Navajo" }, { "nr", "Ndebele, South" }, { "nd", "Ndebele, North" }, { "ng", "Ndonga" }, { "ne", "Nepali" }, { "nn", "Norwegian" }, { "nb", "Bokmål" }, { "no", "Norwegian" }, { "ny", "Chichewa" }, { "oc", "Occitan" }, { "oj", "Ojibwa" }, { "or", "Oriya" }, { "om", "Oromo" }, { "os", "Ossetian" }, { "pa", "Panjabi" }, { "fa", "Persian" }, { "pi", "Pali" }, { "pl", "Polish" }, { "pt", "Portuguese" }, { "ps", "Pushto" }, { "qu", "Quechua" }, { "rm", "Romansh" }, { "ro", "Romanian" }, { "rn", "Rundi" }, { "ru", "Russian" }, { "sg", "Sango" }, { "sa", "Sanskrit" }, { "si", "Sinhala" }, { "sk", "Slovak" }, { "sl", "Slovenian" }, { "se", "Northern Sami" }, { "sm", "Samoan" }, { "sn", "Shona" }, { "sd", "Sindhi" }, { "so", "Somali" }, { "st", "Sotho, Southern" }, { "es", "Spanish" }, { "sc", "Sardinian" }, { "sr", "Serbian" }, { "ss", "Swati" }, { "su", "Sundanese" }, { "sw", "Swahili" }, { "sv", "Swedish" }, { "ty", "Tahitian" }, { "ta", "Tamil" }, { "tt", "Tatar" }, { "te", "Telugu" }, { "tg", "Tajik" }, { "tl", "Tagalog" }, { "th", "Thai" }, { "bo", "Tibetan" }, { "ti", "Tigrinya" }, { "to", "Tonga (Tonga Islands)" }, { "tn", "Tswana" }, { "ts", "Tsonga" }, { "tk", "Turkmen" }, { "tr", "Turkish" }, { "tw", "Twi" }, { "ug", "Uighur" }, { "uk", "Ukrainian" }, { "ur", "Urdu" }, { "uz", "Uzbek" }, { "ve", "Venda" }, { "vi", "Vietnamese" }, { "vo", "Volapük" }, { "cy", "Welsh" }, { "wa", "Walloon" }, { "wo", "Wolof" }, { "xh", "Xhosa" }, { "yi", "Yiddish" }, { "yo", "Yoruba" }, { "za", "Zhuang" }, { "zu", "Zulu" }, };

    public Dictionary<string, VideoQuality> Resolutions1 => Resolutions;

    public DownloadSettingsControl()
    {
        InitializeComponent();

        ResulotionDropDown.ItemsSource = Resolutions.Keys;
        SaveVideosFormatDropDown.ItemsSource = VideoFileTypes;
        ExtensionsDropDown.ItemsSource = FileTypes;
        CaptionsLanguagesComboBox.ItemsSource = Languages.Values;
        VideoLanguagesComboBox.ItemsSource = VideoLanguages.Values;

        var settings = GlobalConsts.DownloadSettings;
        SaveDirectoryTextBox.Text = GlobalConsts.settings.SaveDirectory;
        ExtensionsDropDown.SelectedItem = settings.SaveFormat;
        SaveVideosFormatDropDown.SelectedItem = settings.VideoSaveFormat;
        ResulotionDropDown.SelectedItem = Resolutions.FirstOrDefault(x => x.Value == settings.Quality).Key;
        PreferCheckBox.IsChecked = settings.PreferQuality;
        PreferHighestFPSCheckBox.IsChecked = settings.PreferHighestFPS;
        ConvertCheckBox.IsChecked = settings.Convert;
        BitrateCheckBox.IsChecked = settings.SetBitrate;
        BitRateTextBox.Text = string.IsNullOrWhiteSpace(settings.Bitrate) ? "192" : settings.Bitrate;
        CaptionsCheckBox.IsChecked = settings.DownloadCaptions;
        CaptionsLanguagesComboBox.SelectedItem = Languages[settings.CaptionsLanguage ?? "en"];
        AudioOnlyCheckBox.IsChecked = settings.AudioOnly;
        UniquePlaylistDirectoryCheckBox.IsChecked = settings.SavePlaylistsInDifferentDirectories;
        PlaylistIndexCheckBox.IsChecked = settings.Subset;
        PlaylistStartIndexTextBox.Text = settings.SubsetStartIndex.ToString();
        PlaylistEndIndexTextBox.Text = settings.SubsetEndIndex.ToString();
        OpenDestinationFolderCheckBox.IsChecked = settings.OpenDestinationFolderWhenDone;
        TagAudioFileCheckBox.IsChecked = settings.TagAudioFile;
        FilterByLengthCheckBox.IsChecked = settings.FilterVideosByLength;
        var FilterByLengthShorterOrLongerDropDownItemSource = new[] { FindResource("Longer"), FindResource("Shorter") };
        FilterByLengthShorterOrLongerDropDown.ItemsSource = FilterByLengthShorterOrLongerDropDownItemSource;
        FilterByLengthShorterOrLongerDropDown.SelectedItem = settings.FilterMode ? FilterByLengthShorterOrLongerDropDownItemSource[0] : FilterByLengthShorterOrLongerDropDownItemSource[1];
        FilterByLengthTextBox.Text = settings.FilterByLengthValue.ToString();
        FileNamePattenTextBox.Text = settings.FilenamePattern;
        SkipExistingCheckbox.IsChecked = settings.SkipExisting;
        SimultaneousDownloadsTextBox.Text = DownloadSettings.NormalizeSimultaneousDownloads(settings.MaxSimultaneousDownloads).ToString();
        VideoLanguagesComboBox.SelectedItem = VideoLanguages[settings.VideoLanguage ?? "default"];

        NySubscribeToEvents();

    }

    private void NySubscribeToEvents()
    {
        PreferCheckBox.Checked += NyPreferCheckBox_Checked;
        PreferCheckBox.Unchecked += NyPreferCheckBox_Unchecked;
        ResulotionDropDown.SelectionChanged += NyResulotionDropDown_SelectionChanged;
        PreferHighestFPSCheckBox.Checked += NyPreferHighestFPSCheckBox_Checked;
        PreferHighestFPSCheckBox.Unchecked += NyPreferHighestFPSCheckBox_Unchecked;
        CaptionsCheckBox.Checked += NyCaptionsCheckBox_Checked;
        CaptionsCheckBox.Unchecked += NyCaptionsCheckBox_Unchecked;
        CaptionsLanguagesComboBox.SelectionChanged += NyCaptionsLanguagesComboBox_SelectionChanged;
        ConvertCheckBox.Checked += NyConvertCheckBox_Checked;
        ConvertCheckBox.Unchecked += NyConvertCheckBox_Unchecked;
        ExtensionsDropDown.SelectionChanged += NyExtensionsDropDown_SelectionChanged;
        SaveVideosFormatDropDown.SelectionChanged += NyVideoExtensionsDropDown_SelectionChanged;
        BitrateCheckBox.Checked += NyBitrateCheckBox_Checked;
        BitrateCheckBox.Unchecked += NyBitrateCheckBox_Unchecked;
        BitRateTextBox.TextChanged += NyBitRateTextBox_TextChanged;
        AudioOnlyCheckBox.Checked += NyAudioOnlyCheckBox_Checked;
        AudioOnlyCheckBox.Unchecked += NyAudioOnlyCheckBox_Unchecked;
        UniquePlaylistDirectoryCheckBox.Checked += NyUniquePlaylistDirectoryCheckBox_Checked;
        UniquePlaylistDirectoryCheckBox.Unchecked += NyUniquePlaylistDirectoryCheckBox_Unchecked;
        PlaylistIndexCheckBox.Checked += NyPlaylistIndexCheckBox_Checked;
        PlaylistIndexCheckBox.Unchecked += NyPlaylistIndexCheckBox_Unchecked;
        PlaylistStartIndexTextBox.TextChanged += NyPlaylistStartIndexTextBox_TextChanged;
        PlaylistEndIndexTextBox.TextChanged += NyPlaylistEndIndexTextBox_TextChanged;
        OpenDestinationFolderCheckBox.Checked += NyOpenDestinationFolderCheckBox_Checked;
        OpenDestinationFolderCheckBox.Unchecked += NyOpenDestinationFolderCheckBox_Unchecked;
        TagAudioFileCheckBox.Checked += NyTagAudioFileCheckBox_Checked;
        TagAudioFileCheckBox.Unchecked += NyTagAudioFileCheckBox_Unchecked;
        FilterByLengthCheckBox.Checked += NyFilterByLengthCheckBox_Checked;
        FilterByLengthCheckBox.Unchecked += NyFilterByLengthCheckBox_Checked;
        FilterByLengthShorterOrLongerDropDown.SelectionChanged += NyFilterByLengthShorterOrLongerDropDown_SelectionChanged;
        FilterByLengthTextBox.TextChanged += NyFilterByLengthTextBox_TextChanged;
        SkipExistingCheckbox.Checked += NySkipExistingCheckBox_Checked;
        SkipExistingCheckbox.Unchecked += NySkipExistingCheckBox_Unchecked;
        SimultaneousDownloadsTextBox.TextChanged += SimultaneousDownloadsTextBox_TextChanged;
        VideoLanguagesComboBox.SelectionChanged += NyVideoLanguagesComboBox_SelectionChanged;
    }

    private void NyVideoLanguagesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.VideoLanguage = VideoLanguages.FirstOrDefault(x => x.Value.Equals((string)VideoLanguagesComboBox.SelectedItem, StringComparison.OrdinalIgnoreCase)).Key;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyFilterByLengthTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!double.TryParse(FilterByLengthTextBox.Text, out var value))
        {
            if (!string.IsNullOrWhiteSpace(FilterByLengthTextBox.Text))
                FilterByLengthTextBox.Background = GlobalConsts.ErrorBrush;

            if (GlobalConsts.settings.SaveDownloadOptions)
            {
                GlobalConsts.DownloadSettings.FilterByLengthValue = 4;
                GlobalConsts.DownloadSettings.FilterVideosByLength = false;
                GlobalConsts.NySaveDownloadSettings();
            }
        }
        else
        {
            FilterByLengthTextBox.Background = null;
            if (GlobalConsts.settings.SaveDownloadOptions)
            {
                GlobalConsts.DownloadSettings.FilterByLengthValue = value;
                GlobalConsts.NySaveDownloadSettings();
            }
        }
    }

    private void NyFilterByLengthShorterOrLongerDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.FilterMode = FilterByLengthShorterOrLongerDropDown.SelectedItem.Equals(FindResource("Longer"));
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyFilterByLengthCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.FilterVideosByLength = FilterByLengthCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyTagAudioFileCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.TagAudioFile = TagAudioFileCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyTagAudioFileCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.TagAudioFile = TagAudioFileCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyOpenDestinationFolderCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.OpenDestinationFolderWhenDone = OpenDestinationFolderCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyOpenDestinationFolderCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.OpenDestinationFolderWhenDone = OpenDestinationFolderCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyPlaylistEndIndexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!int.TryParse(PlaylistEndIndexTextBox.Text, out var endIndex))
        {
            if (!string.IsNullOrWhiteSpace(PlaylistEndIndexTextBox.Text))
                PlaylistEndIndexTextBox.Background = GlobalConsts.ErrorBrush;

            if (GlobalConsts.settings.SaveDownloadOptions)
            {
                GlobalConsts.DownloadSettings.SubsetStartIndex = 0;
                GlobalConsts.NySaveDownloadSettings();
            }
        }
        else
        {
            PlaylistEndIndexTextBox.Background = null;
            if (GlobalConsts.settings.SaveDownloadOptions)
            {
                GlobalConsts.DownloadSettings.SubsetEndIndex = endIndex;
                GlobalConsts.NySaveDownloadSettings();
            }
        }
    }

    private void NyPlaylistStartIndexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!int.TryParse(PlaylistStartIndexTextBox.Text, out var startIndex) || startIndex < 1)
        {
            PlaylistStartIndexTextBox.Background = GlobalConsts.ErrorBrush;
            if (GlobalConsts.settings.SaveDownloadOptions)
            {
                GlobalConsts.DownloadSettings.SubsetStartIndex = 0;
                GlobalConsts.NySaveDownloadSettings();
            }
        }
        else
        {
            PlaylistStartIndexTextBox.Background = null;
            if (GlobalConsts.settings.SaveDownloadOptions)
            {
                GlobalConsts.DownloadSettings.SubsetStartIndex = startIndex - 1;
                GlobalConsts.NySaveDownloadSettings();
            }
        }
    }

    private void NyPlaylistIndexCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.Subset = PlaylistIndexCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyPlaylistIndexCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.Subset = PlaylistIndexCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyUniquePlaylistDirectoryCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.DownloadSettings.SavePlaylistsInDifferentDirectories = UniquePlaylistDirectoryCheckBox.IsChecked.Value;
        if (GlobalConsts.settings.SaveDownloadOptions)
            GlobalConsts.NySaveDownloadSettings();
    }

    private void NyUniquePlaylistDirectoryCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        GlobalConsts.DownloadSettings.SavePlaylistsInDifferentDirectories = UniquePlaylistDirectoryCheckBox.IsChecked.Value;
        if (GlobalConsts.settings.SaveDownloadOptions)
            GlobalConsts.NySaveDownloadSettings();
    }

    private void NyCaptionsCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.DownloadCaptions = CaptionsCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyCaptionsCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.DownloadCaptions = CaptionsCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyCaptionsLanguagesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.CaptionsLanguage = Languages.FirstOrDefault(x => x.Value.Equals((string)CaptionsLanguagesComboBox.SelectedItem, StringComparison.OrdinalIgnoreCase)).Key;

            if (CaptionsCheckBox.IsChecked.Value && GlobalConsts.DownloadSettings.CaptionsLanguage == default)
                GlobalConsts.DownloadSettings.CaptionsLanguage = "en";

            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyPreferCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.PreferQuality = PreferCheckBox.IsChecked.Value;
            GlobalConsts.DownloadSettings.Quality = Resolutions1[(string)ResulotionDropDown.SelectedValue];
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyPreferCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.PreferQuality = PreferCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyResulotionDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.Quality = Resolutions1[(string)ResulotionDropDown.SelectedValue];
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyConvertCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.Convert = ConvertCheckBox.IsChecked.Value;
            GlobalConsts.DownloadSettings.SaveFormat = (string)ExtensionsDropDown.SelectedItem;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyConvertCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.Convert = ConvertCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyExtensionsDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.SaveFormat = (string)ExtensionsDropDown.SelectedItem;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyVideoExtensionsDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.VideoSaveFormat = (string)SaveVideosFormatDropDown.SelectedItem;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyBitrateCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.SetBitrate = BitrateCheckBox.IsChecked.Value;
            GlobalConsts.DownloadSettings.Bitrate = BitRateTextBox.Text;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyBitrateCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.SetBitrate = BitrateCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyBitRateTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.Bitrate = BitRateTextBox.Text;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyPreferHighestFPSCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.PreferHighestFPS = PreferHighestFPSCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyPreferHighestFPSCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.PreferHighestFPS = PreferHighestFPSCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyAudioOnlyCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.AudioOnly = AudioOnlyCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyAudioOnlyCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.AudioOnly = AudioOnlyCheckBox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NyTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        dialog.RootFolder = Environment.SpecialFolder.Desktop;
        var result = dialog.ShowDialog();
        if (result == DialogResult.OK)
        {
            SaveDirectoryTextBox.Text = dialog.SelectedPath;
            GlobalConsts.settings.SaveDirectory = SaveDirectoryTextBox.Text;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NySaveDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var dir = SaveDirectoryTextBox.Text;
        if (Directory.Exists(dir))
        {
            GlobalConsts.settings.SaveDirectory = dir;
            GlobalConsts.NySaveDownloadSettings();
            SaveDirectoryTextBox.Background = null;
        }
        else
            SaveDirectoryTextBox.Background = GlobalConsts.ErrorBrush;

    }

    private void NyTile_Click(object sender, RoutedEventArgs e)
    {
        NyTextBox_MouseDoubleClick(sender, null);
    }

    private void NyRestPatternToDefault_Click(object sender, RoutedEventArgs e)
    {
        GlobalConsts.DownloadSettings.FilenamePattern = "$title";
        FileNamePattenTextBox.Text = GlobalConsts.DownloadSettings.FilenamePattern;
        GlobalConsts.NySaveDownloadSettings();
    }

    private void NyFileNamePattenTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        GlobalConsts.DownloadSettings.FilenamePattern = FileNamePattenTextBox.Text;
        GlobalConsts.NySaveDownloadSettings();
    }

    private void NySkipExistingCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.SkipExisting = SkipExistingCheckbox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void NySkipExistingCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GlobalConsts.settings.SaveDownloadOptions)
        {
            GlobalConsts.DownloadSettings.SkipExisting = SkipExistingCheckbox.IsChecked.Value;
            GlobalConsts.NySaveDownloadSettings();
        }
    }

    private void SimultaneousDownloadsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!int.TryParse(SimultaneousDownloadsTextBox.Text, out var value))
            return;
        var limit = DownloadSettings.NormalizeSimultaneousDownloads(value);
        GlobalConsts.DownloadSettings.MaxSimultaneousDownloads = limit;
        DownloadGate.Limit = limit;
        GlobalConsts.NySaveDownloadSettings();
    }

}
