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

    public static bool IsVirtualCable(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

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
