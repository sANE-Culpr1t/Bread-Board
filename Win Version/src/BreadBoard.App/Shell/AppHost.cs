using BreadBoard.App.Services;
using BreadBoard.Audio;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using BreadBoard.Core.Logging;
using BreadBoard.Platform.Hotkeys;

namespace BreadBoard.App.Shell;

/// <summary>
/// Composition root: creates and owns every service, in dependency order, and disposes them in
/// reverse on shutdown. Heavy modules (audio engine, hotkey thread) are
/// created lazily on first use so startup stays fast and idle cost stays near zero.
/// </summary>
public sealed class AppHost
{
    private readonly Lazy<AudioHost> _audio = new(() => { Log.Info("Audio engine created"); return new AudioHost(); });
    private readonly Lazy<GlobalHotkeyService> _hotkeys = new(() => { Log.Info("Hotkey service created"); return new GlobalHotkeyService(); });
    private int _shutdown;

    public AppHost(StartupOptions options)
    {
        Options = options;
        Config = new ConfigService(new ConfigStore(AppPaths.ConfigFile));
        Navigation = new NavigationService(Config);
        Soundboard = new SoundboardController(Config, _audio, _hotkeys, System.Windows.Threading.Dispatcher.CurrentDispatcher);
    }

    /// <summary>Sound library, playback and shortcuts (UI thread).</summary>
    public SoundboardController Soundboard { get; }

    public StartupOptions Options { get; }
    public ConfigService Config { get; }
    public NavigationService Navigation { get; }


    public bool AudioCreated => _audio.IsValueCreated;
    public bool HotkeysCreated => _hotkeys.IsValueCreated;

    /// <summary>Disposes modules in reverse order of dependency. Safe to call more than once.</summary>
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) == 1) return;
        Log.Info("Shutdown started");
        Step("soundboard", Soundboard.Dispose);
        if (_hotkeys.IsValueCreated) Step("hotkeys", () => _hotkeys.Value.Dispose());
        if (_audio.IsValueCreated)
        {
            try { await _audio.Value.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error("Audio engine shutdown failed", ex); }
        }
        if (!Options.IsAutomated) Step("bread board mic", () => SoundboardController.ReleaseBreadBoardMic(Config)); // test runs leave your devices alone
        Step("config", Config.Dispose);
        Log.Info("Shutdown complete");
        Log.Flush();

        static void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Error($"Shutdown step '{name}' failed", ex); }
        }
    }
}
