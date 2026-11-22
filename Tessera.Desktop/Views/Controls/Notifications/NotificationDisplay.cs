using System.Windows;
using System.Windows.Controls;

namespace Tessera.Desktop;

public sealed partial class NotificationDisplay : Grid
{
    private const int MaxVisible = 4;
    private readonly StackPanel _column = new() { Margin = new Thickness(14, 10, 14, 10) };
    private readonly List<Entry> _entries = [];
    private Entry? _status;

    internal IEnumerable<NotificationCard> Cards => _entries.Select(entry => entry.Card);

    public NotificationDisplay()
    {
        ClipToBounds = true;
        Children.Add(_column);
        Place(NotificationPlacement.TopRight);
        Unloaded += (_, _) => Clear();
    }

    internal void Place(NotificationPlacement position)
    {
        var horizontal = position switch
        {
            NotificationPlacement.TopLeft or NotificationPlacement.MiddleLeft or NotificationPlacement.BottomLeft => HorizontalAlignment.Left,
            NotificationPlacement.TopCenter or NotificationPlacement.BottomCenter => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right
        };
        var vertical = position switch
        {
            NotificationPlacement.MiddleLeft or NotificationPlacement.MiddleRight => VerticalAlignment.Center,
            NotificationPlacement.BottomLeft or NotificationPlacement.BottomCenter or NotificationPlacement.BottomRight => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Top
        };
        if (_column.HorizontalAlignment == horizontal && _column.VerticalAlignment == vertical) return;
        _column.HorizontalAlignment = horizontal;
        _column.VerticalAlignment = vertical;
        for (int q = _entries.Count - 1; q >= 0; q--)
        {
            var entry = _entries[q];
            if (entry.IsExiting) Remove(entry);
            else entry.StopSlide();
        }
    }

    internal void Show(string message, NotificationKind kind = NotificationKind.Information)
    {
        if (!string.IsNullOrWhiteSpace(message)) Add(message, kind);
    }

    internal void UpdateStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            if (_status is not null) Remove(_status);
            return;
        }
        if (_status is null) _status = Add(message, NotificationKind.Information);
        else _status.Show(message, NotificationKind.Information);
    }

    internal void Clear()
    {
        for (int q = _entries.Count - 1; q >= 0; q--) Remove(_entries[q]);
    }

    private Entry Add(string message, NotificationKind kind)
    {
        if (_entries.Count >= MaxVisible) Remove(_entries[0]);
        var entry = new Entry(this);
        _entries.Add(entry);
        _column.Children.Add(entry.Card);
        entry.Show(message, kind);
        return entry;
    }

    private void Remove(Entry entry)
    {
        entry.Stop();
        _column.Children.Remove(entry.Card);
        _entries.Remove(entry);
        if (ReferenceEquals(_status, entry)) _status = null;
    }
}
