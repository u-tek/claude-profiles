using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ClaudeProfiles.Services;

/// <summary>Lets the downloaded exe install itself: copy into Programs, add shortcuts, and register with
/// Settings &gt; Apps so it can be uninstalled from there. No admin rights needed.</summary>
public static class Installer
{
    public const string RepoUrl = "https://github.com/u-tek/claude-profiles";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeProfiles";

    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string InstallDir => Path.Combine(Local, @"Programs\ClaudeProfiles");
    public static string InstalledExe => Path.Combine(InstallDir, "ClaudeProfiles.exe");

    static string Me => Environment.ProcessPath!;

    public static bool IsInstalledCopy =>
        string.Equals(AppPaths.Normalize(Me), AppPaths.Normalize(InstalledExe), StringComparison.OrdinalIgnoreCase);

#pragma warning disable IL3000 // Location is empty inside a single-file bundle, which is exactly what this checks.
    /// <summary>Only the published single-file build installs itself; a dev build under bin\ just runs.</summary>
    public static bool IsPublishedBuild => string.IsNullOrEmpty(typeof(Installer).Assembly.Location);
#pragma warning restore IL3000

    static Version? VersionOf(string exe)
    {
        try
        {
            if (!File.Exists(exe)) return null;
            var v = FileVersionInfo.GetVersionInfo(exe);
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
        }
        catch { return null; }
    }

    /// <summary>Puts this build in place (unless the installed one is newer), then starts the installed copy
    /// with the same arguments. Running a newer download over an existing install upgrades it.</summary>
    public static void InstallAndRelaunch(string[] args)
    {
        var installed = VersionOf(InstalledExe);
        var mine = VersionOf(Me);
        if (installed == null || mine >= installed)
        {
            StopInstalledCopy();
            Directory.CreateDirectory(InstallDir);
            // A stream copy leaves the download's mark-of-the-web behind, so shortcuts open without a security prompt.
            using (var src = File.OpenRead(Me))
            using (var dst = File.Create(InstalledExe))
                src.CopyTo(dst);
            Log.Write($"installed {mine} to {InstallDir} (was {installed?.ToString() ?? "not installed"})");
        }

        var psi = new ProcessStartInfo(InstalledExe) { UseShellExecute = false, WorkingDirectory = InstallDir };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>Run on every start of the installed copy: keeps the Settings &gt; Apps entry current and,
    /// the first time, adds the desktop shortcut.</summary>
    public static void Register()
    {
        try
        {
            using (var existing = Registry.CurrentUser.OpenSubKey(UninstallKey))
                if (existing == null) Shortcuts.EnsureDesktop();

            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", "Claude Profiles");
            key.SetValue("DisplayVersion", VersionOf(InstalledExe)?.ToString(3) ?? "");
            key.SetValue("Publisher", "u-tek");
            key.SetValue("DisplayIcon", InstalledExe + ",0");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
            key.SetValue("URLInfoAbout", RepoUrl);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }
        catch (Exception ex) { Log.Write("register failed: " + ex.Message); }
    }

    /// <summary>Removes the app, its shortcuts and its Settings &gt; Apps entry. Profiles' Claude data is kept.</summary>
    public static void Uninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show(
                $"Remove Claude Profiles from this PC?\n\nYour profiles' Claude data stays in {AppPaths.Root}. " +
                "Delete that folder yourself if you no longer need it.",
                "Uninstall Claude Profiles", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        StopInstalledCopy();
        if (File.Exists(AppPaths.ProfilesFile))
            foreach (var p in ProfileStore.Load()) Shortcuts.RemoveForProfile(p);
        Shortcuts.RemoveAppShortcuts();
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }
        try { Directory.Delete(AppPaths.IconsDir, recursive: true); } catch { }
        Log.Write("uninstalled");

        // A running exe can't delete its own folder, so a hidden cmd does it a moment after this process exits.
        if (Directory.Exists(InstallDir))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
    }

    /// <summary>Asks a running installed launcher to quit so its exe can be replaced or removed, and makes sure it has.</summary>
    static void StopInstalledCopy()
    {
        var running = Process.GetProcessesByName("ClaudeProfiles")
            .Where(p => p.Id != Environment.ProcessId && IsInstalledProcess(p))
            .ToList();
        if (running.Count == 0) return;

        SingleInstance.Send("quit");
        foreach (var p in running)
        {
            try
            {
                if (!p.WaitForExit(5000)) { p.Kill(); p.WaitForExit(3000); }
            }
            catch { }
        }
    }

    static bool IsInstalledProcess(Process p)
    {
        try { return string.Equals(p.MainModule?.FileName, InstalledExe, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
