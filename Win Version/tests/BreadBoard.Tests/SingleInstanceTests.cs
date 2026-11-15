using BreadBoard.Platform;

namespace BreadBoard.Tests;

public class SingleInstanceTests
{
    [Fact]
    public async Task Second_acquire_fails_and_signals_the_first()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bb-single-" + Guid.NewGuid().ToString("N"));
        var activated = new TaskCompletionSource();
        using var first = SingleInstance.TryAcquire(dir);
        Assert.NotNull(first);
        first!.ListenForActivation(() => activated.TrySetResult());

        // A second launch runs on another thread (mutex ownership is per thread).
        var second = await Task.Run(() => SingleInstance.TryAcquire(dir));
        Assert.Null(second);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
