namespace YoutubePlaylistDownloader.Objects;

public sealed class QueueFileItem : INotifyPropertyChanged
{
    private string status = "";
    private string fileName = "";

    public string Id { get; init; }
    public int Number { get; init; }
    public string Title { get; init; }
    public string FileName
    {
        get => fileName;
        set
        {
            if (fileName == value)
                return;
            fileName = value ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    public string Status
    {
        get => status;
        set
        {
            if (status == value)
                return;
            status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    public string Display => $"{Number:00}  {Title}    {FileName}    [{Status}]";

    public event PropertyChangedEventHandler PropertyChanged;
}
