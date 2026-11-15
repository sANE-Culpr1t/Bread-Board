namespace BreadBoard.Core;

/// <summary>Per-user storage locations.</summary>
public static class AppPaths
{
    public static string Roaming { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BreadBoard");

    public static string Local { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreadBoard");

    public static string ConfigFile => Path.Combine(Roaming, "config.json");
    public static string SoundLibrary => Path.Combine(Local, "Sounds");
    public static string Logs => Path.Combine(Local, "Logs");
}
