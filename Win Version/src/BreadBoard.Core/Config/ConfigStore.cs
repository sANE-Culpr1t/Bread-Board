using System.Text.Json;
using System.Text.Json.Serialization;
using BreadBoard.Core.Logging;
using BreadBoard.Core.Models;

namespace BreadBoard.Core.Config;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

/// <summary>Loads and atomically saves <see cref="AppConfig"/> as JSON.</summary>
public sealed class ConfigStore(string path)
{
    public string Path { get; } = path;

    public AppConfig Load()
    {
        if (!File.Exists(Path)) return new AppConfig();
        try
        {
            using var s = File.OpenRead(Path);
            var cfg = JsonSerializer.Deserialize(s, ConfigJsonContext.Default.AppConfig);
            return cfg is null ? new AppConfig() : Migrate(cfg);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file for inspection instead of silently overwriting it.
            var backup = Path + $".bad-{DateTime.Now:yyyyMMddHHmmss}";
            try { File.Copy(Path, backup, overwrite: true); } catch (IOException) { }
            Log.Error($"Config unreadable, using defaults (backup: {backup})", ex);
            return new AppConfig();
        }
    }

    /// <summary>Upgrades settings written by older versions.</summary>
    public static AppConfig Migrate(AppConfig cfg)
    {
        if (cfg.Version < 2)
        {
            // v2: "Hear sounds on this PC" became the mixer's monitor output.
            cfg.Devices.MonitorEnabled = cfg.Playback.HearOnThisPc;
            Log.Info("Settings migrated to version 2");
        }
        if (!DeviceSettings.LatencyChoices.Contains(cfg.Devices.LatencyMs)) cfg.Devices.LatencyMs = 20;
        cfg.Version = AppConfig.CurrentVersion;
        return cfg;
    }

    /// <summary>Deep copy (used to hand the audio engine a snapshot it may adjust).</summary>
    public static AppConfig Clone(AppConfig config) =>
        JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(config, ConfigJsonContext.Default.AppConfig), ConfigJsonContext.Default.AppConfig)!;

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        using (var s = File.Create(tmp))
            JsonSerializer.Serialize(s, config, ConfigJsonContext.Default.AppConfig);
        File.Move(tmp, Path, overwrite: true);
    }
}
