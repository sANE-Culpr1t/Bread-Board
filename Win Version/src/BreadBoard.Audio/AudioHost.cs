using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Dsp;
using BreadBoard.Audio.Mixing;
using BreadBoard.Audio.Output;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Audio.Sources;
using BreadBoard.Core.Logging;
using System.Diagnostics;
using BreadBoard.Core.Models;
using BreadBoard.Core.Sounds;

namespace BreadBoard.Audio;

public enum SourceState { Off, Starting, Running, Unavailable, Failed }

public sealed record SourceStatus(SourceState State, string? Name, string? Error = null);

public sealed record HostStatus(SourceStatus Mic, SourceStatus App, SourceStatus Output, SourceStatus Monitor)
{
    public static readonly HostStatus Off = new(new(SourceState.Off, null), new(SourceState.Off, null), new(SourceState.Off, null), new(SourceState.Off, null));
}

/// <summary>
/// Owns the audio graph: devices, capture sources, mixer and outputs. Applies settings from
/// <see cref="AppConfig"/> and restarts anything that fails (device unplugged, app closed).
/// All public methods are safe to call from the UI thread; work is serialised internally.
/// </summary>
public sealed class AudioHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _life = new();
    private CaptureSource? _mic, _app;
    private int _appPid;
    private int _latency = -1;
    private bool _monitorDriven;
    private Process? _appProcess;
    private Timer? _appWait;
    private OutputDevice? _output, _monitor;
    private AppConfig _config = new();
    private SourceStatus _micStatus = HostStatus.Off.Mic, _appStatus = HostStatus.Off.App;
    private SourceStatus _outStatus = HostStatus.Off.Output, _monStatus = HostStatus.Off.Monitor;
    private int _retryPending;

    public AudioHost()
    {
        Soundboard = new SoundboardEngine();
        Mixer = new MixerEngine(Soundboard);
        Devices = new DeviceService();
        Devices.DevicesChanged += () => ScheduleRecovery("devices changed", 0);
    }

    public DeviceService Devices { get; }
    public SoundboardEngine Soundboard { get; }
    public MixerEngine Mixer { get; }

    public HostStatus Status => new(_micStatus, _appStatus, _outStatus, _monStatus);
    /// <summary>True while the monitor device drives the mix because there is no virtual-mic output.</summary>
    public bool MonitorOnly => _monitorDriven && _monitor is not null;
    public event Action<HostStatus>? StatusChanged;

    /// <summary>Starts (or restarts) everything for <paramref name="config"/>.</summary>
    public async Task StartAsync(AppConfig config)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _config = config;
            ApplyMixSettings(config);
            await RebuildAsync().ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Applies gains, mutes, routing and processing immediately (no device restart).</summary>
    public void ApplyMixSettings(AppConfig c)
    {
        _config = c;
        Apply(Mixer.Mic, Mixer.MicRouting, c.Channels.Mic);
        Apply(Mixer.App, Mixer.AppRouting, c.Channels.App);
        Mixer.AppRouting.ToMonitor = false; // you already hear the app itself; a second, delayed copy would echo
        Apply(Mixer.Board, Mixer.BoardRouting, c.Channels.Soundboard);
        Mixer.BoardMonitor.Gain = c.Channels.Soundboard.Gain / 100f;
        Mixer.BoardMonitor.Muted = c.Channels.Soundboard.Muted;
        Mixer.Master.Gain = c.Channels.Master.Gain / 100f;
        Mixer.Master.Muted = c.Channels.Master.Muted;

        var p = c.Processing;
        Mixer.Gate.Enabled = p.NoiseGate.Enabled;
        Mixer.Gate.Threshold = p.NoiseGate.Value / 100f;
        Mixer.MicCompressor.Enabled = p.Compressor.Enabled;
        Mixer.MicCompressor.Amount = p.Compressor.Value / 100f;
        Mixer.MasterLimiter.Enabled = p.Limiter.Enabled;
        Mixer.MasterLimiter.Ceiling = p.Limiter.Value / 100f;
        Mixer.AppDucker.Enabled = p.Ducking.Enabled;
        Mixer.AppDucker.Amount = p.Ducking.Value / 100f;
        Mixer.PushToTalkEnabled = p.PushToTalk.Enabled;

        Soundboard.AllowStacking = c.Playback.AllowStacking;
        Soundboard.MaxVoices = c.Playback.MaxVoices;

        static void Apply(ChannelStrip strip, ChannelRouting routing, ChannelSettings s)
        {
            strip.Gain = s.Gain / 100f;
            strip.Muted = s.Muted;
            routing.ToMain = s.ToVirtualMic;
            routing.ToMonitor = s.ToMonitor;
        }
    }

    private CaptureSource? _loopCapture;
    private OutputDevice? _loopOut;

    public bool LoopbackTestRunning => _loopOut is not null;

    /// <summary>
    /// Plays what apps receive from the Bread Board mic (its recording side) in your headphones, so you hear
    /// exactly what Discord hears. Returns null when started, otherwise why it couldn't.
    /// </summary>
    public string? StartLoopbackTest()
    {
        StopLoopbackTest();
        if (Devices.FindVirtualCableInput() is not { } mic) return "There's no Bread Board mic to listen to.";
        using var micDevice = Devices.Resolve(DeviceKind.Input, mic.Id);
        using var headphones = Devices.Resolve(DeviceKind.Output, _config.Devices.MonitorDeviceId);
        if (micDevice is null || headphones is null) return "Your headphones or the Bread Board mic aren't available.";
        if (DeviceService.IsVirtualCable(headphones.FriendlyName)) return "Pick your headphones as the monitor first (Mixer > Monitor).";
        try
        {
            _loopCapture = CaptureSource.StartDevice(micDevice, _config.Devices.LatencyMs);
            _loopOut = OutputDevice.Start(headphones, new RingBufferSampleProvider(_loopCapture.Buffer), _config.Devices.LatencyMs);
            Log.Info($"Loopback test: {mic.Name} -> {headphones.FriendlyName}");
            return null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warn("Loopback test failed", ex);
            StopLoopbackTest();
            return ex.Message;
        }
    }

    public void StopLoopbackTest()
    {
        _loopOut?.Dispose(); _loopOut = null;
        _loopCapture?.Dispose(); _loopCapture = null;
    }

    /// <summary>Decodes and registers a sound so it can be triggered. Runs on a worker thread.</summary>
    public Task<SoundClip> LoadSoundAsync(SoundEntry s) => Task.Run(() =>
    {
        var clip = SoundLoader.Load(s.Id, s.FilePath);
        Soundboard.Register(s.Id, clip, s.Mode, s.Volume, s.Route);
        return clip;
    });

    private async Task RebuildAsync()
    {
        var dev = _config.Devices;
        var latency = dev.LatencyMs;
        var restartAll = latency != _latency; // buffer size changed: reopen every stream
        _latency = latency;

        // Output (virtual cable) — started first so its name is known for the feedback check.
        var outDevice = dev.OutputDeviceId is null
            ? (Devices.FindVirtualCableOutput() is { } cable ? Devices.Resolve(DeviceKind.Output, cable.Id) : null)
            : Devices.Resolve(DeviceKind.Output, dev.OutputDeviceId);
        if (restartAll || _output is null || _outStatus.Name != outDevice?.FriendlyName || _outStatus.State != SourceState.Running)
        {
            _output?.Dispose(); _output = null;
            if (outDevice is null)
                SetOutput(new(SourceState.Unavailable, null, dev.OutputDeviceId is null
                    ? "No virtual audio cable found. Install VB-Audio Virtual Cable or pick an output."
                    : "The chosen virtual-mic output isn't connected."));
            else
            {
                try
                {
                    _output = OutputDevice.Start(outDevice, Mixer, latency);
                    _output.Stopped += ex => { if (ex is not null) { SetOutput(new(SourceState.Failed, _outStatus.Name, ex.Message)); ScheduleRecovery("output failed", 1000); } };
                    SetOutput(new(SourceState.Running, outDevice.FriendlyName));
                }
                catch (Exception ex)
                {
                    Log.Error("Output start failed", ex);
                    SetOutput(new(SourceState.Failed, outDevice.FriendlyName, ex.Message));
                }
            }
        }
        outDevice?.Dispose();

        using (var micDevice = Devices.ResolveMic(dev.InputDeviceId, dev.WindowsMicBefore))
        {
            if (!dev.MicEnabled)
            {
                StopMic(new(SourceState.Off, null));
            }
            else if (micDevice is null)
            {
                StopMic(new(SourceState.Unavailable, null, "Microphone not found"));
            }
            else if (DeviceService.IsFeedbackLoop(micDevice.FriendlyName, _outStatus.Name))
            {
                StopMic(new(SourceState.Failed, micDevice.FriendlyName, "Feedback loop: the input is the virtual cable itself. Pick your real microphone."));
            }
            else if (restartAll || _mic is null || _micStatus.Name != micDevice.FriendlyName || _micStatus.State != SourceState.Running)
            {
                StopMic(new(SourceState.Starting, micDevice.FriendlyName));
                try
                {
                    var src = CaptureSource.StartDevice(micDevice, latency);
                    src.Stopped += ex => { if (ex is not null) { SetMic(new(SourceState.Failed, src.Name, ex.Message)); ScheduleRecovery("mic failed", 1000); } };
                    _mic = src;
                    Mixer.MicSource = src;
                    SetMic(new(SourceState.Running, micDevice.FriendlyName));
                }
                catch (Exception ex)
                {
                    Log.Error("Mic start failed", ex);
                    SetMic(new(SourceState.Failed, micDevice.FriendlyName, ex.Message));
                }
            }
        }

        // App audio (process loopback). Remembered by executable name; follows the app across restarts.
        if (dev.AppSource is not { ProcessName.Length: > 0 } sel)
        {
            StopApp(new(SourceState.Off, null));
        }
        else if (AppSourceRules.Blocked(sel.ProcessName) is { } why)
        {
            StopApp(new(SourceState.Failed, sel.ProcessName, why));
        }
        else
        {
            var app = AudioAppEnumerator.Find(AudioAppEnumerator.List(Devices), sel.ProcessName)
                      ?? AudioAppEnumerator.FindRunning(sel.ProcessName);
            var label = app?.DisplayName ?? sel.DisplayName ?? sel.ProcessName;
            if (app is null)
            {
                StopApp(new(SourceState.Unavailable, label, $"Waiting for {label} to start"));
                WatchForAppStart(sel.ProcessName);
            }
            else if (restartAll || _app is null || _appPid != app.ProcessId || _appStatus.State != SourceState.Running)
            {
                StopApp(new(SourceState.Starting, label));
                try
                {
                    var src = await CaptureSource.StartProcessAsync(app.ProcessId, label, latency).ConfigureAwait(false);
                    src.Stopped += ex => { SetApp(new(SourceState.Unavailable, label, ex?.Message ?? "Capture stopped")); ScheduleRecovery("app capture stopped", 2000); };
                    _app = src;
                    _appPid = app.ProcessId;
                    Mixer.AppSource = src;
                    WatchForAppExit(app.ProcessId, label);
                    SetApp(new(SourceState.Running, label));
                }
                catch (Exception ex)
                {
                    Log.Error($"App capture failed for {label}", ex);
                    SetApp(new(SourceState.Failed, label, ex.Message));
                }
            }
        }

        if (!dev.MonitorEnabled)
        {
            StopMonitor(new(SourceState.Off, null));
        }
        else
        {
            // With no virtual-mic output the monitor device clocks the mixer itself, so you still hear sounds.
            var driven = _output is null;
            using var monDevice = Devices.Resolve(DeviceKind.Output, dev.MonitorDeviceId);
            if (monDevice is null) StopMonitor(new(SourceState.Unavailable, null, "Monitor device not found"));
            else if (!driven && monDevice.FriendlyName == _outStatus.Name) StopMonitor(new(SourceState.Failed, monDevice.FriendlyName, "The monitor can't be the virtual mic output; you'd send everything twice."));
            else if (restartAll || _monitor is null || _monStatus.Name != monDevice.FriendlyName || _monStatus.State != SourceState.Running || _monitorDriven != driven)
            {
                StopMonitor(new(SourceState.Starting, monDevice.FriendlyName));
                try
                {
                    var ring = new FloatRingBuffer(AudioFormat.SamplesFor(500));
                    Mixer.MonitorOut = ring;
                    _monitor = OutputDevice.Start(monDevice, driven ? new MonitorTap(Mixer, ring) : new RingBufferSampleProvider(ring), latency);
                    _monitorDriven = driven;
                    _monitor.Stopped += ex => { if (ex is not null) ScheduleRecovery("monitor failed", 1000); };
                    SetMonitor(new(SourceState.Running, monDevice.FriendlyName));
                }
                catch (Exception ex)
                {
                    Log.Error("Monitor start failed", ex);
                    SetMonitor(new(SourceState.Failed, monDevice.FriendlyName, ex.Message));
                }
            }
        }
    }

    private void StopMic(SourceStatus status)
    {
        Mixer.MicSource = null;
        _mic?.Dispose(); _mic = null;
        SetMic(status);
    }

    private void StopApp(SourceStatus status)
    {
        Mixer.AppSource = null;
        _app?.Dispose(); _app = null;
        _appPid = 0;
        _appProcess?.Dispose(); _appProcess = null;
        _appWait?.Dispose(); _appWait = null;
        SetApp(status);
    }

    /// <summary>Process exit is signalled by the OS (no polling); the app's capture is then torn down.</summary>
    private void WatchForAppExit(int pid, string label)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                if (!ReferenceEquals(_appProcess, proc)) return;
                Log.Info($"{label} closed");
                SetApp(new(SourceState.Unavailable, label, $"{label} closed"));
                ScheduleRecovery("app closed", 300);
            };
            _appProcess = proc;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Can't watch {label} for exit", ex);
        }
    }

    /// <summary>
    /// While the chosen app isn't running, check every few seconds whether it has started (a cheap
    /// process-name lookup; nothing runs when no app is chosen or it is already being captured).
    /// </summary>
    private void WatchForAppStart(string processName)
    {
        _appWait = new Timer(_ =>
        {
            if (!AudioAppEnumerator.IsRunning(processName)) return;
            Log.Info($"{processName} started");
            ScheduleRecovery("app started", 500);
        }, null, AppWaitMs, AppWaitMs);
    }

    /// <summary>How often a missing app is looked for, in milliseconds.</summary>
    public int AppWaitMs { get; init; } = 3000;

    private void StopMonitor(SourceStatus status)
    {
        Mixer.MonitorOut = null;
        _monitor?.Dispose(); _monitor = null;
        SetMonitor(status);
    }

    /// <summary>Re-runs the rebuild after a delay, coalescing bursts of failures/notifications.</summary>
    private void ScheduleRecovery(string reason, int delayMs)
    {
        if (_life.IsCancellationRequested || Interlocked.Exchange(ref _retryPending, 1) == 1) return;
        Log.Info($"Recovery scheduled ({reason})");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, _life.Token).ConfigureAwait(false);
                Interlocked.Exchange(ref _retryPending, 0);
                await _lock.WaitAsync(_life.Token).ConfigureAwait(false);
                try { await RebuildAsync().ConfigureAwait(false); }
                finally { _lock.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Error("Recovery failed", ex); }
        });
    }

    private void SetMic(SourceStatus s) { _micStatus = s; Raise(); }
    private void SetApp(SourceStatus s) { _appStatus = s; Raise(); }
    private void SetOutput(SourceStatus s) { _outStatus = s; Raise(); }
    private void SetMonitor(SourceStatus s) { _monStatus = s; Raise(); }
    private void Raise() => StatusChanged?.Invoke(Status);

    public async ValueTask DisposeAsync()
    {
        await _life.CancelAsync().ConfigureAwait(false);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            StopApp(new(SourceState.Off, null));
            StopMic(new(SourceState.Off, null));
            StopMonitor(new(SourceState.Off, null));
            StopLoopbackTest();
            _output?.Dispose(); _output = null;
            Devices.Dispose();
        }
        finally { _lock.Release(); }
        _lock.Dispose();
        _life.Dispose();
    }
}
