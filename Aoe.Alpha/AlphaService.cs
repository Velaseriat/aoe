using System.Collections.Concurrent;
using System.Net.Sockets;
using Aoe.Protocol;

namespace Aoe.Alpha;

public enum AlphaStatus
{
    Disconnected,
    Connected,
    Recording,
    Thinking,
    Error,
}

/// <summary>
/// Alpha is just a screen + keyboard: it detects the PTT key, tells Beta when to capture, and
/// injects whatever transcript text Beta sends back. All audio + Speaches work happens on Beta.
/// </summary>
public sealed class AlphaService : IDisposable
{
    private readonly AlphaConfig _config;
    private readonly Action<Action> _postToUi;
    private readonly GemmaClient _gemma;
    private readonly SearxngClient _searx;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, Mode> _sessionModes = new();

    private FramedPeer? _peer;
    private volatile bool _connected;
    private long _sessionCounter;
    private long _currentSession = -1;
    private int _activeVk = -1; // which PTT key started the active capture (-1 = none)

    public event Action<AlphaStatus, string?>? StatusChanged;

    /// <summary>Raised when the assistant has an answer to surface as a toast (title, message).</summary>
    public event Action<string, string>? Notify;

    public AlphaService(AlphaConfig config, Action<Action> postToUi)
    {
        _config = config;
        _postToUi = postToUi;
        _gemma = new GemmaClient(config.AssistantBaseUrl, config.AssistantModel, config.AssistantSystemPrompt);
        _searx = new SearxngClient(config.SearxngBaseUrl, config.SearchResultCount);
    }

    private static readonly ToolSpec WebSearchTool = new(
        "web_search",
        "Search the web for current, recent, or factual information when you are unsure or the question concerns current events. Returns the top results with titles, snippets, and URLs.",
        new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "The search query" },
            },
            required = new[] { "query" },
        });

    public void Start() => _ = Task.Run(() => ConnectLoopAsync(_cts.Token));

    /// <summary>Called on PTT key-down (UI thread). <paramref name="mode"/> is chosen by which key fired.</summary>
    public void BeginCapture(int vk, Mode mode)
    {
        FramedPeer? peer = _peer;
        if (!_connected || peer is null)
        {
            Log.Warn("PTT down but Beta not connected");
            Report(AlphaStatus.Disconnected, "Beta not connected");
            return;
        }

        // Captures are strictly sequential; ignore the second key if one is already held.
        if (_activeVk != -1)
        {
            Log.Warn($"PTT down vk=0x{vk:X2} ignored (capture already active on vk=0x{_activeVk:X2})");
            return;
        }

        long id = Interlocked.Increment(ref _sessionCounter);
        _currentSession = id;
        _activeVk = vk;
        _sessionModes[id] = mode;
        Log.Info($"PTT down vk=0x{vk:X2} mode={mode} -> StartCapture session {id}");
        Report(AlphaStatus.Recording, null);
        _ = SafeSendAsync(peer, ControlMessage.StartCapture(id, mode));
    }

    /// <summary>Called on PTT key-up (UI thread). Only the key that started the capture ends it.</summary>
    public void EndCapture(int vk)
    {
        if (vk != _activeVk)
            return;

        FramedPeer? peer = _peer;
        long id = _currentSession;
        _currentSession = -1;
        _activeVk = -1;
        if (peer is null || id < 0)
            return;
        Log.Info($"PTT up -> StopCapture session {id}");
        // Transcription happens on Beta; the transcript arrives later as a control message.
        _ = SafeSendAsync(peer, ControlMessage.StopCapture(id));
        if (_connected)
            Report(AlphaStatus.Connected, null);
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_config.BetaHost, _config.BetaPort, ct).ConfigureAwait(false);
                client.NoDelay = true;
                using var stream = client.GetStream();
                var peer = new FramedPeer(stream);
                _peer = peer;
                _connected = true;
                Log.Info($"connected to Beta {_config.BetaHost}:{_config.BetaPort}");
                Report(AlphaStatus.Connected, $"{_config.BetaHost}:{_config.BetaPort}");

                await ReadLoopAsync(peer, ct).ConfigureAwait(false);
                Log.Warn("read loop ended (Beta closed connection)");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Warn($"connection error: {ex.Message}");
                Report(AlphaStatus.Disconnected, ex.Message);
            }
            finally
            {
                _connected = false;
                _peer = null;
            }

            if (!ct.IsCancellationRequested)
            {
                Report(AlphaStatus.Disconnected, "retrying");
                try { await Task.Delay(2000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ReadLoopAsync(FramedPeer peer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Frame? frame = await peer.ReadFrameAsync(ct).ConfigureAwait(false);
            if (frame is null)
                break;
            if (frame.Value.Kind != FrameKind.Control)
                continue;

            var msg = FrameProtocol.DecodeControl(frame.Value);
            switch (msg.Type)
            {
                case ControlType.Transcript:
                    string text = msg.Text ?? string.Empty;
                    Mode mode = _sessionModes.TryRemove(msg.SessionId, out Mode m) ? m : Mode.Dictation;
                    Log.Info($"transcript (session {msg.SessionId}, mode {mode}): \"{text}\"");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (mode == Mode.Assistant)
                            _ = HandleAssistantAsync(text.Trim());
                        else
                            // Trailing space so back-to-back dictations don't run together.
                            _postToUi(() => Inject(text.TrimEnd() + " "));
                    }
                    break;
                case ControlType.Error:
                    Log.Error($"Beta error: {msg.Message}");
                    Report(AlphaStatus.Error, msg.Message);
                    break;
            }
        }
    }

    /// <summary>Assistant mode: ask Gemma (with web_search) and surface the answer as a toast.</summary>
    private async Task HandleAssistantAsync(string question)
    {
        Report(AlphaStatus.Thinking, null);
        try
        {
            string answer = await _gemma.AskAsync(
                question,
                new[] { WebSearchTool },
                ExecuteToolAsync,
                _config.AssistantMaxToolIterations,
                _cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(answer))
                answer = "(no answer)";
            Log.Info($"assistant answer: \"{answer}\"");
            // Raw markdown; the popup renders it (the balloon fallback strips it).
            Notify?.Invoke(question, answer);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("assistant request failed", ex);
            Notify?.Invoke("Assistant error", ex.Message);
            Report(AlphaStatus.Error, $"assistant: {ex.Message}");
        }
        finally
        {
            if (_connected && _currentSession < 0)
                Report(AlphaStatus.Connected, null);
        }
    }

    /// <summary>Runs a tool the model asked for. Currently only web_search via SearXNG.</summary>
    private async Task<string> ExecuteToolAsync(string name, string argumentsJson, CancellationToken ct)
    {
        if (!string.Equals(name, "web_search", StringComparison.OrdinalIgnoreCase))
            return $"Unknown tool: {name}";

        string query;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            query = doc.RootElement.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        }
        catch
        {
            query = "";
        }

        Log.Info($"tool web_search: \"{query}\"");
        Report(AlphaStatus.Thinking, $"searching: {query}");
        return await _searx.SearchAsync(query, _config.SearchResultCount, ct).ConfigureAwait(false);
    }

    private void Inject(string text)
    {
        try
        {
            if (_config.InjectViaClipboard)
                TextInjector.PasteViaClipboard(text);
            else
                TextInjector.TypeUnicode(text);
            Log.Info($"injected {text.Length} chars via {(_config.InjectViaClipboard ? "clipboard" : "keystrokes")}");
        }
        catch (Exception ex)
        {
            Log.Error("inject failed", ex);
            Report(AlphaStatus.Error, $"inject failed: {ex.Message}");
        }
    }

    private async Task SafeSendAsync(FramedPeer peer, ControlMessage msg)
    {
        try { await peer.SendControlAsync(msg, _cts.Token).ConfigureAwait(false); }
        catch (Exception ex) { Report(AlphaStatus.Error, ex.Message); }
    }

    private void Report(AlphaStatus status, string? detail) => StatusChanged?.Invoke(status, detail);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
