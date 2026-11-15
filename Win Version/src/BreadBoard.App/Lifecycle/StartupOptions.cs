namespace BreadBoard.App.Lifecycle;

/// <summary>Command-line switches. Normal users pass none.</summary>
/// <param name="DataDir">Keep config, sounds and logs in this folder (tests, portable use).</param>
/// <param name="SmokeTest">Start, visit every page, shut down cleanly and write a result file.</param>
/// <param name="PerfSeconds">Measure startup and idle CPU/RAM for this many seconds, write a report, then exit.</param>
/// <param name="ScreenshotDir">Render every page and the mini-player to PNG files here, then exit.</param>
public sealed record StartupOptions(string? DataDir, bool SmokeTest, int? PerfSeconds, string? ScreenshotDir = null)
{
    public bool IsAutomated => SmokeTest || PerfSeconds is not null || ScreenshotDir is not null;

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? dataDir = null, shots = null;
        int? perf = null;
        var smoke = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--data-dir" when i + 1 < args.Count: dataDir = args[++i]; break;
                case "--smoke-test": smoke = true; break;
                case "--perf" when i + 1 < args.Count && int.TryParse(args[i + 1], out var s): perf = s; i++; break;
                case "--screenshots" when i + 1 < args.Count: shots = args[++i]; break;
            }
        }
        return new StartupOptions(dataDir, smoke, perf, shots);
    }
}
