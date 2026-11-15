namespace BreadBoard.Core;

/// <summary>
/// Per-user storage locations. Call <see cref="UseRoot"/> (e.g. from a --data-dir argument)
/// before anything reads a path to keep tests and portable runs away from the real profile.
/// </summary>
public static class AppPaths
{
    private static string _roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BreadBoard");
    private static string _local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreadBoard");

    public static string Roaming => _roaming;
    public static string ConfigFile => Path.Combine(_roaming, "config.json");
    public static string SoundLibrary => Path.Combine(_local, "Sounds");
    public static string Logs => Path.Combine(_local, "Logs");

    /// <summary>Puts all data (config, sounds, logs) under one folder.</summary>
    public static void UseRoot(string root)
    {
        var full = Path.GetFullPath(root);
        _roaming = full;
        _local = full;
    }
}
