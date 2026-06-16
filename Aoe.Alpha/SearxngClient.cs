using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace Aoe.Alpha;

/// <summary>
/// Minimal SearXNG client for the assistant's web_search tool. Returns a compact, model-friendly
/// digest of the top results (title, url, snippet) rather than raw JSON.
/// </summary>
public sealed class SearxngClient
{
    private readonly HttpClient _http;
    private readonly int _defaultCount;

    public SearxngClient(string baseUrl, int defaultCount)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _defaultCount = defaultCount;
    }

    public async Task<string> SearchAsync(string query, int count, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "No query provided.";

        int take = count > 0 ? count : _defaultCount;
        string url = $"search?q={Uri.EscapeDataString(query)}&format=json";

        SearxResponse? resp;
        try
        {
            resp = await _http.GetFromJsonAsync<SearxResponse>(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"Search failed: {ex.Message}";
        }

        if (resp?.Results is not { Count: > 0 })
            return "No results found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Top {Math.Min(take, resp.Results.Count)} results for \"{query}\":");
        int i = 0;
        foreach (SearxResult r in resp.Results)
        {
            if (i++ >= take)
                break;
            sb.AppendLine($"{i}. {r.Title}");
            if (!string.IsNullOrWhiteSpace(r.Content))
                sb.AppendLine($"   {Collapse(r.Content)}");
            if (!string.IsNullOrWhiteSpace(r.Url))
                sb.AppendLine($"   {r.Url}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Collapse(string s)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length <= 300 ? s : s[..300] + "...";
    }

    private sealed class SearxResponse
    {
        [JsonPropertyName("results")] public List<SearxResult>? Results { get; set; }
    }

    private sealed class SearxResult
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
