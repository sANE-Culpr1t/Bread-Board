using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using BreadBoard.Audio.Import;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Mixer;

/// <summary>
/// Downloads VB-CABLE (free donationware by VB-Audio, vb-cable.com) from its official site and starts its
/// installer. VB-Audio allows using the unchanged package with another app as long as users can see where it
/// comes from and donate; Windows asks the user to allow the driver install, and a restart finishes it.
/// </summary>
public static class VirtualCableSetup
{
    public const string Website = "https://vb-audio.com/Cable/";
    // shortcut: pinned to the package current in Nov 2026; if VB-Audio renames it the download fails and the
    // user is sent to the website instead.
    private const string PackageUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip";
    private const int ErrorCancelled = 1223; // the user said No to the Windows permission prompt

    public enum Result { Installed, Cancelled, Failed }

    /// <summary>Downloads, unpacks and runs the installer; returns when the installer closes.</summary>
    public static async Task<(Result Result, string Message)> InstallAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BreadBoard-VBCABLE");
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "VBCABLE_Driver_Pack.zip");

            progress.Report("Downloading VB-CABLE from vb-audio.com…");
            using (var http = SoundDownloader.CreateHttpClient())
            using (var res = await http.GetAsync(PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                res.EnsureSuccessStatusCode();
                await using var file = File.Create(zip);
                await res.Content.CopyToAsync(file, ct);
            }

            progress.Report("Unpacking…");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, dir), ct);
            var setup = FindInstaller(dir) ?? throw new FileNotFoundException("The VB-CABLE package has no installer for this PC.");

            progress.Report("Waiting for the VB-CABLE installer (Windows asks for permission first)…");
            using var proc = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = dir })!;
            await proc.WaitForExitAsync(ct);
            Log.Info($"VB-CABLE installer exited with {proc.ExitCode}");
            return (Result.Installed, "Installer closed. If you clicked Install Driver, restart your PC to finish.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return (Result.Cancelled, "Not installed: Windows permission was declined.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
        {
            Log.Warn("VB-CABLE setup failed", ex);
            return (Result.Failed, $"Couldn't install it automatically ({ex.Message}). Get it from vb-cable.com instead.");
        }
    }

    public static void OpenWebsite() => Process.Start(new ProcessStartInfo(Website) { UseShellExecute = true });

    /// <summary>The package holds one setup program per processor type.</summary>
    private static string? FindInstaller(string dir)
    {
        var exes = Directory.GetFiles(dir, "*Setup*.exe", SearchOption.AllDirectories);
        var want = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => null,
        };
        return exes.FirstOrDefault(e => want is not null && Path.GetFileNameWithoutExtension(e).Contains(want, StringComparison.OrdinalIgnoreCase))
            ?? exes.FirstOrDefault(e => !Path.GetFileNameWithoutExtension(e).Contains("arm64", StringComparison.OrdinalIgnoreCase)
                                     && !Path.GetFileNameWithoutExtension(e).Contains("x64", StringComparison.OrdinalIgnoreCase));
    }
}
