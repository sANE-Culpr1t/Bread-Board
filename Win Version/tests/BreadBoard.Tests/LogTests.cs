using System.Text.Json;
using BreadBoard.Core.Logging;

namespace BreadBoard.Tests;

public class LogTests
{
    [Fact]
    public void Writes_json_lines_with_category()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bb-log-" + Guid.NewGuid().ToString("N"));
        Log.Init(dir);
        Log.Info("hello from test");
        Log.Error("boom", new InvalidOperationException("bad"));
        Log.Flush();
        var lines = Directory.GetFiles(dir, "*.jsonl").SelectMany(File.ReadAllLines).ToList();
        var hello = lines.Select(l => JsonDocument.Parse(l).RootElement).First(e => e.GetProperty("msg").GetString() == "hello from test");
        Assert.Equal("LogTests", hello.GetProperty("cat").GetString());
        Assert.Contains(lines, l => l.Contains("InvalidOperationException", StringComparison.Ordinal));
    }
}
