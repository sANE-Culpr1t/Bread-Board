namespace BreadBoard.Audio.Dsp;

// Simple, allocation-free dynamics processors for interleaved stereo at 48 kHz.
// Parameters are volatile so the UI thread can change them while audio runs.

internal static class Coef
{
    /// <summary>One-pole smoothing coefficient for a time constant in ms.</summary>
    public static float For(float ms) => 1f - MathF.Exp(-1f / (AudioFormat.SampleRate * ms / 1000f));
}

/// <summary>Closes the mic below a threshold, with hold and soft attack/release to avoid chatter.</summary>
public sealed class NoiseGate
{
    private volatile bool _enabled;
    private volatile float _threshold = 0.12f;
    private float _env, _gain = 1f;
    private int _holdLeft;
    private static readonly float EnvAttack = Coef.For(1), EnvRelease = Coef.For(60);
    private static readonly float Open = Coef.For(2), Close = Coef.For(80);
    private const int HoldFrames = AudioFormat.SampleRate * 150 / 1000;

    public bool Enabled { get => _enabled; set => _enabled = value; }
    /// <summary>Linear amplitude threshold, 0–1.</summary>
    public float Threshold { get => _threshold; set => _threshold = Math.Clamp(value, 0f, 1f); }

    public void Process(Span<float> s)
    {
        if (!_enabled) { _gain = 1f; return; }
        var th = _threshold;
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var a = MathF.Max(MathF.Abs(s[i]), MathF.Abs(s[i + 1]));
            _env += (a - _env) * (a > _env ? EnvAttack : EnvRelease);
            if (_env >= th) _holdLeft = HoldFrames;
            else if (_holdLeft > 0) _holdLeft--;
            var target = _holdLeft > 0 ? 1f : 0f;
            _gain += (target - _gain) * (target > _gain ? Open : Close);
            s[i] *= _gain;
            s[i + 1] *= _gain;
        }
    }
}

/// <summary>Feed-forward peak compressor with fixed threshold (-18 dBFS); amount sets the ratio.</summary>
public sealed class Compressor
{
    private volatile bool _enabled;
    private volatile float _amount = 0.4f;
    private float _env;
    private static readonly float Attack = Coef.For(5), Release = Coef.For(120);
    private const float ThresholdDb = -18f;

    public bool Enabled { get => _enabled; set => _enabled = value; }
    /// <summary>0–1; 1 ≈ 8:1 with make-up gain.</summary>
    public float Amount { get => _amount; set => _amount = Math.Clamp(value, 0f, 1f); }

    public void Process(Span<float> s)
    {
        if (!_enabled) return;
        var ratio = 1f + _amount * 7f;
        var makeup = DbToLin(-ThresholdDb * (1f - 1f / ratio) * 0.5f);
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var a = MathF.Max(MathF.Abs(s[i]), MathF.Abs(s[i + 1]));
            _env += (a - _env) * (a > _env ? Attack : Release);
            var gain = 1f;
            if (_env > 1e-6f)
            {
                var db = LinToDb(_env);
                if (db > ThresholdDb) gain = DbToLin((ThresholdDb + (db - ThresholdDb) / ratio) - db);
            }
            gain *= makeup;
            s[i] *= gain;
            s[i + 1] *= gain;
        }
    }

    private static float LinToDb(float v) => 20f * MathF.Log10(v);
    private static float DbToLin(float db) => MathF.Pow(10f, db / 20f);
}

/// <summary>Brick-wall-ish limiter: instant attack, smooth release, final hard clip at the ceiling.</summary>
public sealed class Limiter
{
    private volatile bool _enabled = true;
    private volatile float _ceiling = 0.92f;
    private float _gain = 1f;
    private static readonly float Release = Coef.For(80);

    public bool Enabled { get => _enabled; set => _enabled = value; }
    public float Ceiling { get => _ceiling; set => _ceiling = Math.Clamp(value, 0.1f, 1f); }

    public void Process(Span<float> s)
    {
        var ceil = _enabled ? _ceiling : 1f;
        for (var i = 0; i + 1 < s.Length; i += 2)
        {
            var a = MathF.Max(MathF.Abs(s[i]), MathF.Abs(s[i + 1]));
            var needed = a * _gain > ceil ? ceil / a : 1f;
            _gain = needed < _gain ? needed : _gain + (1f - _gain) * Release;
            s[i] = Math.Clamp(s[i] * _gain, -ceil, ceil);
            s[i + 1] = Math.Clamp(s[i + 1] * _gain, -ceil, ceil);
        }
    }
}

/// <summary>Lowers a signal while a side-chain (the mic) is active.</summary>
public sealed class Ducker
{
    private volatile bool _enabled;
    private volatile float _amount = 0.5f;
    private float _gain = 1f, _env;
    private static readonly float Env = Coef.For(30), Down = Coef.For(40), Up = Coef.For(400);
    private const float SpeakingThreshold = 0.05f;

    public bool Enabled { get => _enabled; set => _enabled = value; }
    /// <summary>0–1 reduction while speaking.</summary>
    public float Amount { get => _amount; set => _amount = Math.Clamp(value, 0f, 1f); }

    public void Process(Span<float> target, ReadOnlySpan<float> sideChain)
    {
        if (!_enabled) { _gain = 1f; return; }
        var duck = 1f - _amount;
        for (var i = 0; i + 1 < target.Length && i + 1 < sideChain.Length; i += 2)
        {
            var a = MathF.Max(MathF.Abs(sideChain[i]), MathF.Abs(sideChain[i + 1]));
            _env += (a - _env) * Env;
            var g = _env > SpeakingThreshold ? duck : 1f;
            _gain += (g - _gain) * (g < _gain ? Down : Up);
            target[i] *= _gain;
            target[i + 1] *= _gain;
        }
    }
}
