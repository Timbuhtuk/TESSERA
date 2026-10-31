namespace PixelArtAseprite;

/// <summary>Monotonic, thread-safe progress ranges. Fractions describe completed work, not time.</summary>
internal sealed class ProcessingProgress : IProgress<double>
{
    private readonly IProgress<double>? _target;
    private readonly double _start;
    private readonly double _length;
    private readonly object _sync = new();
    private double _last = -1;

    public ProcessingProgress(IProgress<double>? target) : this(target, 0, 1) { }

    private ProcessingProgress(IProgress<double>? target, double start, double length)
    {
        _target = target;
        _start = start;
        _length = length;
    }

    public ProcessingProgress Slice(int index, int count)
    {
        if (count <= 0 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return Range((double)index / count, 1.0 / count);
    }

    public ProcessingProgress Range(double start, double length)
    {
        if (!double.IsFinite(start) || !double.IsFinite(length) || start < 0 || length < 0 || start + length > 1.00000001)
            throw new ArgumentOutOfRangeException(nameof(start));
        return new ProcessingProgress(_target is null ? null : this, start, length);
    }

    public void Report(double value)
    {
        if (_target is null || !double.IsFinite(value)) return;
        value = Math.Clamp(value, 0, 1);
        lock (_sync)
        {
            if (value <= _last) return;
            _last = value;
            _target.Report(_start + _length * value);
        }
    }
}
