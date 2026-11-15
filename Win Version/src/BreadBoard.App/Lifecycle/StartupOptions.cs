namespace BreadBoard.App.Lifecycle;

/// <summary>Command-line switches. Normal users pass none.</summary>
/// <param name="DataDir">Keep config, sounds and logs in this folder (tests, portable use).</param>
/// <param name="SmokeTest">Start, visit every page, shut down cleanly and write a result file.</param>
/// <param name="PerfSeconds">Measure startup and idle CPU/RAM for this many seconds, write a report, then exit.</param>
public sealed record StartupOptions(string? DataDir, bool SmokeTest, int? PerfSeconds)
{
    public bool IsAutomated => SmokeTest || PerfSeconds is not null;

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? dataDir = null;
        int? perf = null;
        var smoke = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--data-dir" when i + 1 < args.Count: dataDir = args[++i]; break;
                case "--smoke-test": smoke = true; break;
                case "--perf" when i + 1 < args.Count && int.TryParse(args[i + 1], out var s): perf = s; i++; break;
            }
        }
        return new StartupOptions(dataDir, smoke, perf);
    }
}
