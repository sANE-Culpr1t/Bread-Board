using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BreadBoard.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Structured, non-blocking logger. Each entry is one JSON object per line
/// (<c>{"t","lvl","cat","msg","thread","ex"}</c>) in <c>breadboard-yyyyMMdd.jsonl</c>.
/// The category is the calling source file, so call sites stay one-liners.
/// Callers only enqueue (safe on audio threads); a background task writes in batches.
/// The writer task only exists after <see cref="Init"/> and sleeps when idle (no timers).
/// </summary>
public static class Log
{
    private readonly record struct Entry(DateTimeOffset Time, LogLevel Level, string Category, string Message, int Thread, Exception? Error);

    private static readonly ConcurrentQueue<Entry> Queue = new();
    private static readonly SemaphoreSlim Signal = new(0);
    private static readonly ManualResetEventSlim Idle = new(true);
    private static string? _dir;
    private static Task? _writer;


    /// <summary>Also echo human-readable lines to this sink (e.g. Console.Error).</summary>
    public static Action<string>? Echo { get; set; }


    public static void Init(string directory)
    {
        _dir = directory;
        System.IO.Directory.CreateDirectory(directory);
        Prune(directory);
        _writer ??= Task.Run(WriteLoop);
    }

    public static void Debug(string msg, [CallerFilePath] string file = "") => Write(LogLevel.Debug, msg, null, file);
    public static void Info(string msg, [CallerFilePath] string file = "") => Write(LogLevel.Info, msg, null, file);
    public static void Warn(string msg, Exception? ex = null, [CallerFilePath] string file = "") => Write(LogLevel.Warn, msg, ex, file);
    public static void Error(string msg, Exception? ex = null, [CallerFilePath] string file = "") => Write(LogLevel.Error, msg, ex, file);

    private static void Write(LogLevel level, string msg, Exception? ex, string file)
    {
        if (level < LogLevel.Info) return;
        var e = new Entry(DateTimeOffset.Now, level, Path.GetFileNameWithoutExtension(file), msg, Environment.CurrentManagedThreadId, ex);
        Echo?.Invoke($"{e.Time:HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} [{e.Category}] {msg}{(ex is null ? "" : $" | {ex.GetType().Name}: {ex.Message}")}");
        Idle.Reset();
        Queue.Enqueue(e);
        Signal.Release();
    }

    /// <summary>Blocks until queued entries are on disk (call on shutdown). Bounded wait.</summary>
    public static void Flush(int timeoutMs = 2000)
    {
        if (_writer is null) return;
        Signal.Release();
        Idle.Wait(timeoutMs);
    }

    private static async Task WriteLoop()
    {
        using var ms = new MemoryStream();
        while (true)
        {
            await Signal.WaitAsync().ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false); // batch bursts
            ms.SetLength(0);
            using (var w = new Utf8JsonWriter(ms))
            {
                while (Queue.TryDequeue(out var e))
                {
                    w.WriteStartObject();
                    w.WriteString("t", e.Time);
                    w.WriteString("lvl", e.Level.ToString().ToLowerInvariant());
                    w.WriteString("cat", e.Category);
                    w.WriteString("msg", e.Message);
                    w.WriteNumber("thread", e.Thread);
                    if (e.Error is not null) w.WriteString("ex", e.Error.ToString());
                    w.WriteEndObject();
                    w.Flush();
                    ms.WriteByte((byte)'\n');
                    w.Reset(ms);
                }
            }
            if (_dir is not null && ms.Length > 0)
            {
                try
                {
                    await using var f = new FileStream(Path.Combine(_dir, $"breadboard-{DateTime.Now:yyyyMMdd}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    ms.Position = 0;
                    await ms.CopyToAsync(f).ConfigureAwait(false);
                }
                catch (IOException) { /* disk issue: drop rather than crash */ }
            }
            if (Queue.IsEmpty) Idle.Set();
        }
    }

    private static void Prune(string dir)
    {
        try
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(dir, "breadboard-*.*"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // old logs stay; harmless
    }
}
