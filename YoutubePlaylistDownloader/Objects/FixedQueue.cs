namespace YoutubePlaylistDownloader.Objects;

class FixedQueue<T>(int size) : Queue<T>()
{
    private readonly int size = size;

    public void NyEnqueue(T item)
    {
        while (Count >= size)
            Dequeue();

        base.Enqueue(item);
    }

}
