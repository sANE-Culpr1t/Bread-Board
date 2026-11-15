namespace BreadBoard.Audio.Dsp;

/// <summary>
/// Gain + mute with click-free ramping, and a peak meter the UI can poll.
/// Settings are written by the UI thread and read by the audio thread (plain volatile fields).
/// </summary>
public sealed class ChannelStrip
{
    private volatile float _targetGain = 1f;
    private volatile bool _muted;
    private float _currentGain = 1f;
    private float _peak;

    /// <summary>Linear gain, 0–1 (UI percent / 100).</summary>
    public float Gain { get => _targetGain; set => _targetGain = Math.Clamp(value, 0f, 2f); }
    public bool Muted { get => _muted; set => _muted = value; }

    /// <summary>Highest absolute sample since the last call (0–1+). Resets on read.</summary>
    public float TakePeak() => Interlocked.Exchange(ref _peak, 0f);

    /// <summary>Applies gain in place to interleaved samples and updates the meter.</summary>
    public void Process(Span<float> samples)
    {
        var target = _muted ? 0f : _targetGain;
        var g = _currentGain;
        var step = samples.Length > 0 ? (target - g) / samples.Length : 0f;
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            g += step;
            var v = samples[i] * g;
            samples[i] = v;
            var a = MathF.Abs(v);
            if (a > peak) peak = a;
        }
        _currentGain = target;
        UpdatePeak(peak);
    }

    private void UpdatePeak(float value)
    {
        float cur;
        do
        {
            cur = Volatile.Read(ref _peak);
            if (value <= cur) return;
        } while (Interlocked.CompareExchange(ref _peak, value, cur) != cur);
    }
}
