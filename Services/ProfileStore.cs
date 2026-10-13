using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace ClaudeProfiles.Services;

public sealed class ProfileRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#D97757";
    public string DataDir { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime? LastOpened { get; set; }
}

public static class ProfileStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static readonly string[] Palette =
    {
        "#D97757", // clay
        "#4F86C6", // sky
        "#5E9E6E", // sage
        "#8E6BBF", // plum
        "#C99A2E", // ochre
        "#C9577A", // rose
        "#3A9A9A", // teal
        "#6B7280", // slate
    };

    public static List<ProfileRecord> Load()
    {
        if (File.Exists(AppPaths.ProfilesFile))
        {
            try
            {
                return JsonSerializer.Deserialize<List<ProfileRecord>>(File.ReadAllText(AppPaths.ProfilesFile)) ?? new();
            }
            catch (Exception ex)
            {
                // Never overwrite a file we couldn't read: keep it, then rebuild from the folders on disk.
                var backup = $"{AppPaths.ProfilesFile}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                Log.Write($"profiles.json unreadable ({ex.Message}); kept as {backup}");
                try { File.Copy(AppPaths.ProfilesFile, backup, overwrite: false); } catch { }
            }
        }

        var adopted = Adopt();
        Save(adopted);
        return adopted;
    }

    /// <summary>Recovers profiles from Claude data folders already on disk.</summary>
    static List<ProfileRecord> Adopt()
    {
        var adopted = new List<ProfileRecord>();
        int i = 0;
        foreach (var parent in new[] { AppPaths.Root, AppPaths.ProfilesDir })
        {
            if (!Directory.Exists(parent)) continue;
            foreach (var dir in Directory.GetDirectories(parent))
            {
                if (string.Equals(AppPaths.Normalize(dir), AppPaths.Normalize(AppPaths.ProfilesDir), StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(Path.Combine(dir, "Local State")) && !File.Exists(Path.Combine(dir, "config.json"))) continue;
                adopted.Add(new ProfileRecord
                {
                    Name = Regex.Replace(Path.GetFileName(dir), @"(?<=[A-Za-z])(?=\d)", " "),
                    DataDir = AppPaths.Normalize(dir),
                    Color = Palette[(1 + i++) % Palette.Length],
                });
            }
        }
        return adopted;
    }

    public static void Save(List<ProfileRecord> profiles)
    {
        Directory.CreateDirectory(AppPaths.Root);
        var tmp = AppPaths.ProfilesFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profiles, Json));
        File.Move(tmp, AppPaths.ProfilesFile, overwrite: true);
    }

    public static ProfileRecord Create(List<ProfileRecord> profiles, string name, string color)
    {
        var folder = SafeFolderName(name);
        var dir = Path.Combine(AppPaths.ProfilesDir, folder);
        for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(AppPaths.ProfilesDir, $"{folder} {n}");
        Directory.CreateDirectory(dir);

        var record = new ProfileRecord { Name = name.Trim(), Color = color, DataDir = AppPaths.Normalize(dir) };
        profiles.Add(record);
        Save(profiles);
        return record;
    }

    /// <summary>Removes the profile and sends its data folder to the Recycle Bin (recoverable).</summary>
    public static void Delete(List<ProfileRecord> profiles, ProfileRecord record)
    {
        if (Directory.Exists(record.DataDir))
            FileSystem.DeleteDirectory(record.DataDir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        profiles.Remove(record);
        Save(profiles);
    }

    /// <summary>Signs the profile out by recycling its data and starting with an empty folder.</summary>
    public static void Reset(ProfileRecord record)
    {
        if (Directory.Exists(record.DataDir))
            FileSystem.DeleteDirectory(record.DataDir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        Directory.CreateDirectory(record.DataDir);
    }

    public static string SuggestColor(List<ProfileRecord> profiles) =>
        Palette.FirstOrDefault(c => profiles.All(p => !string.Equals(p.Color, c, StringComparison.OrdinalIgnoreCase)))
        ?? Palette[profiles.Count % Palette.Length];

    static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim('.', ' ');
        return string.IsNullOrEmpty(clean) ? "Profile" : clean;
    }
}
