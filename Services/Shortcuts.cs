using System.IO;

namespace ClaudeProfiles.Services;

public static class Shortcuts
{
    static string ExePath => Environment.ProcessPath!;

    public static string ProfileShortcutPath(ProfileRecord p) =>
        Path.Combine(AppPaths.Desktop, $"Claude - {Sanitize(p.Name)}.lnk");

    public static bool HasDesktopShortcut(ProfileRecord p) => File.Exists(ProfileShortcutPath(p));

    /// <summary>Desktop shortcut that opens this profile through the launcher (so sign-in routing is active).</summary>
    public static void CreateForProfile(ProfileRecord p) =>
        Create(ProfileShortcutPath(p), ExePath, $"--open {p.Id}", Icons.ProfileIcon(p), $"Open Claude as \"{p.Name}\"");

    public static void RemoveForProfile(ProfileRecord p, string? previousName = null)
    {
        foreach (var name in new[] { p.Name, previousName })
        {
            if (name == null) continue;
            var path = Path.Combine(AppPaths.Desktop, $"Claude - {Sanitize(name)}.lnk");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    public static void EnsureStartMenu()
    {
        var path = Path.Combine(AppPaths.StartMenu, "Claude Profiles.lnk");
        try { Create(path, ExePath, "", Icons.AppIcon(), "Run separate Claude profiles side by side"); }
        catch (Exception ex) { Log.Write("start menu shortcut failed: " + ex.Message); }
    }

    static void Create(string lnkPath, string target, string args, string icon, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = target;
            lnk.Arguments = args;
            lnk.WorkingDirectory = Path.GetDirectoryName(target);
            lnk.IconLocation = icon + ",0";
            lnk.Description = description;
            lnk.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }

    static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
    }
}
