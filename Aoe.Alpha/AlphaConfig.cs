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

    /// <summary>Speaches base URL including /v1.</summary>
    public string SpeachesBaseUrl { get; set; } = "http://127.0.0.1:8000/v1";
    public string SpeachesModel { get; set; } = "deepdml/faster-whisper-large-v3-turbo-ct2";
    public string? SpeachesApiKey { get; set; }

    /// <summary>Optional language hint (e.g. "en"); null lets Whisper auto-detect.</summary>
    public string? Language { get; set; } = "en";

    /// <summary>Virtual-key code of the push-to-talk key. Default 0x87 = VK_F24.</summary>
    public int PushToTalkVk { get; set; } = 0x87;

    /// <summary>If true, paste via clipboard + Ctrl+V; otherwise type characters directly.</summary>
    public bool InjectViaClipboard { get; set; } = true;

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
