// Bread Board backend test host. Exercises the real audio engine without a UI.
//   bb devices                 list audio inputs/outputs
//   bb apps                    list running apps that can be captured
//   bb download <link>         download a sound (Myinstants page or .mp3/.wav/.ogg) into the library
//   bb add <file|link>         add a sound to the library config
//   bb run [--app <name>] [--no-mic]   live mix: mic + app audio + soundboard -> virtual mic
//   bb selftest                automated end-to-end test through the virtual cable
using BreadBoard.Audio;
using BreadBoard.Audio.Devices;
using BreadBoard.Audio.Import;
using BreadBoard.Audio.Input;
using BreadBoard.Core.Hotkeys;
using BreadBoard.Audio.Soundboard;
using BreadBoard.Audio.Sources;
using BreadBoard.Core;
using BreadBoard.Core.Config;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

Log.Init(AppPaths.Logs);
Log.Echo = args.Contains("--verbose") ? Console.Error.WriteLine : null;
var store = new ConfigStore(AppPaths.ConfigFile);

try
{
    return args.FirstOrDefault() switch
    {
        "devices" => Devices(),
        "apps" => Apps(),
        "download" when args.Length > 1 => await Download(args[1]),
        "add" when args.Length > 1 => await Add(args[1]),
        "run" => await Run(),
        "selftest" => await SelfTest(),
        "probe" => await Probe(),
        _ => Usage(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Log.Error("CLI failed", ex);
    return 1;
}
finally
{
    Log.Flush();
}

int Usage()
{
    Console.WriteLine("""
        Bread Board backend test host
          bb devices                         list audio devices
          bb apps                            list apps whose audio can be captured
          bb download <link>                 download a sound into the library
          bb add <file|link>                 add a sound to the library
          bb run [--app <name>] [--no-mic]   live mix to the virtual mic (keys 1-9 play sounds)
          bb selftest                        automated test through the virtual cable
        """);
    return 0;
}

int Devices()
{
    using var devices = new DeviceService();
    foreach (var kind in new[] { DeviceKind.Input, DeviceKind.Output })
    {
        Console.WriteLine(kind == DeviceKind.Input ? "Inputs (microphones):" : "Outputs:");
        foreach (var d in devices.GetDevices(kind))
            Console.WriteLine($"  {(d.IsDefault ? "*" : " ")} {d.Name}{(d.IsVirtualCable ? "   [virtual cable]" : "")}");
    }
    var cable = devices.FindVirtualCableOutput();
    Console.WriteLine(cable is null
        ? "\nNo virtual cable output found — install VB-Audio Virtual Cable."
        : $"\nVirtual mic output: {cable.Name}  (select 'CABLE Output' as the microphone in Discord/OBS)");
    return 0;
}

int Apps()
{
    using var devices = new DeviceService();
    var apps = AudioAppEnumerator.List(devices);
    Console.WriteLine($"{"PID",7}  {"Audio",-8}  {"Process",-22}  Window");
    foreach (var a in apps)
        Console.WriteLine($"{a.ProcessId,7}  {(a.IsPlaying ? "playing" : a.HasAudioSession ? "idle" : ""),-8}  {a.ProcessName,-22}  {Trim(a.WindowTitle, 60)}");
    return 0;
}

async Task<int> Download(string link)
{
    using var http = SoundDownloader.CreateHttpClient();
    var result = await new SoundDownloader(http).DownloadAsync(link, AppPaths.SoundLibrary);
    var clip = SoundLoader.Load("check", result.FilePath); // proves it decodes
    Console.WriteLine($"Saved {result.FilePath}  ({clip.Duration.TotalSeconds:0.0} s, decoded OK)");
    return 0;
}

async Task<int> Add(string source)
{
    var cfg = store.Load();
    SoundEntry entry;
    if (File.Exists(source))
    {
        var dest = Path.Combine(AppPaths.SoundLibrary, Path.GetFileName(source));
        Directory.CreateDirectory(AppPaths.SoundLibrary);
        if (!File.Exists(dest)) File.Copy(source, dest);
        entry = new SoundEntry { Name = Path.GetFileNameWithoutExtension(source), FilePath = dest };
    }
    else
    {
        using var http = SoundDownloader.CreateHttpClient();
        var r = await new SoundDownloader(http).DownloadAsync(source, AppPaths.SoundLibrary);
        entry = new SoundEntry { Name = r.SuggestedName, FilePath = r.FilePath, SourceUrl = r.SourceUrl.ToString() };
    }
    var clip = SoundLoader.Load(entry.Id, entry.FilePath);
    cfg.Sounds.Add(entry);
    store.Save(cfg);
    Console.WriteLine($"Added '{entry.Name}' ({clip.Duration.TotalSeconds:0.0} s) as sound #{cfg.Sounds.Count}");
    return 0;
}

async Task<int> Run()
{
    var cfg = store.Load();
    var appArg = Arg("--app");
    if (appArg is not null) cfg.Devices.AppSource = new AppSourceSelection { ProcessName = appArg };
    if (args.Contains("--no-mic")) cfg.Devices.MicEnabled = false;

    await using var host = new AudioHost();
    foreach (var s in cfg.Sounds)
    {
        try { await host.LoadSoundAsync(s); }
        catch (Exception ex) { Console.WriteLine($"  skipped '{s.Name}': {ex.Message}"); }
    }
    await host.StartAsync(cfg);
    PrintStatus(host.Status);
    if (Console.IsInputRedirected)
    {
        Console.WriteLine("'run' needs an interactive console. Use 'bb probe' for a non-interactive check.");
        return 1;
    }

    // Global hotkeys: work while Discord or a game has focus.
    using var hotkeys = new GlobalHotkeyService();
    var sounds = cfg.Sounds.Take(9).ToList();
    Console.WriteLine("\nGlobal hotkeys (work in any app):");
    for (var i = 0; i < sounds.Count; i++)
    {
        var s = sounds[i];
        var combo = s.Hotkey ?? $"Num{i + 1}";
        var id = s.Id;
        string? err = Hotkey.TryParse(combo, out var hk)
            ? await hotkeys.RegisterAsync(hk, () => host.Soundboard.Trigger(id, true), s.Mode == PlayMode.Hold ? () => host.Soundboard.Trigger(id, false) : null)
            : "invalid";
        Console.WriteLine($"  {combo,-14} {s.Name} ({s.Mode}){(err is null ? "" : $"  !! {err}")}");
    }
    async Task Global(string? combo, string label, Action action)
    {
        if (!Hotkey.TryParse(combo, out var hk)) return;
        var err = await hotkeys.RegisterAsync(hk, action);
        Console.WriteLine($"  {hk,-14} {label}{(err is null ? "" : $"  !! {err}")}");
    }
    await Global(cfg.Hotkeys.StopAll, "Stop all sounds", host.Soundboard.StopAll);
    await Global(cfg.Hotkeys.MuteMic, "Mute / unmute mic", () => { cfg.Channels.Mic.Muted = !cfg.Channels.Mic.Muted; host.ApplyMixSettings(cfg); });

    Console.WriteLine("""

        Keys in this window: 1-9 play · s stop · m mute mic · v virtual mic on/off · q quit
                             g gate on/off · [ ] gate threshold · - + mic gain
        """);

    while (true)
    {
        while (Console.KeyAvailable)
        {
            var k = Console.ReadKey(intercept: true);
            var key = k.KeyChar;
            var digit = key is >= '1' and <= '9' ? key - '1' : k.Key is >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad9 ? k.Key - ConsoleKey.NumPad1 : -1;
            var g = cfg.Processing.NoiseGate;
            if (key == 'q') { Console.WriteLine(); store.Save(cfg); Console.WriteLine("Settings saved."); return 0; }
            else if (key == 's') host.Soundboard.StopAll();
            else if (key == 'm') cfg.Channels.Mic.Muted = !cfg.Channels.Mic.Muted;
            else if (key == 'v') cfg.Devices.VirtualMicEnabled = !cfg.Devices.VirtualMicEnabled;
            else if (key == 'g') g.Enabled = !g.Enabled;
            else if (key == '[') g.Value = Math.Max(0, g.Value - 1);
            else if (key == ']') g.Value = Math.Min(50, g.Value + 1);
            else if (key is '-' or '_') cfg.Channels.Mic.Gain = Math.Max(0, cfg.Channels.Mic.Gain - 10);
            else if (key is '+' or '=') cfg.Channels.Mic.Gain = Math.Min(200, cfg.Channels.Mic.Gain + 10);
            else if (digit >= 0 && digit < sounds.Count) host.Soundboard.Trigger(sounds[digit].Id);
            host.ApplyMixSettings(cfg);
        }
        var l = host.Mixer.TakeLevels();
        var gate = cfg.Processing.NoiseGate.Enabled ? $"gate {cfg.Processing.NoiseGate.Value}%" : "gate off";
        var line = $"mic {Bar(l.Mic)} app {Bar(l.App)} sb {Bar(l.Soundboard)} out {Bar(l.Master)} | mic {cfg.Channels.Mic.Gain}% {gate}{(cfg.Channels.Mic.Muted ? " MUTED" : "")}{(cfg.Devices.VirtualMicEnabled ? "" : " VMIC OFF")}";
        var width = Math.Max(20, Console.WindowWidth - 1); // never wrap, or \r can't redraw in place
        Console.Write("\r" + (line.Length > width ? line[..width] : line.PadRight(width)));
        await Task.Delay(50);
    }
}

/// <summary>Non-interactive: runs mic + app → virtual mic for a few seconds and reports what reached CABLE Output.</summary>
async Task<int> Probe()
{
    var cfg = store.Load();
    var appArg = Arg("--app");
    if (appArg is not null) cfg.Devices.AppSource = new AppSourceSelection { ProcessName = appArg };
    if (args.Contains("--no-mic")) cfg.Devices.MicEnabled = false;
    var seconds = int.TryParse(Arg("--seconds"), out var s) ? s : 4;
    if (args.Contains("--no-gate")) cfg.Processing.NoiseGate.Enabled = false;

    await using var host = new AudioHost();
    await host.StartAsync(cfg);
    PrintStatus(host.Status);

    using var devices = new DeviceService();
    var cableIn = devices.GetDevices(DeviceKind.Input).FirstOrDefault(d => d.IsVirtualCable);
    using var listenDev = cableIn is null ? null : devices.Resolve(DeviceKind.Input, cableIn.Id);
    using var listen = listenDev is null ? null : CaptureSource.StartDevice(listenDev);

    float mic = 0, app = 0, outPeak = 0;
    double heard = 0;
    for (var t = 0; t < seconds * 10; t++)
    {
        await Task.Delay(100);
        var l = host.Mixer.TakeLevels();
        mic = Math.Max(mic, l.Mic); app = Math.Max(app, l.App); outPeak = Math.Max(outPeak, l.Master);
        if (listen is not null) heard = Math.Max(heard, Rms(listen));
    }
    Console.WriteLine($"\nPeak levels over {seconds}s:  mic {mic:0.000}   app {app:0.000}   virtual-mic out {outPeak:0.000}");
    Console.WriteLine($"Heard on 'CABLE Output' (what Discord/OBS receive): rms {heard:0.000}");
    return 0;
}

async Task<int> SelfTest()
{
    Console.WriteLine("Bread Board self-test (no sound reaches your speakers)\n");
    using var devices = new DeviceService();
    var cableOut = devices.FindVirtualCableOutput();
    var cableIn = devices.GetDevices(DeviceKind.Input).FirstOrDefault(d => d.IsVirtualCable);
    if (cableOut is null || cableIn is null)
    {
        Console.WriteLine("FAIL  virtual cable not found (need 'CABLE Input' and 'CABLE Output').");
        return 1;
    }
    Console.WriteLine($"PASS  virtual cable found: {cableOut.Name} -> {cableIn.Name}");

    var cfg = new AppConfig();
    cfg.Devices.MicEnabled = false; // keep the room out of the test
    cfg.Devices.OutputDeviceId = cableOut.Id;
    await using var host = new AudioHost();
    await host.StartAsync(cfg);
    var ok = host.Status.Output.State == SourceState.Running;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  engine output running on {host.Status.Output.Name} {host.Status.Output.Error}");
    if (!ok) return 1;

    // Listen on the cable's other end, and on our own process via process loopback.
    using var micSide = devices.Resolve(DeviceKind.Input, cableIn.Id)!;
    using var cableCapture = CaptureSource.StartDevice(micSide);
    using var selfCapture = await CaptureSource.StartProcessAsync(Environment.ProcessId, "bb (self)");

    host.Soundboard.Register("tone", Tone(440, 2.5, 0.5f), PlayMode.OneShot, 100);
    await Task.Delay(300);
    cableCapture.Buffer.Skip(cableCapture.Buffer.Count);
    selfCapture.Buffer.Skip(selfCapture.Buffer.Count);
    host.Soundboard.Trigger("tone");
    await Task.Delay(1200);

    var failures = 0;
    failures += Check("soundboard -> virtual cable -> 'CABLE Output' (what Discord hears)", Rms(cableCapture), 0.05);
    failures += Check("process loopback capture of a running app", Rms(selfCapture), 0.05);

    host.Soundboard.StopAll();
    cfg.Devices.VirtualMicEnabled = false;
    host.ApplyMixSettings(cfg);
    host.Soundboard.Trigger("tone");
    await Task.Delay(400);
    cableCapture.Buffer.Skip(cableCapture.Buffer.Count);
    await Task.Delay(600);
    var muted = Rms(cableCapture);
    var silent = muted < 0.005;
    Console.WriteLine($"{(silent ? "PASS" : "FAIL")}  virtual mic switch off sends silence (rms {muted:0.0000})");
    failures += silent ? 0 : 1;

    Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed. See {AppPaths.Logs}");
    return failures == 0 ? 0 : 1;
}

int Check(string what, double rms, double min)
{
    var pass = rms >= min;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {what} (rms {rms:0.000})");
    return pass ? 0 : 1;
}

static double Rms(CaptureSource src)
{
    var buf = new float[src.Buffer.Count];
    var n = src.Buffer.Read(buf);
    if (n == 0) return 0;
    double sum = 0;
    for (var i = 0; i < n; i++) sum += buf[i] * buf[i];
    return Math.Sqrt(sum / n);
}

static SoundClip Tone(double hz, double seconds, float amp)
{
    var frames = (int)(seconds * AudioFormat.SampleRate);
    var data = new float[frames * 2];
    for (var i = 0; i < frames; i++)
        data[2 * i] = data[2 * i + 1] = amp * (float)Math.Sin(2 * Math.PI * hz * i / AudioFormat.SampleRate);
    return new SoundClip("tone", data);
}

void PrintStatus(HostStatus s)
{
    Console.WriteLine($"Mic:     {s.Mic.State,-11} {s.Mic.Name} {s.Mic.Error}");
    Console.WriteLine($"App:     {s.App.State,-11} {s.App.Name} {s.App.Error}");
    Console.WriteLine($"Output:  {s.Output.State,-11} {s.Output.Name} {s.Output.Error}");
}

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

/// <summary>dB-scaled meter, -60 dB … 0 dB, so quiet microphones still show movement.</summary>
static string Bar(float level)
{
    var db = level <= 1e-6f ? -60 : 20 * Math.Log10(level);
    var n = (int)Math.Round(Math.Clamp((db + 60) / 60, 0, 1) * 10);
    return new string('#', n) + new string('.', 10 - n);
}

static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
