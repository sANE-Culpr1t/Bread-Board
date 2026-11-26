using BreadBoard.Core.Logging;
using NAudio.CoreAudioApi;

namespace BreadBoard.Audio.Devices;

public enum DeviceKind { Input, Output }

public sealed record AudioDeviceInfo(string Id, string Name, DeviceKind Kind, bool IsDefault, bool IsVirtualCable);

/// <summary>Enumerates endpoints, finds the virtual cable, guards against feedback loops and reports device changes.</summary>
public sealed class DeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private Timer? _debounce;

    public DeviceService()
    {
        _notifications = _enumerator.CreateNotificationClient(false);
        _notifications.DeviceStateChanged += (_, _) => Changed();
        _notifications.DeviceAdded += (_, _) => Changed();
        _notifications.DeviceRemoved += (_, _) => Changed();
        _notifications.DefaultDeviceChanged += (_, _) => Changed();
    }

    internal MMDeviceEnumerator Enumerator => _enumerator;

    /// <summary>Raised (debounced, on a thread-pool thread) when devices are added, removed or change state.</summary>
    public event Action? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> GetDevices(DeviceKind kind)
    {
        var flow = kind == DeviceKind.Input ? DataFlow.Capture : DataFlow.Render;
        string? defaultId = null;
        if (_enumerator.TryGetDefaultAudioEndpoint(flow, Role.Console, out var def))
            using (def) defaultId = def.ID;

        var list = new List<AudioDeviceInfo>();
        foreach (var d in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            using (d) list.Add(new(d.ID, d.FriendlyName, kind, d.ID == defaultId, IsVirtualCable(d.FriendlyName)));
        return list;
    }

    /// <summary>Returns the endpoint for <paramref name="id"/>, or the default when id is null. Null if unavailable.</summary>
    public MMDevice? Resolve(DeviceKind kind, string? id)
    {
        try
        {
            if (id is not null)
            {
                var d = _enumerator.GetDevice(id);
                if (d.State == DeviceState.Active) return d;
                d.Dispose();
                return null;
            }
            var flow = kind == DeviceKind.Input ? DataFlow.Capture : DataFlow.Render;
            return _enumerator.TryGetDefaultAudioEndpoint(flow, Role.Console, out var def) ? def : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Log.Warn($"Device '{id}' not available", ex);
            return null;
        }
    }

    /// <summary>The render side of a virtual cable ("CABLE Input"), which apps then see as a microphone.</summary>
    public AudioDeviceInfo? FindVirtualCableOutput() =>
        GetDevices(DeviceKind.Output).FirstOrDefault(d => d.IsVirtualCable);

    /// <summary>The recording side of a virtual cable ("CABLE Output"): what Discord and games select to hear Bread Board.</summary>
    public AudioDeviceInfo? FindVirtualCableInput() =>
        GetDevices(DeviceKind.Input).FirstOrDefault(d => d.IsVirtualCable);

    /// <summary>
    /// The microphone Bread Board records: <paramref name="chosenId"/>, or for "system default" Windows' default,
    /// unless that is the Bread Board mic itself (then <paramref name="previousId"/> or the first real mic).
    /// </summary>
    public MMDevice? ResolveMic(string? chosenId, string? previousId)
    {
        if (chosenId is not null) return Resolve(DeviceKind.Input, chosenId);
        var def = Resolve(DeviceKind.Input, null);
        if (def is null || !IsVirtualCable(def.FriendlyName)) return def;
        def.Dispose();
        var real = GetDevices(DeviceKind.Input).Where(d => !d.IsVirtualCable).ToList();
        var pick = real.Find(d => d.Id == previousId) ?? real.FirstOrDefault();
        return pick is null ? null : Resolve(DeviceKind.Input, pick.Id);
    }

    /// <summary>Windows' default device of this kind, or null when there is none.</summary>
    public string? DefaultId(DeviceKind kind)
    {
        if (!_enumerator.TryGetDefaultAudioEndpoint(kind == DeviceKind.Input ? DataFlow.Capture : DataFlow.Render, Role.Communications, out var def)) return null;
        using (def) return def.ID;
    }

    /// <summary>Makes <paramref name="inputId"/> Windows' default microphone for every app.</summary>
    public static void SetDefaultInput(string inputId) => WindowsDefaultDevice.SetDefault(inputId);

    public const string MicName = "Bread Board Mic";

    /// <summary>The device's own volume (Sound settings › device › Volume), 0–1; null if it can't be read.</summary>
    public float? GetVolume(string id)
    {
        try { using var d = _enumerator.GetDevice(id); return d.AudioEndpointVolume.MasterVolumeLevelScalar; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException) { return null; }
    }

    public void SetVolume(string id, float level)
    {
        try { using var d = _enumerator.GetDevice(id); d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException) { Log.Warn($"Setting the volume of {id} failed", ex); }
    }

    /// <summary>Both ends of the virtual cable (enabled or disabled): the playback side Bread Board feeds, and the mic side apps hear.</summary>
    public IReadOnlyList<(string Id, DeviceKind Kind, string Name, bool Enabled)> BreadBoardEndpoints()
    {
        var list = new List<(string, DeviceKind, string, bool)>();
        foreach (var (flow, kind) in new[] { (DataFlow.Render, DeviceKind.Output), (DataFlow.Capture, DeviceKind.Input) })
            foreach (var d in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Disabled))
                using (d)
                {
                    var name = d.FriendlyName;
                    if (IsVirtualCable(name)) list.Add((d.ID, kind, name, d.State == DeviceState.Active));
                }
        return list;
    }

    /// <summary>Shows the Bread Board mic in every app's device list, or hides it (while Bread Board isn't running).</summary>
    public void SetBreadBoardDevicesEnabled(bool enabled)
    {
        foreach (var e in BreadBoardEndpoints())
            if (e.Enabled != enabled) WindowsDefaultDevice.SetEnabled(e.Id, enabled);
    }

    /// <summary>Names the cable's mic side "Bread Board Mic" and its playback side "Bread Board Mic feed". False if Windows refused.</summary>
    public bool NameBreadBoardDevices()
    {
        var ok = true;
        foreach (var e in BreadBoardEndpoints())
            ok &= WindowsDefaultDevice.Rename(e.Id, e.Kind == DeviceKind.Input ? MicName : e.Name.Contains("16ch", StringComparison.OrdinalIgnoreCase) ? MicName + " feed 16ch" : MicName + " feed");
        return ok;
    }

    public static bool IsVirtualCable(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
        || name.Contains(MicName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recording from the cable's capture side while also playing into it would loop our own output back in.
    /// </summary>
    public static bool IsFeedbackLoop(string? inputName, string? outputName) =>
        inputName is not null && outputName is not null && IsVirtualCable(inputName) && IsVirtualCable(outputName);

    private void Changed()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ => DevicesChanged?.Invoke(), null, 400, Timeout.Infinite);
    }

    public void Dispose()
    {
        _debounce?.Dispose();
        _notifications.Dispose();
        _enumerator.Dispose();
    }
}
