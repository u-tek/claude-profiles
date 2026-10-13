using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ClaudeProfiles.Services;

namespace ClaudeProfiles;

public enum ItemKind { Main, Profile, AddTile }

/// <summary>One card in the grid: the regular app, a profile, or the "new profile" tile.</summary>
public sealed class ProfileItem : INotifyPropertyChanged
{
    public ItemKind Kind { get; }
    public ProfileRecord? Record { get; }
    public AppKind App { get; }

    public ProfileItem(ItemKind kind, ProfileRecord? record = null, AppKind app = AppKind.Claude)
    {
        Kind = kind;
        Record = record;
        App = record?.App ?? app;
    }

    public bool IsMain => Kind == ItemKind.Main;
    public bool IsProfile => Kind == ItemKind.Profile;
    public bool IsCodex => App == AppKind.Codex;
    public string AppName => App.ToString();

    public string Name => IsMain ? MainName(App) : Record?.Name ?? "";
    public static string MainName(AppKind app) => $"Main {app}";
    public string Initial => Icons.Initial(Name);
    /// <summary>The profile's folder, or for the regular app, where it keeps its sign-in.</summary>
    public string DataDir => !IsMain ? Record!.DataDir : IsCodex ? CodexApp.MainHome : AppPaths.MainClaudeData;
    public Brush AvatarBrush => new SolidColorBrush(!IsMain ? Icons.ParseColor(Record!.Color) : IsCodex ? Icons.Ink : Icons.Clay);
    public string Caption => IsMain ? $"Your regular {AppName} app" : LastOpenedText;
    public string AddCaption => $"Another {AppName}, fully separate";

    string LastOpenedText => Record?.LastOpened is { } t ? "Opened " + Ago(t) : "Never opened";

    bool _running, _signedIn;
    string? _account, _sharedWith;
    DateTime? _lastAttempt;

    public bool IsRunning { get => _running; set { if (Set(ref _running, value)) Changed(nameof(StatusText), nameof(PrimaryLabel), nameof(IsWaiting)); } }
    public bool IsSignedIn { get => _signedIn; set { if (Set(ref _signedIn, value)) Changed(nameof(StatusText), nameof(IsWaiting)); } }
    public string? AccountUuid { get => _account; set => Set(ref _account, value); }
    public DateTime? LastAttempt { get => _lastAttempt; set => Set(ref _lastAttempt, value); }

    /// <summary>Running but not yet signed in: sign-in links will be routed here.</summary>
    public bool IsWaiting => IsProfile && IsRunning && !IsSignedIn;

    /// <summary>Names of other cards signed in to the same Claude account.</summary>
    public string? SharedWith { get => _sharedWith; set { if (Set(ref _sharedWith, value)) Changed(nameof(HasWarning), nameof(WarningText)); } }
    public bool HasWarning => SharedWith != null;
    public string WarningText => $"Same account as {SharedWith}";

    public string StatusText => (IsRunning, IsSignedIn) switch
    {
        (true, true) => "Running",
        (true, false) => IsProfile ? "Waiting for sign-in" : "Running · signed out",
        (false, true) => "Signed in",
        (false, false) => "Not signed in",
    };

    public string PrimaryLabel => IsRunning ? "Show" : "Open";

    public void Refresh() => Changed(nameof(Name), nameof(Initial), nameof(AvatarBrush), nameof(Caption));

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} d ago";
        return t.ToString("d MMM");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    void Changed(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
