namespace BreadBoard.Tests;

/// <summary>Writes small 16-bit PCM WAV files for tests.</summary>
internal static class TestWav
{
    public static string Write(string path, double seconds = 0.5, int rate = 44100, int channels = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var frames = (int)(seconds * rate);
        using var w = new BinaryWriter(File.Create(path));
        var data = frames * channels * 2;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        for (var i = 0; i < frames; i++)
        {
            var s = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 12000);
            for (var c = 0; c < channels; c++) w.Write(s);
        }
        return path;
    }
}
