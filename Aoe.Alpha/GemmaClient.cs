using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aoe.Alpha;

/// <summary>Declares a callable tool exposed to the model.</summary>
public sealed record ToolSpec(string Name, string Description, object Parameters);

/// <summary>Executes a tool call: given the tool name and raw JSON arguments, returns a text result.</summary>
public delegate Task<string> ToolExecutor(string name, string argumentsJson, CancellationToken ct);

/// <summary>
/// Minimal OpenAI-compatible chat client with a native tool-calling loop. Points at local Gemma 4
/// served by Ollama (over Caddy TLS); TLS validates against the Felsan root CA in the Windows store.
/// </summary>
public sealed class GemmaClient
{
    private static readonly JsonSerializerOptions SerOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _systemPrompt;

    public GemmaClient(string baseUrl, string model, string systemPrompt)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(180),
        };
        _model = model;
        _systemPrompt = systemPrompt;
    }

    /// <summary>
    /// Ask the model, letting it call tools as needed. Loops until the model returns a final answer
    /// or <paramref name="maxToolIterations"/> tool round trips are exhausted.
    /// </summary>
    public async Task<string> AskAsync(
        string question,
        IReadOnlyList<ToolSpec> tools,
        ToolExecutor executor,
        int maxToolIterations,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = _systemPrompt },
            new() { Role = "user", Content = question },
        };

        Tool[]? toolDefs = tools is { Count: > 0 }
            ? tools.Select(t => new Tool
            {
                Function = new ToolFunction { Name = t.Name, Description = t.Description, Parameters = t.Parameters },
            }).ToArray()
            : null;

        for (int iter = 0; iter <= maxToolIterations; iter++)
        {
            // On the final permitted pass, drop tools so the model is forced to answer.
            bool allowTools = toolDefs is not null && iter < maxToolIterations;
            ChatMessage reply = await SendAsync(messages, allowTools ? toolDefs : null, ct).ConfigureAwait(false);
            messages.Add(reply);

            if (reply.ToolCalls is not { Length: > 0 })
                return reply.Content?.Trim() ?? string.Empty;

            foreach (ToolCall call in reply.ToolCalls)
            {
                string result;
                try
                {
                    result = await executor(call.Function?.Name ?? "", call.Function?.Arguments ?? "{}", ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = $"Tool error: {ex.Message}";
                }

                messages.Add(new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = call.Id,
                    Content = result,
                });
            }
        }

        return string.Empty;
    }

    private async Task<ChatMessage> SendAsync(List<ChatMessage> messages, Tool[]? tools, CancellationToken ct)
    {
        var req = new ChatRequest { Model = _model, Stream = false, Messages = messages, Tools = tools };
        using HttpResponseMessage resp =
            await _http.PostAsJsonAsync("chat/completions", req, SerOpts, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        ChatResponse? parsed = await resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct).ConfigureAwait(false);
        return parsed?.Choices is { Length: > 0 } choices && choices[0].Message is { } m
            ? m
            : new ChatMessage { Role = "assistant", Content = string.Empty };
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
        [JsonPropertyName("tools")] public Tool[]? Tools { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("tool_calls")] public ToolCall[]? ToolCalls { get; set; }
        [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
    }

    private sealed class ToolCall
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public ToolCallFunction? Function { get; set; }
    }

    private sealed class ToolCallFunction
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    }

    private sealed class Tool
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public ToolFunction Function { get; set; } = new();
    }

    private sealed class ToolFunction
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("parameters")] public object Parameters { get; set; } = new { type = "object" };
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
