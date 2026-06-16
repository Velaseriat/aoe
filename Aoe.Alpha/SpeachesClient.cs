using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aoe.Alpha;

/// <summary>Minimal client for the Speaches (OpenAI-compatible) transcription endpoint.</summary>
public sealed class SpeachesClient
{
    private readonly HttpClient _http;
    private readonly AlphaConfig _config;

    public SpeachesClient(AlphaConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        if (!string.IsNullOrWhiteSpace(config.SpeachesApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.SpeachesApiKey);
    }

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent(_config.SpeachesModel), "model");
        form.Add(new StringContent("json"), "response_format");
        if (!string.IsNullOrWhiteSpace(_config.Language))
            form.Add(new StringContent(_config.Language), "language");

        string url = $"{_config.SpeachesBaseUrl.TrimEnd('/')}/audio/transcriptions";
        using HttpResponseMessage resp = await _http.PostAsync(url, form, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Speaches returned {(int)resp.StatusCode}: {body}");

        try
        {
            var parsed = JsonSerializer.Deserialize<TranscriptionResponse>(body);
            return parsed?.Text?.Trim() ?? string.Empty;
        }
        catch (JsonException)
        {
            // Some response formats return plain text.
            return body.Trim();
        }
    }

    private sealed class TranscriptionResponse
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
