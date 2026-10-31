using System.Diagnostics;
using System.Windows.Threading;
using PixelArtDownscale;

namespace DomainColorTest;

/// <summary>One operation's UI stream. Superseded and disposed streams cannot revive an indicator.</summary>
public sealed class OperationProgressSession : IProgress<double>, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<OperationProgressSession, double, bool?>? _update;
    private readonly object _sync = new();
    private double _value;
    private long _lastPost;
    private bool _ended;

    public object? Owner { get; init; }
    public bool IsPreview { get; init; }
    public bool IsFinished { get { lock (_sync) return _ended; } }
    public ProcessingProgress Work { get; }

    public OperationProgressSession(Dispatcher dispatcher, Action<OperationProgressSession, double, bool?>? update = null)
    {
        _dispatcher = dispatcher;
        _update = update;
        Work = new ProcessingProgress(this).Range(0, .95);
    }

    public void Report(double value)
    {
        if (!double.IsFinite(value)) return;
        lock (_sync)
        {
            if (_ended) return;
            value = Math.Clamp(value, 0, .99);
            if (value < _value) return;
            _value = value;
            long now = Stopwatch.GetTimestamp();
            if (_lastPost != 0 && Stopwatch.GetElapsedTime(_lastPost, now).TotalMilliseconds < 50) return;
            _lastPost = now;
            Post(value, null);
        }
    }

    public void Complete()
    {
        lock (_sync)
        {
            if (_ended) return;
            _ended = true;
            Post(1, true);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_ended) return;
            _ended = true;
            Post(_value, false);
        }
    }

    private void Post(double value, bool? terminal)
    {
        if (_update is null || _dispatcher.HasShutdownStarted) return;
        // Enqueue under the same lock as Report/Complete so worker updates cannot overtake termination.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _update(this, value, terminal)));
    }
}
