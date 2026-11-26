using System.Runtime.InteropServices;

namespace BreadBoard.Audio.Devices;

/// <summary>
/// Endpoint changes the Windows Sound control panel makes: default device, name, enabled/disabled. Windows has
/// no public API for these; IPolicyConfig is the interface the control panel itself uses, stable since Windows 7.
/// </summary>
internal static class WindowsDefaultDevice
{
    /// <summary>The device's own name ("CABLE Output"), shown under or after the adapter name.</summary>
    private static readonly PropertyKey DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);


    public static void SetDefault(string endpointId) => Use(p =>
    {
        for (var role = 0; role < 3; role++) // console, multimedia, communications (Discord and Teams use the last)
            Marshal.ThrowExceptionForHR(p.SetDefaultEndpoint(endpointId, role));
    });

    /// <summary>Disabled endpoints disappear from every app's device list until enabled again.</summary>
    public static void SetEnabled(string endpointId, bool enabled) =>
        Use(p => Marshal.ThrowExceptionForHR(p.SetEndpointVisibility(endpointId, enabled ? 1 : 0)));

    /// <summary>
    /// Renames the endpoint (its own name; the adapter name, e.g. "VB-Audio Virtual Cable", comes from the
    /// driver and Windows refuses to change it). Returns false if Windows refused.
    /// </summary>
    public static bool Rename(string endpointId, string deviceName)
    {
        var hr = 0;
        Use(p => hr = SetString(p, endpointId, DeviceDesc, deviceName));
        return hr >= 0;
    }

    private static int SetString(IPolicyConfig p, string id, PropertyKey key, string value)
    {
        var v = new PropVariant { vt = 31 /* VT_LPWSTR */, ptr = Marshal.StringToCoTaskMemUni(value) };
        try { return p.SetPropertyValue(id, 0, ref key, ref v); }
        finally { Marshal.FreeCoTaskMem(v.ptr); }
    }

    private static void Use(Action<IPolicyConfig> action)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try { action(policy); }
        finally { Marshal.ReleaseComObject(policy); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid fmtid, int pid)
    {
        public Guid fmtid = fmtid;
        public int pid = pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint ptr;
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient;

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, nint format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, nint format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, nint endpointFormat, nint mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, nint defaultPeriod, nint minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, nint period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
