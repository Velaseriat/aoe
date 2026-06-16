using System.Text.Json;
using Aoe.Protocol;

namespace Aoe.Beta;

/// <summary>
/// Beta runtime configuration, loaded from appsettings.json next to the exe (all fields optional).
/// Beta owns the Speaches connection since it is the machine with the microphone.
/// </summary>
public sealed class BetaConfig
{
    /// <summary>TCP port Beta listens on for Alpha.</summary>
    public int Port { get; set; } = FrameProtocol.DefaultPort;

    /// <summary>Speaches base URL including /v1.</summary>
    public string SpeachesBaseUrl { get; set; } = "http://127.0.0.1:8000/v1";
    public string SpeachesModel { get; set; } = "deepdml/faster-whisper-large-v3-turbo-ct2";
    public string? SpeachesApiKey { get; set; }

    /// <summary>Optional language hint (e.g. "en"); null lets Whisper auto-detect.</summary>
    public string? Language { get; set; } = "en";

    public static BetaConfig Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        BetaConfig config = new();
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                config = JsonSerializer.Deserialize<BetaConfig>(json, opts) ?? new BetaConfig();
            }
            catch
            {
                config = new BetaConfig();
            }
        }

        // Environment override kept for convenience / backwards compatibility.
        string? env = Environment.GetEnvironmentVariable("AOE_PORT");
        if (int.TryParse(env, out int p))
            config.Port = p;

        return config;
    }
}
