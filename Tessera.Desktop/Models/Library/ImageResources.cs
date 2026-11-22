using System.IO;
using System.Windows.Media.Imaging;

namespace Tessera.Desktop;

internal readonly record struct ThumbnailResource(ImageResources Cache, string Path, int Width = 112, int Height = 112, string? Fallback = null)
{
    public BitmapSource? Get() => Cache.Get(Path, Width, Height) ??
        (Fallback is { } path ? Cache.Get(path, Width, Height) : null);
}

internal sealed class ImageResources
{
    internal const int MaxEntries = 256;
    internal const long MaxBytes = 8 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _recent = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _thumbnails = [];
    private long _bytes, _revision;

    private readonly record struct Key(string Path, int Width, int Height);
    private sealed record Entry(Key Key, BitmapSource Image, long Bytes);

    internal (int Count, long Bytes) Usage { get { lock (_gate) return (_thumbnails.Count, _bytes); } }
    internal bool Contains(string path) { lock (_gate) return _thumbnails.Keys.Any(key => key.Path == path); }

    public BitmapSource? Get(string path, int width, int height)
    {
        var key = new Key(path, width, height);
        long revision;
        lock (_gate)
        {
            if (Find(key) is { } cached) return cached;
            revision = _revision;
        }

        BitmapSource image;
        try { image = Decode(path, width, height); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or
            ArgumentException or System.Runtime.InteropServices.COMException)
        { return null; }

        long stride = ((long)image.PixelWidth * Math.Max(32, image.Format.BitsPerPixel) + 7) / 8;
        long bytes = stride * image.PixelHeight;
        lock (_gate)
        {
            if (Find(key) is { } cached) return cached;
            if (revision != _revision || bytes > MaxBytes) return image;
            var node = _recent.AddFirst(new Entry(key, image, bytes));
            _thumbnails.Add(key, node);
            _bytes += bytes;
            while (_bytes > MaxBytes || _thumbnails.Count > MaxEntries) Remove(_recent.Last!);
        }
        return image;
    }

    public void Invalidate(string path) => InvalidatePaths(key => key == path);

    public void InvalidateDirectory(string directory)
    {
        string prefix = directory + Path.DirectorySeparatorChar;
        InvalidatePaths(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void InvalidatePaths(Func<string, bool> matches)
    {
        lock (_gate)
        {
            _revision++;
            foreach (var key in _thumbnails.Keys.Where(key => matches(key.Path)).ToArray()) Remove(_thumbnails[key]);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _revision++;
            _thumbnails.Clear();
            _recent.Clear();
            _bytes = 0;
        }
    }

    private BitmapSource? Find(Key key)
    {
        if (!_thumbnails.TryGetValue(key, out var node)) return null;
        _recent.Remove(node);
        _recent.AddFirst(node);
        return node.Value.Image;
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _recent.Remove(node);
        _thumbnails.Remove(node.Value.Key);
        _bytes -= node.Value.Bytes;
    }

    private static BitmapSource Decode(string path, int width, int height)
    {
        using var stream = File.OpenRead(path);
        var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        int originalWidth = header.PixelWidth, originalHeight = header.PixelHeight;
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (originalWidth > width || originalHeight > height)
        {
            if ((long)originalWidth * height >= (long)originalHeight * width) image.DecodePixelWidth = width;
            else image.DecodePixelHeight = height;
        }
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
