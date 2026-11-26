using System.Diagnostics;
using System.Runtime.InteropServices;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Sounds;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BreadBoard.Audio.Sources;

/// <summary>A running application the user can pick as the "App Audio" source.</summary>
/// <param name="ProcessId">Root process to capture (child processes are included).</param>
/// <param name="HasAudioSession">The app (or one of its child processes) has an audio session.</param>
/// <param name="IsPlaying">One of its sessions is currently active.</param>
/// <param name="ExePath">Full executable path when Windows lets us read it (for the icon), else null.</param>
/// <param name="DisplayName">Product description ("Brave Browser") or the process name.</param>
public sealed record AudioApp(int ProcessId, string ProcessName, string WindowTitle, bool HasAudioSession, bool IsPlaying,
    string? ExePath = null, string? DisplayName = null)
{
    public string Label => DisplayName ?? ProcessName;
}

/// <summary>
/// Lists apps with visible windows and/or audio sessions. Browsers play audio from a child
/// process, so each session is mapped up to its top-level ancestor with the same executable.
/// </summary>
public static partial class AudioAppEnumerator
{
    public static IReadOnlyList<AudioApp> List(Devices.DeviceService devices)
    {
        var parents = ParentMap();
        var names = new Dictionary<int, string>();
        string NameOf(int pid)
        {
            if (names.TryGetValue(pid, out var n)) return n;
            try { using var p = Process.GetProcessById(pid); n = p.ProcessName; } catch (ArgumentException) { n = ""; } catch (InvalidOperationException) { n = ""; }
            return names[pid] = n;
        }
        int Root(int pid)
        {
            var name = NameOf(pid);
            for (var guard = 0; guard < 16 && parents.TryGetValue(pid, out var parent) && parent != 0 && parent != pid; guard++)
            {
                if (!string.Equals(NameOf(parent), name, StringComparison.OrdinalIgnoreCase)) break;
                pid = parent;
            }
            return pid;
        }

        // Audio sessions on every active render device.
        var audio = new Dictionary<int, bool>(); // root pid -> playing
        foreach (var dev in devices.Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (dev)
            {
                try
                {
                    var sessions = dev.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        using var s = sessions[i];
                        if (s.IsSystemSoundsSession) continue;
                        var pid = (int)s.GetProcessID;
                        if (pid == 0 || pid == Environment.ProcessId) continue;
                        var root = Root(pid);
                        var active = s.State == AudioSessionState.AudioSessionStateActive;
                        audio[root] = (audio.TryGetValue(root, out var was) && was) || active;
                    }
                }
                catch (COMException ex) { Log.Warn($"Could not read sessions on {dev.FriendlyName}", ex); }
            }
        }

        // Visible top-level windows, grouped by root process.
        var titles = new Dictionary<int, string>();
        foreach (var (pid, title) in VisibleWindows())
        {
            if (pid == Environment.ProcessId) continue;
            var root = Root(pid);
            if (!titles.ContainsKey(root)) titles[root] = title;
        }

        var result = new List<AudioApp>();
        foreach (var pid in titles.Keys.Union(audio.Keys))
        {
            var name = NameOf(pid);
            if (name.Length == 0 || AppSourceRules.Blocked(name) is not null) continue;
            var has = audio.TryGetValue(pid, out var playing);
            var exe = ExePath(pid);
            result.Add(new AudioApp(pid, name, titles.GetValueOrDefault(pid, name), has, playing, exe, Describe(exe)));
        }
        return [.. result
            .OrderByDescending(a => a.IsPlaying)
            .ThenByDescending(a => a.HasAudioSession)
            .ThenBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Finds the best running match for a remembered selection.</summary>
    public static AudioApp? Find(IReadOnlyList<AudioApp> apps, string processName) =>
        apps.Where(a => string.Equals(a.ProcessName, AppSourceRules.BaseName(processName), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.IsPlaying).ThenByDescending(a => a.HasAudioSession).FirstOrDefault();

    /// <summary>
    /// Fallback for a remembered app that has no window or audio session yet (just launched, minimised
    /// to the tray): the top-most running process with that name. Null when it isn't running.
    /// </summary>
    public static AudioApp? FindRunning(string processName)
    {
        var procs = Process.GetProcessesByName(AppSourceRules.BaseName(processName));
        try
        {
            if (procs.Length == 0) return null;
            var ids = procs.Select(p => p.Id).ToHashSet();
            var parents = ParentMap();
            var root = ids.FirstOrDefault(id => !parents.TryGetValue(id, out var parent) || !ids.Contains(parent), ids.First());
            if (root == Environment.ProcessId) return null;
            return new AudioApp(root, procs[0].ProcessName, procs[0].ProcessName, false, false, ExePath(root), null);
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
    }

    /// <summary>The app's own volume (its first sound session's), 0–1; null when it has no session.</summary>
    public static float? GetVolume(Devices.DeviceService devices, string processName)
    {
        float? found = null;
        ForEachSession(devices, processName, s => { found ??= s.SimpleAudioVolume.Volume; });
        return found;
    }

    /// <summary>Sets the app's volume in every sound session it has (what you hear; Windows' Volume mixer shows the same).</summary>
    public static void SetVolume(Devices.DeviceService devices, string processName, float level) =>
        ForEachSession(devices, processName, s => s.SimpleAudioVolume.Volume = Math.Clamp(level, 0f, 1f));

    private static void ForEachSession(Devices.DeviceService devices, string processName, Action<AudioSessionControl> action)
    {
        var name = AppSourceRules.BaseName(processName);
        foreach (var dev in devices.Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (dev)
            {
                try
                {
                    var sessions = dev.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        using var s = sessions[i];
                        var pid = (int)s.GetProcessID;
                        if (pid == 0) continue;
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            if (string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)) action(s);
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { } // exited meanwhile
                    }
                }
                catch (COMException ex) { Log.Warn($"Could not read sessions on {dev.FriendlyName}", ex); }
            }
        }
    }

    /// <summary>True if any process with this name is running (cheap; used while waiting for an app to start).</summary>
    public static bool IsRunning(string processName)
    {
        var procs = Process.GetProcessesByName(AppSourceRules.BaseName(processName));
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }


    private static string? Describe(string? exe)
    {
        if (exe is null) return null;
        try
        {
            var d = FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim();
            return string.IsNullOrEmpty(d) || d.Length > 48 ? null : d;
        }
        catch (FileNotFoundException) { return null; }
    }

    private static string? ExePath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try
        {
            var buf = new char[1024];
            var len = buf.Length;
            return QueryFullProcessImageName(h, 0, buf, ref len) ? new string(buf, 0, len) : null;
        }
        finally { CloseHandle(h); }
    }

    private static List<(int Pid, string Title)> VisibleWindows()
    {
        var list = new List<(int, string)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0) return true;
            if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            var len = GetWindowTextLength(hwnd);
            if (len == 0) return true;
            var buf = new char[len + 1];
            var got = GetWindowText(hwnd, buf, buf.Length);
            if (got == 0 || GetWindowThreadProcessId(hwnd, out var pid) == 0) return true;
            list.Add(((int)pid, new string(buf, 0, got)));
            return true;
        }, 0);
        return list;
    }

    private static Dictionary<int, int> ParentMap()
    {
        var map = new Dictionary<int, int>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == -1) return map;
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snap, ref e)) return map;
            do map[(int)e.th32ProcessID] = (int)e.th32ParentProcessID;
            while (Process32Next(snap, ref e));
        }
        finally { CloseHandle(snap); }
        return map;
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const uint GW_OWNER = 4;
    private const uint TH32CS_SNAPPROCESS = 0x2;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern long GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, [Out] char[] text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(nint snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(nint snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint h, uint flags, [Out] char[] name, ref int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
}
