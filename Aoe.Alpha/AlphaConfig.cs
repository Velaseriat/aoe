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

    /// <summary>System prompt steering the assistant toward concise, well-formatted answers.</summary>
    public string AssistantSystemPrompt { get; set; } =
        "You are a voice assistant whose replies are shown in a small desktop popup. Always answer in the distinctive "
        + "speaking style of Donald Trump: short punchy sentences, superlatives (tremendous, huge, the best, believe me), "
        + "repetition for emphasis, and a confident, boastful tone. Keep it brief and to the point (a few sentences); use "
        + "markdown such as short lists or code blocks only when it genuinely helps. The facts must stay accurate. Use the "
        + "web_search tool whenever the question concerns current events, recent facts, or anything you are not confident "
        + "about; otherwise answer directly. No preamble and no follow-up questions.";

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
