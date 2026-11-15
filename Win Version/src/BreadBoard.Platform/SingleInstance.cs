using System.Security.Cryptography;
using System.Text;

namespace BreadBoard.Platform;

/// <summary>
/// Ensures one running instance per data folder. A second launch signals the first to come to the
/// front and exits. Waiting uses a registered OS wait (no polling thread).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    /// <summary>Returns the instance guard, or null if another instance owns <paramref name="dataDir"/> (it has been told to activate).</summary>
    public static SingleInstance? TryAcquire(string dataDir)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).ToUpperInvariant())))[..16];
        var mutex = new Mutex(true, $@"Local\BreadBoard-{key}", out var owned);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\BreadBoard-{key}-activate");
        if (!owned)
        {
            activate.Set();
            activate.Dispose();
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, activate);
    }

    /// <summary>Invokes <paramref name="onActivate"/> (on a thread-pool thread) whenever another launch is attempted.</summary>
    public void ListenForActivation(Action onActivate) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
