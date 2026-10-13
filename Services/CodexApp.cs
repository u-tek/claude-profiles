using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ClaudeProfiles.Services;

/// <summary>Finds the Codex desktop app (a Microsoft Store package) and starts instances of it.</summary>
public static class CodexApp
{
    const string FamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    const string RepositoryKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>The regular Codex app keeps its sign-in, settings and chats here.</summary>
    public static string MainHome =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    /// <summary>A profile's CODEX_HOME: its sign-in, settings and chats.</summary>
    public static string HomeDir(string profileDir) => Path.Combine(profileDir, "codex-home");

    /// <summary>A profile's app data (windows, caches), passed as --user-data-dir.</summary>
    public static string UserDataDir(string profileDir) => Path.Combine(profileDir, "user-data");

    /// <summary>The installed package's main exe (Codex runs on OpenAI's ChatGPT.exe desktop shell), newest version first.</summary>
    public static string? FindExe()
    {
        try
        {
            using var repo = Registry.CurrentUser.OpenSubKey(RepositoryKey);
            if (repo == null) return null;
            return repo.GetSubKeyNames()
                .Where(n => n.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
                            n.EndsWith("__" + FamilyName["OpenAI.Codex_".Length..], StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => Version.TryParse(n.Split('_')[1], out var v) ? v : new Version())
                .Select(n =>
                {
                    using var key = repo.OpenSubKey(n);
                    return key?.GetValue("PackageRootFolder") is string root ? Path.Combine(root, "app", "ChatGPT.exe") : null;
                })
                .FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }

    public static bool IsInstalled => FindExe() != null;

    /// <summary>Starts (or brings forward) the regular Codex app.</summary>
    public static void LaunchMain()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{FamilyName}!App") { UseShellExecute = false });
        Log.Write("launch codex <main>");
    }

    /// <summary>Starts (or brings forward) the Codex instance for a profile folder.</summary>
    /// <remarks>Windows won't start a Store app's exe directly, and its normal launch path can't set environment
    /// variables, which is how Codex is told where a profile's sign-in lives. So this runs our own exe inside Codex's
    /// package with <c>--start-codex</c>; it sets the variables and starts Codex as its child, which keeps the
    /// package identity Codex needs (for its sandbox, among other things).</remarks>
    public static void Launch(string profileDir)
    {
        var exe = FindExe() ?? throw new InvalidOperationException("Codex isn't installed.");
        var home = HomeDir(profileDir);
        var data = UserDataDir(profileDir);
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(data);

        var args = $"--start-codex \"{exe}\" \"{home}\" \"{data}\"";
        var script = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{FamilyName}' -AppId 'App' " +
                     $"-Command {PsQuote(Environment.ProcessPath!)} -Args {PsQuote(args)}";
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            psi.ArgumentList.Add(a);
        Process.Start(psi);
        Log.Write($"launch codex dataDir={profileDir}");
    }

    /// <summary>The <c>--start-codex</c> half of <see cref="Launch"/>, running inside Codex's package.</summary>
    public static void StartInPackage(string exe, string home, string data)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        psi.ArgumentList.Add("--user-data-dir=" + data);
        psi.Environment["CODEX_HOME"] = home;
        psi.Environment["CODEX_ELECTRON_USER_DATA_PATH"] = data;
        Process.Start(psi);
    }

    static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";
}
