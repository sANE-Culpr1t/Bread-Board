using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.Core.Config;

/// <summary>
/// Owns the live <see cref="AppConfig"/>. Changes go through <see cref="Update"/>, which schedules a
/// debounced save (one-shot timer, re-armed per change — nothing runs while idle).
/// <see cref="Flush"/> writes immediately and is called on shutdown.
/// </summary>
public sealed class ConfigService : IDisposable
{
    private readonly ConfigStore _store;
    private readonly Lock _gate = new();
    private readonly Timer _saveTimer;
    private readonly int _debounceMs;
    private bool _dirty;

    public ConfigService(ConfigStore store, int debounceMs = 750)
    {
        _store = store;
        _debounceMs = debounceMs;
        Current = store.Load();
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Live settings. Read freely; mutate only inside <see cref="Update"/>.</summary>
    public AppConfig Current { get; }

    public event Action<AppConfig>? Changed;

    public void Update(Action<AppConfig> change)
    {
        lock (_gate)
        {
            change(Current);
            _dirty = true;
            _saveTimer.Change(_debounceMs, Timeout.Infinite);
        }
        Changed?.Invoke(Current);
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            try
            {
                _store.Save(Current);
                _dirty = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Saving settings failed", ex);
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}
