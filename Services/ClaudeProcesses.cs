using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace ClaudeProfiles.Services;

public sealed record ClaudeProcess(int Pid, int ParentPid, string CommandLine, DateTime Started)
{
    /// <summary>Electron helper processes (renderer, GPU, …) carry --type=.</summary>
    public bool IsHelper => CommandLine.Contains("--type=", StringComparison.OrdinalIgnoreCase);

    /// <summary>Only the desktop app — Claude Code's CLI is also named claude.exe.</summary>
    public bool IsDesktopApp =>
        CommandLine.Contains(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) ||
        CommandLine.Contains(@"\WindowsApps\claude-desktop.exe", StringComparison.OrdinalIgnoreCase) ||
        CommandLine.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Codex desktop app, which runs as ChatGPT.exe from its Store package.</summary>
    public bool IsCodex => CommandLine.Contains(@"\WindowsApps\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase);

    public string? UserDataDir => ClaudeProcesses.ParseUserDataDir(CommandLine);
    public string? DeepLink => ClaudeProcesses.ParseDeepLink(CommandLine);
}

public static class ClaudeProcesses
{
    // Matches --user-data-dir="C:\a b", "--user-data-dir=C:\a b" (how a quoted argument with spaces arrives) and --user-data-dir=C:\ab.
    static readonly Regex DataDirRx = new(@"(?:""--user-data-dir=([^""]+)""|--user-data-dir=(?:""([^""]+)""|(\S+)))", RegexOptions.IgnoreCase);
    static readonly Regex LinkRx = new(@"(claude://[^""\s]+)", RegexOptions.IgnoreCase);

    public static string? ParseUserDataDir(string cmd)
    {
        var m = DataDirRx.Match(cmd);
        if (!m.Success) return null;
        return AppPaths.Normalize(m.Groups.Values.Skip(1).First(g => g.Success).Value);
    }

    public static string? ParseDeepLink(string cmd)
    {
        var m = LinkRx.Match(cmd);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>All running Claude and Codex desktop main processes (helpers and Claude Code excluded).</summary>
    public static List<ClaudeProcess> Snapshot()
    {
        var list = new List<ClaudeProcess>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, CommandLine, CreationDate FROM Win32_Process WHERE Name='claude.exe' OR Name='ChatGPT.exe'");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var p = FromWmi(o);
                    if (p != null && (p.IsDesktopApp || p.IsCodex) && !p.IsHelper) list.Add(p);
                }
            }
        }
        catch (Exception ex) { Log.Write("snapshot failed: " + ex.Message); }
        return list;
    }

    public static ClaudeProcess? FromWmi(ManagementBaseObject o)
    {
        var cmd = o["CommandLine"] as string;
        if (string.IsNullOrEmpty(cmd)) return null;
        var started = DateTime.Now;
        if (o["CreationDate"] is string cd)
        {
            try { started = ManagementDateTimeConverter.ToDateTime(cd); } catch { }
        }
        return new ClaudeProcess(Convert.ToInt32(o["ProcessId"]), Convert.ToInt32(o["ParentProcessId"]), cmd, started);
    }

    public static ClaudeProcess? MainFor(IEnumerable<ClaudeProcess> procs, string dataDir) =>
        procs.FirstOrDefault(p => string.Equals(p.UserDataDir, dataDir, StringComparison.OrdinalIgnoreCase));

    /// <summary>The regular app: no --user-data-dir, alive for more than a moment
    /// (a link hand-off process lives well under a second).</summary>
    public static ClaudeProcess? MainClaude(IEnumerable<ClaudeProcess> procs) =>
        procs.Where(p => !p.IsCodex && p.UserDataDir == null && DateTime.Now - p.Started > TimeSpan.FromSeconds(3))
             .OrderBy(p => p.Started).FirstOrDefault();

    /// <summary>The regular Codex app: the one started without --user-data-dir.</summary>
    public static ClaudeProcess? MainCodex(IEnumerable<ClaudeProcess> procs) =>
        procs.Where(p => p.IsCodex && p.UserDataDir == null).OrderBy(p => p.Started).FirstOrDefault();

    public static void KillTree(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex) { Log.Write($"kill {pid} failed: {ex.Message}"); }
    }
}
