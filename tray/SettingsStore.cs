using System.ComponentModel;
using System.Text.Json;
using Microsoft.Win32;

namespace GitIt.Tray;

// Owner and repo live in %APPDATA%\GitIt\settings.json. The token lives in
// Windows Credential Manager (see CredentialStore), never in the file.
public sealed class StoredSettings
{
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = Names.DefaultRepo;
}

public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitIt");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    // Returns null when settings or token are missing or fail validation.
    public static RepoConfig? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(FilePath));
            if (stored is null || !Names.IsOwner(stored.Owner) || !Names.IsRepo(stored.Repo)) return null;
            var token = CredentialStore.ReadToken();
            return string.IsNullOrWhiteSpace(token) ? null : new RepoConfig(stored.Owner, stored.Repo, token);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or Win32Exception)
        {
            return null;
        }
    }

    // Throws IOException, UnauthorizedAccessException or Win32Exception on failure.
    public static void Save(RepoConfig cfg)
    {
        CredentialStore.WriteToken(cfg.Token);
        Directory.CreateDirectory(Dir);
        var stored = new StoredSettings { Owner = cfg.Owner, Repo = cfg.Repo };
        var temp = Path.Combine(Dir, $"settings.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, overwrite: true);
    }
}

public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GitItTray";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
