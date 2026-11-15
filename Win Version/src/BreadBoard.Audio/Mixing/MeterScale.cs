namespace BreadBoard.Audio.Mixing;

/// <summary>How level meters read a peak.</summary>
public static class MeterScale
{
    /// <summary>Peak (linear) → 0–1 on a −60…0 dB scale, so quiet voices still move the meter.</summary>
    public static double Of(float peak) => peak <= 1e-6f ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
}
