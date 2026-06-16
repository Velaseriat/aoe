using System.Text.Json;
using Aoe.Protocol;

namespace Aoe.Alpha;

/// <summary>
/// Runtime configuration, loaded from appsettings.json next to the exe (all fields optional).
/// </summary>
public sealed class AlphaConfig
{
    public string BetaHost { get; set; } = "127.0.0.1";
    public int BetaPort { get; set; } = FrameProtocol.DefaultPort;

    /// <summary>Virtual-key code of the dictation push-to-talk key. Default 0xA3 = VK_RCONTROL (right Ctrl).</summary>
    public int PushToTalkVk { get; set; } = 0xA3;

    /// <summary>Virtual-key code of the assistant push-to-talk key. Default 0xA5 = VK_RMENU (right Alt).</summary>
    public int AssistantPushToTalkVk { get; set; } = 0xA5;

    /// <summary>If true, paste via clipboard + Ctrl+V; otherwise type characters directly.</summary>
    public bool InjectViaClipboard { get; set; } = true;

    /// <summary>OpenAI-compatible base URL for the assistant LLM (local Gemma via Ollama, over Caddy TLS).</summary>
    public string AssistantBaseUrl { get; set; } = "https://coom.felsan.net:11443/v1";

    /// <summary>Model name for the assistant LLM. gemma4 supports native tool-calling.</summary>
    public string AssistantModel { get; set; } = "gemma4:latest";

    /// <summary>Base URL of the SearXNG instance used for the web_search tool (over Caddy TLS).</summary>
    public string SearxngBaseUrl { get; set; } = "https://coom.felsan.net:8443";

    /// <summary>How many web results to feed back to the model per search.</summary>
    public int SearchResultCount { get; set; } = 5;

    /// <summary>Safety cap on tool-call round trips before forcing a final answer.</summary>
    public int AssistantMaxToolIterations { get; set; } = 4;

    /// <summary>System prompt steering the assistant toward short, toast-friendly answers.</summary>
    public string AssistantSystemPrompt { get; set; } =
        "You are a concise voice assistant whose replies are shown in a small desktop notification with a hard "
        + "limit of 255 characters (about 40 words). Use the web_search tool whenever the question concerns current "
        + "events, recent facts, or anything you are not confident about; otherwise answer directly. After any search, "
        + "give your final answer under the limit: 1-2 short sentences, no preamble, no follow-up questions, no markdown.";

    public static AlphaConfig Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new AlphaConfig();

        try
        {
            string json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<AlphaConfig>(json, opts) ?? new AlphaConfig();
        }
        catch
        {
            return new AlphaConfig();
        }
    }
}
