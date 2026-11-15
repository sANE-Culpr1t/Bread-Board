namespace BreadBoard.App.Shell;

/// <summary>Command-line switches. Normal users pass none.</summary>
/// <param name="DataDir">Keep config, sounds and logs in this folder (tests, portable use).</param>
/// <param name="SmokeTest">Start, visit every page, shut down cleanly and write a result file.</param>
/// <param name="PerfSeconds">Measure startup and idle CPU/RAM for this many seconds, write a report, then exit.</param>
/// <param name="ScreenshotDir">Render every page and the mini-player to PNG files here, then exit.</param>
/// <param name="IconPath">Write the app icon (.ico) rendered from the logo here and exit, without a window.</param>
/// <param name="IconPngDir">With <paramref name="IconPath"/>: also write each icon size as a PNG here.</param>
public sealed record StartupOptions(string? DataDir, bool SmokeTest, int? PerfSeconds, string? ScreenshotDir = null, string? IconPath = null, string? IconPngDir = null)
{
    public bool IsAutomated => SmokeTest || PerfSeconds is not null || ScreenshotDir is not null;

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? dataDir = null, shots = null, icon = null, iconPng = null;
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
                case "--export-icon" when i + 1 < args.Count: icon = args[++i]; break;
                case "--export-png" when i + 1 < args.Count: iconPng = args[++i]; break;
            }
        }
        return new StartupOptions(dataDir, smoke, perf, shots, icon, iconPng);
    }
}
