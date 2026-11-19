using System.Globalization;
using System.Text.RegularExpressions;

namespace GitIt.Tray;

public sealed record Hit(string Owner, string Repo, string Description, DateTimeOffset? AddedAt)
{
    public string FullName => $"{Owner}/{Repo}";

    // Built from the validated name, never taken from the stored file, so only
    // https://github.com links are ever opened or copied.
    public string Url => $"https://github.com/{Owner}/{Repo}";
}

public sealed record RepoConfig(string Owner, string Repo, string Token);

public static partial class Names
{
    public const string DefaultRepo = "my-gits";

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex OwnerRegex();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepoRegex();

    public static bool IsOwner(string? value) => value is not null && OwnerRegex().IsMatch(value);

    public static bool IsRepo(string? value) =>
        value is not null && value is not "." and not ".." && RepoRegex().IsMatch(value);

    public static bool TryParseFullName(string? value, out string owner, out string repo)
    {
        owner = repo = "";
        var parts = value?.Split('/');
        if (parts is not { Length: 2 } || !IsOwner(parts[0]) || !IsRepo(parts[1])) return false;
        (owner, repo) = (parts[0], parts[1]);
        return true;
    }
}

// The only date helper in the tray app. Stored dates are ISO-8601 UTC; a string
// without an offset is read as UTC, never as local time.
public static class Dates
{
    public static DateTimeOffset? TryParseUtc(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    public static string ToLocalDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public static string LocalTimeNow() => DateTimeOffset.Now.ToString("t", CultureInfo.CurrentCulture);
}
