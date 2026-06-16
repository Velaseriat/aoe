using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Aoe.Alpha;

/// <summary>Minimal client for the OpenAI-compatible chat completions endpoint (assistant mode).</summary>
public sealed class OpenAiClient
{
    private readonly HttpClient _http;
    private readonly AlphaConfig _config;

    public OpenAiClient(AlphaConfig config, string apiKey)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Resolves the API key without ever committing it: OPENAI_API_KEY env var first, then the
    /// configured key file, then an "openai_secret" file next to the exe. Returns null if none found.
    /// </summary>
    public static string? ResolveApiKey(AlphaConfig config)
    {
        string? env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        if (!string.IsNullOrWhiteSpace(config.OpenAiKeyPath) && File.Exists(config.OpenAiKeyPath))
            return File.ReadAllText(config.OpenAiKeyPath).Trim();

        string local = Path.Combine(AppContext.BaseDirectory, "openai_secret");
        if (File.Exists(local))
            return File.ReadAllText(local).Trim();

        return null;
    }

    public async Task<string> AskAsync(string prompt, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _config.OpenAiModel,
            messages = new[]
            {
                new { role = "system", content = _config.OpenAiSystemPrompt },
                new { role = "user", content = prompt },
            },
        };

        string json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        string url = $"{_config.OpenAiBaseUrl.TrimEnd('/')}/chat/completions";
        using HttpResponseMessage resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI returned {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        string? text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return text?.Trim() ?? string.Empty;
    }
}
