using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Aoe.Alpha;

/// <summary>
/// Minimal OpenAI-compatible chat client used for the assistant mode. Points at local Gemma served
/// by Ollama (over Caddy TLS). TLS validates against the Felsan root CA in the Windows trust store.
/// </summary>
public sealed class GemmaClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _systemPrompt;

    public GemmaClient(string baseUrl, string model, string systemPrompt)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(120),
        };
        _model = model;
        _systemPrompt = systemPrompt;
    }

    public async Task<string> AskAsync(string question, CancellationToken ct)
    {
        var req = new ChatRequest
        {
            Model = _model,
            Stream = false,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = _systemPrompt },
                new ChatMessage { Role = "user", Content = question },
            },
        };

        using HttpResponseMessage resp = await _http.PostAsJsonAsync("chat/completions", req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        ChatResponse? parsed = await resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct).ConfigureAwait(false);
        return parsed?.Choices is { Length: > 0 } choices
            ? choices[0].Message?.Content?.Trim() ?? string.Empty
            : string.Empty;
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public Choice[]? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
