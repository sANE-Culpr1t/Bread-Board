using System.Collections.Concurrent;
using System.Text;

namespace BreadBoard.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Minimal non-blocking file logger. Callers (including audio threads) only enqueue;
/// a background task writes. One file per day, kept for 7 days.
/// </summary>
public static class Log
{
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly SemaphoreSlim Signal = new(0);
    private static string? _dir;
    private static Task? _writer;

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    /// <summary>Also echo entries to this sink (e.g. Console.Error for the CLI).</summary>
    public static Action<string>? Echo { get; set; }

    public static void Init(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(directory);
        Prune(directory);
        _writer ??= Task.Run(WriteLoop);
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg, null);
    public static void Info(string msg) => Write(LogLevel.Info, msg, null);
    public static void Warn(string msg, Exception? ex = null) => Write(LogLevel.Warn, msg, ex);
    public static void Error(string msg, Exception? ex = null) => Write(LogLevel.Error, msg, ex);

    private static void Write(LogLevel level, string msg, Exception? ex)
    {
        if (level < MinLevel) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] {msg}";
        if (ex is not null) line += $" | {ex.GetType().Name}: {ex.Message}";
        Echo?.Invoke(line);
        Queue.Enqueue(ex is null ? line : line + Environment.NewLine + ex);
        Signal.Release();
    }

    /// <summary>Waits until queued entries are written (call on shutdown).</summary>
    public static void Flush()
    {
        for (var i = 0; i < 50 && !Queue.IsEmpty; i++) Thread.Sleep(10);
    }

    private static async Task WriteLoop()
    {
        var sb = new StringBuilder();
        while (true)
        {
            await Signal.WaitAsync().ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false); // batch bursts
            sb.Clear();
            while (Queue.TryDequeue(out var line)) sb.AppendLine(line);
            if (_dir is null || sb.Length == 0) continue;
            try
            {
                await File.AppendAllTextAsync(Path.Combine(_dir, $"breadboard-{DateTime.Now:yyyyMMdd}.log"), sb.ToString()).ConfigureAwait(false);
            }
            catch (IOException) { /* disk issue: drop rather than crash */ }
        }
    }

    private static void Prune(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "breadboard-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
        }
        catch (IOException) { }
    }
}
