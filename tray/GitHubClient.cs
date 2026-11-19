using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GitIt.Tray;

public sealed class GitHubException(string message) : Exception(message);

public static class GitHubClient
{
    private const string HitsPath = "hits.json";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GitIt-Tray/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task<HttpResponseMessage> GetAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        try
        {
            return await Http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitHubException("GitHub did not respond in time.");
        }
        catch (HttpRequestException e)
        {
            throw new GitHubException($"Could not reach GitHub: {e.Message}");
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new GitHubException("GitHub rejected the token (401). Update it in Settings.");
        if (!response.IsSuccessStatusCode)
            throw new GitHubException($"{what} failed: HTTP {(int)response.StatusCode}.");
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new GitHubException($"{what} returned something that is not JSON.");
        }
    }

    private static string RepoPath(string owner, string repo) =>
        $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";

    public static async Task<string> GetLoginAsync(string token, CancellationToken ct)
    {
        using var response = await GetAsync("user", token, ct);
        using var json = await ReadJsonAsync(response, "Looking up your GitHub user", ct);
        var login = json.RootElement.TryGetProperty("login", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString()
            : null;
        return Names.IsOwner(login) ? login! : throw new GitHubException("GitHub returned an unexpected user.");
    }

    // Returns hits newest first, in file order. Entries with an invalid name are skipped.
    public static async Task<IReadOnlyList<Hit>> GetHitsAsync(RepoConfig cfg, CancellationToken ct)
    {
        var basePath = RepoPath(cfg.Owner, cfg.Repo);
        using var response = await GetAsync($"{basePath}/contents/{HitsPath}", cfg.Token, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            using var repoResponse = await GetAsync(basePath, cfg.Token, ct);
            if (!repoResponse.IsSuccessStatusCode)
                throw new GitHubException($"Can't see {cfg.Owner}/{cfg.Repo}. Check Settings and the token's access.");
            return [];
        }

        string base64;
        using (var meta = await ReadJsonAsync(response, $"Reading {HitsPath}", ct))
        {
            var root = meta.RootElement;
            var encoding = GetString(root, "encoding");
            if (encoding == "base64")
            {
                base64 = GetString(root, "content");
            }
            else
            {
                // Files over 1 MB come back without inline content; fetch the blob instead.
                var sha = GetString(root, "sha");
                using var blobResponse = await GetAsync($"{basePath}/git/blobs/{Uri.EscapeDataString(sha)}", cfg.Token, ct);
                using var blob = await ReadJsonAsync(blobResponse, $"Reading {HitsPath}", ct);
                base64 = GetString(blob.RootElement, "content");
            }
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(base64.Replace("\n", "").Replace("\r", "")));
        }
        catch (FormatException)
        {
            throw new GitHubException($"{HitsPath} could not be decoded.");
        }
        return ParseHits(text);
    }

    private static IReadOnlyList<Hit> ParseHits(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
                throw new GitHubException($"{HitsPath} has no \"hits\" list.");

            var result = new List<Hit>(hits.GetArrayLength());
            foreach (var item in hits.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!Names.TryParseFullName(GetString(item, "fullName"), out var owner, out var repo)) continue;
                result.Add(new Hit(owner, repo, GetString(item, "description"), Dates.TryParseUtc(GetString(item, "addedAt"))));
            }
            return result;
        }
        catch (JsonException)
        {
            throw new GitHubException($"{HitsPath} is not valid JSON.");
        }
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
