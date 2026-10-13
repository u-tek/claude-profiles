using System.Diagnostics;
using System.IO;

namespace ClaudeProfiles.Services;

/// <summary>Finds the installed Claude desktop app and starts instances of it.</summary>
public static class ClaudeApp
{
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string? FindLauncher()
    {
        var candidates = new[]
        {
            // Microsoft Store / MSIX install: the app execution alias accepts arguments.
            Path.Combine(Local, @"Microsoft\WindowsApps\claude-desktop.exe"),
            Path.Combine(Local, @"Microsoft\WindowsApps\claude.exe"),
            // Classic installer.
            Path.Combine(Local, @"AnthropicClaude\claude.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsInstalled => FindLauncher() != null;

    /// <summary>Starts (or brings forward) the instance that owns <paramref name="dataDir"/>.
    /// A null dataDir means the regular Claude app.</summary>
    public static void Launch(string? dataDir, string? deepLink = null)
    {
        var exe = FindLauncher() ?? throw new InvalidOperationException("Claude isn't installed.");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (dataDir != null) psi.ArgumentList.Add($"--user-data-dir={dataDir}");
        if (deepLink != null) psi.ArgumentList.Add(deepLink);
        Process.Start(psi);
        Log.Write($"launch dataDir={dataDir ?? "<main>"} link={(deepLink == null ? "none" : Redact(deepLink))}");
    }

    public static string Redact(string link)
    {
        var q = link.IndexOfAny(new[] { '?', '#' });
        return q < 0 ? link : link[..q] + "?...";
    }
}
