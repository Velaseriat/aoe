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

    /// <summary>OpenAI (or compatible) base URL including /v1, used for assistant mode.</summary>
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Model name for assistant mode. Set this to whatever your account has access to.</summary>
    public string OpenAiModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Optional path to a file containing the API key. Ignored if the OPENAI_API_KEY environment
    /// variable is set (preferred). Never put the key in this file's repo-tracked config.
    /// </summary>
    public string? OpenAiKeyPath { get; set; }

    public string OpenAiSystemPrompt { get; set; } =
        "You are a concise voice assistant. Answer in 1-3 sentences unless asked for more detail. " +
        "If you are unsure or the question concerns a recent or current event, say so briefly rather than guessing.";

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
