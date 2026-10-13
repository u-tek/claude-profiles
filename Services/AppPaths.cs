using System.IO;

namespace ClaudeProfiles.Services;

public static class AppPaths
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Visible root holding every profile's data folder.</summary>
    public static string Root => Path.Combine(Home, "ClaudeProfiles");
    public static string ProfilesDir => Path.Combine(Root, "Profiles");
    public static string ProfilesFile => Path.Combine(Root, "profiles.json");

    /// <summary>Launcher-private state: icons, log.</summary>
    public static string StateDir => Path.Combine(Local, "ClaudeProfiles");
    public static string IconsDir => Path.Combine(StateDir, "icons");
    public static string LogFile => Path.Combine(StateDir, "launcher.log");

    /// <summary>The regular Claude app's own data folder.</summary>
    public static string MainClaudeData => Path.Combine(Roaming, "Claude");

    /// <summary>Where the regular app writes main.log (the Store build moved it to Local).</summary>
    public static string[] MainClaudeLogs => new[]
    {
        Path.Combine(Local, "Claude", "Logs", "main.log"),
        Path.Combine(MainClaudeData, "logs", "main.log"),
    };

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string StartMenu => Path.Combine(Roaming, @"Microsoft\Windows\Start Menu\Programs");

    public static string Normalize(string path)
    {
        try { path = Path.GetFullPath(path.Trim().Trim('"')); } catch { }
        return path.TrimEnd('\\', '/');
    }
}

public static class Log
{
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.StateDir);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
