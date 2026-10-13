using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeProfiles.Services;

public sealed record ProfileState(bool SignedIn, string? AccountUuid, DateTime? LastSignInAttempt);

/// <summary>Reads what a Claude data folder says about its sign-in, without touching it.</summary>
public static class ProfileInspector
{
    const string AuthMarker = "[Auth] Using system browser";

    public static ProfileState Read(string dataDir)
    {
        bool signedIn = false;
        string? account = null;
        try
        {
            var config = Path.Combine(dataDir, "config.json");
            if (File.Exists(config))
            {
                using var fs = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                if (root.TryGetProperty("windowSizeWasSignedIn", out var s) && s.ValueKind == JsonValueKind.True) signedIn = true;
                if (root.TryGetProperty("lastKnownAccountUuid", out var a) && a.ValueKind == JsonValueKind.String) account = a.GetString();
            }
        }
        catch { }
        return new ProfileState(signedIn, account, LastSignInAttempt(dataDir));
    }

    /// <summary>When this instance last sent the user to the browser to sign in.</summary>
    public static DateTime? LastSignInAttempt(string dataDir)
    {
        var logs = string.Equals(AppPaths.Normalize(dataDir), AppPaths.Normalize(AppPaths.MainClaudeData), StringComparison.OrdinalIgnoreCase)
            ? AppPaths.MainClaudeLogs
            : new[] { Path.Combine(dataDir, "logs", "main.log") };
        return logs.Select(LastAttemptIn).Max();
    }

    static DateTime? LastAttemptIn(string logPath)
    {
        var tail = ReadTail(logPath, 96 * 1024);
        if (tail == null) return null;
        var idx = tail.LastIndexOf(AuthMarker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var lineStart = tail.LastIndexOf('\n', idx) + 1;
        if (idx - lineStart < 19) return null;
        return DateTime.TryParseExact(tail.Substring(lineStart, 19), "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
    }

    static string? ReadTail(string path, int bytes)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var len = (int)Math.Min(bytes, fs.Length);
            fs.Seek(-len, SeekOrigin.End);
            var buf = new byte[len];
            fs.ReadExactly(buf);
            return Encoding.UTF8.GetString(buf);
        }
        catch { return null; }
    }
}
