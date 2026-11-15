namespace BreadBoard.Audio.Dsp;

/// <summary>
/// Lock-free single-producer / single-consumer ring buffer of float samples.
/// The capture thread writes, the render thread reads. No allocation after construction.
/// </summary>
public sealed class FloatRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private long _write; // only the producer advances
    private long _read;  // only the consumer advances

    public FloatRingBuffer(int minCapacity)
    {
        var cap = 1;
        while (cap < minCapacity) cap <<= 1;
        _buffer = new float[cap];
        _mask = cap - 1;
    }

    public int Capacity => _buffer.Length;
    public int Count => (int)(Volatile.Read(ref _write) - Volatile.Read(ref _read));

    /// <summary>Producer: writes as much as fits; excess is dropped (consumer is stalled).</summary>
    public int Write(ReadOnlySpan<float> data)
    {
        var w = _write;
        var free = _buffer.Length - (int)(w - Volatile.Read(ref _read));
        var n = Math.Min(free, data.Length);
        var start = (int)(w & _mask);
        var first = Math.Min(n, _buffer.Length - start);
        data[..first].CopyTo(_buffer.AsSpan(start));
        data[first..n].CopyTo(_buffer.AsSpan(0));
        Volatile.Write(ref _write, w + n);
        return n;
    }

    /// <summary>Producer: writes <paramref name="count"/> zero samples.</summary>
    public int WriteSilence(int count)
    {
        var w = _write;
        var free = _buffer.Length - (int)(w - Volatile.Read(ref _read));
        var n = Math.Min(free, count);
        var start = (int)(w & _mask);
        var first = Math.Min(n, _buffer.Length - start);
        _buffer.AsSpan(start, first).Clear();
        _buffer.AsSpan(0, n - first).Clear();
        Volatile.Write(ref _write, w + n);
        return n;
    }

    /// <summary>Consumer: reads up to dest.Length samples; returns how many were read.</summary>
    public int Read(Span<float> dest)
    {
        var r = _read;
        var n = Math.Min(dest.Length, (int)(Volatile.Read(ref _write) - r));
        var start = (int)(r & _mask);
        var first = Math.Min(n, _buffer.Length - start);
        _buffer.AsSpan(start, first).CopyTo(dest);
        _buffer.AsSpan(0, n - first).CopyTo(dest[first..]);
        Volatile.Write(ref _read, r + n);
        return n;
    }

    /// <summary>Consumer: discards up to <paramref name="count"/> samples (used to cap latency).</summary>
    public void Skip(int count)
    {
        var r = _read;
        var n = Math.Min(count, (int)(Volatile.Read(ref _write) - r));
        Volatile.Write(ref _read, r + n);
    }
}
