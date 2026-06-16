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

    /// <summary>Model name for the assistant LLM.</summary>
    public string AssistantModel { get; set; } = "gemma3:4b";

    /// <summary>System prompt steering the assistant toward short, toast-friendly answers.</summary>
    public string AssistantSystemPrompt { get; set; } =
        "You are a concise voice assistant. Answer in 1-3 short sentences suitable for a desktop notification. "
        + "If you are unsure or the question concerns a recent or current event, say so briefly rather than guessing.";

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
