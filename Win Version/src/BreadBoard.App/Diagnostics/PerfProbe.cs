using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using BreadBoard.Core;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Diagnostics;

/// <summary>
/// --perf N: measures time to first frame, then samples CPU and memory while the app sits idle
/// for N seconds, writes perf-report.json and closes. Only runs when requested.
/// </summary>
public static class PerfProbe
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static async Task RunAsync(Window window, int idleSeconds)
    {
        var rendered = new TaskCompletionSource();
        window.ContentRendered += (_, _) => rendered.TrySetResult();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);
        var proc = Process.GetCurrentProcess();
        var startupMs = (DateTime.Now - proc.StartTime).TotalMilliseconds;

        await Task.Delay(1500).ConfigureAwait(true); // let startup work settle
        GC.Collect();
        proc.Refresh();
        var cpu0 = proc.TotalProcessorTime;
        var sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(idleSeconds)).ConfigureAwait(true);
        proc.Refresh();
        var cpuPercent = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;

        var report = new
        {
            startupMs = Math.Round(startupMs),
            idleSeconds,
            idleCpuPercent = Math.Round(cpuPercent, 3),
            workingSetMb = Math.Round(proc.WorkingSet64 / 1048576.0, 1),
            privateMb = Math.Round(proc.PrivateMemorySize64 / 1048576.0, 1),
            managedHeapMb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
            threads = proc.Threads.Count,
            build = typeof(PerfProbe).Assembly.GetName().Version?.ToString(3),
#if DEBUG
            configuration = "Debug",
#else
            configuration = "Release",
#endif
        };
        var json = JsonSerializer.Serialize(report, Indented);
        await File.WriteAllTextAsync(Path.Combine(AppPaths.Roaming, "perf-report.json"), json).ConfigureAwait(true);
        Log.Info($"Perf: {JsonSerializer.Serialize(report)}");
        window.Close();
    }
}
