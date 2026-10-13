using System.Management;

namespace ClaudeProfiles.Services;

public sealed record SignInTarget(string Id, string Name, string DataDir, DateTime? LastAttempt, DateTime? LastOpened);

/// <summary>
/// Windows hands every claude:// link to the regular Claude app, so a profile that starts a
/// browser sign-in never gets the link back. The router watches for the short-lived process
/// Windows starts for that link, stops it before it reaches the regular app, and re-delivers
/// the link to the profile that is waiting for it.
/// </summary>
public sealed class SignInRouter : IDisposable
{
    public const string ProbePrefix = "claude://claude-profiles-probe";
    static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(20);

    readonly Func<IReadOnlyList<SignInTarget>> _waitingProfiles;
    ManagementEventWatcher? _watcher;
    readonly object _gate = new();

    public event Action<SignInTarget>? Routed;
    public event Action<int, TimeSpan>? ProbeCaught;

    public bool IsArmed { get; private set; }

    /// <summary>The regular app is open and signed out, so a link may well be for it.</summary>
    public volatile bool MainNeedsSignIn;
    public string Mode { get; private set; } = "off";

    public SignInRouter(Func<IReadOnlyList<SignInTarget>> waitingProfiles) => _waitingProfiles = waitingProfiles;

    public void SetArmed(bool armed)
    {
        lock (_gate)
        {
            if (armed == IsArmed) return;
            IsArmed = armed;
            if (armed) Start(); else Stop();
        }
    }

    void Start()
    {
        // Kernel trace events arrive instantly but need admin; fall back to fast polling.
        try
        {
            var w = new ManagementEventWatcher(new WqlEventQuery(
                "SELECT ProcessID FROM Win32_ProcessStartTrace WHERE ProcessName='claude.exe'"));
            w.EventArrived += (_, e) => OnTrace(Convert.ToInt32(e.NewEvent["ProcessID"]));
            w.Start();
            _watcher = w;
            Mode = "trace";
        }
        catch
        {
            var w = new ManagementEventWatcher(new WqlEventQuery(
                "__InstanceCreationEvent", TimeSpan.FromMilliseconds(100),
                "TargetInstance ISA 'Win32_Process' AND TargetInstance.Name = 'claude.exe'"));
            w.EventArrived += (_, e) =>
            {
                if (e.NewEvent["TargetInstance"] is ManagementBaseObject t && ClaudeProcesses.FromWmi(t) is { } p) Handle(p);
            };
            w.Start();
            _watcher = w;
            Mode = "poll";
        }
        Log.Write($"router armed ({Mode})");
    }

    void Stop()
    {
        try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
        _watcher = null;
        Mode = "off";
        Log.Write("router disarmed");
    }

    void OnTrace(int pid)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT ProcessId, ParentProcessId, CommandLine, CreationDate FROM Win32_Process WHERE ProcessId={pid}");
            foreach (ManagementObject o in s.Get())
                using (o) if (ClaudeProcesses.FromWmi(o) is { } p) Handle(p);
        }
        catch { }
    }

    void Handle(ClaudeProcess p)
    {
        // Only the hand-off process Windows starts for a link: the desktop app, no profile, carrying a claude:// link.
        if (!p.IsDesktopApp || p.IsHelper || p.UserDataDir != null) return;
        var link = p.DeepLink;
        if (link == null) return;

        if (link.StartsWith(ProbePrefix, StringComparison.OrdinalIgnoreCase))
        {
            ClaudeProcesses.KillTree(p.Pid);
            var lag = DateTime.Now - p.Started;
            Log.Write($"probe caught pid={p.Pid} after {lag.TotalMilliseconds:F0}ms");
            ProbeCaught?.Invoke(p.Pid, lag);
            return;
        }

        if (!IsSignInLink(link)) return;

        var target = PickTarget();
        if (target == null)
        {
            Log.Write($"sign-in link {ClaudeApp.Redact(link)} left alone: no profile waiting");
            return;
        }

        ClaudeProcesses.KillTree(p.Pid);
        Log.Write($"sign-in link caught pid={p.Pid} after {(DateTime.Now - p.Started).TotalMilliseconds:F0}ms -> {target.Name}");
        try
        {
            ClaudeApp.Launch(target.DataDir, link);
            Routed?.Invoke(target);
        }
        catch (Exception ex) { Log.Write("re-deliver failed: " + ex.Message); }
    }

    SignInTarget? PickTarget()
    {
        var waiting = _waitingProfiles();
        if (waiting.Count == 0) return null;

        var now = DateTime.Now;
        DateTime? Recent(DateTime? t) => t is { } v && now - v < AttemptWindow ? v : null;

        // The regular app started its own sign-in more recently than any profile: it's theirs.
        var mainAttempt = Recent(ProfileInspector.LastSignInAttempt(AppPaths.MainClaudeData));
        var best = waiting
            .Select(w => (w, attempt: Recent(ProfileInspector.LastSignInAttempt(w.DataDir)) ?? Recent(w.LastAttempt)))
            .OrderByDescending(x => x.attempt ?? DateTime.MinValue)
            .ThenByDescending(x => x.w.LastOpened ?? DateTime.MinValue)
            .First();

        if (mainAttempt != null && (best.attempt == null || mainAttempt > best.attempt)) return null;
        // Main is waiting too: only take the link when a profile visibly started this sign-in.
        if (MainNeedsSignIn && best.attempt == null) return null;
        return best.w;
    }

    public static bool IsSignInLink(string link) =>
        link.StartsWith("claude://login/", StringComparison.OrdinalIgnoreCase) ||
        link.Contains("magic-link", StringComparison.OrdinalIgnoreCase) ||
        link.Contains("/auth", StringComparison.OrdinalIgnoreCase) ||
        link.Contains("oauth", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => SetArmed(false);
}
