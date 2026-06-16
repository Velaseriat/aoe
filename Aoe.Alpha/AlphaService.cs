using System.Net.Sockets;
using Aoe.Protocol;

namespace Aoe.Alpha;

public enum AlphaStatus
{
    Disconnected,
    Connected,
    Recording,
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
    private readonly CancellationTokenSource _cts = new();

    private FramedPeer? _peer;
    private volatile bool _connected;
    private long _sessionCounter;
    private long _currentSession = -1;

    public event Action<AlphaStatus, string?>? StatusChanged;

    public AlphaService(AlphaConfig config, Action<Action> postToUi)
    {
        _config = config;
        _postToUi = postToUi;
    }

    public void Start() => _ = Task.Run(() => ConnectLoopAsync(_cts.Token));

    /// <summary>Called on PTT key-down (UI thread).</summary>
    public void BeginCapture()
    {
        FramedPeer? peer = _peer;
        if (!_connected || peer is null)
        {
            Log.Warn("PTT down but Beta not connected");
            Report(AlphaStatus.Disconnected, "Beta not connected");
            return;
        }

        long id = Interlocked.Increment(ref _sessionCounter);
        _currentSession = id;
        Log.Info($"PTT down -> StartCapture session {id}");
        Report(AlphaStatus.Recording, null);
        _ = SafeSendAsync(peer, ControlMessage.StartCapture(id, Mode.Dictation));
    }

    /// <summary>Called on PTT key-up (UI thread).</summary>
    public void EndCapture()
    {
        FramedPeer? peer = _peer;
        long id = _currentSession;
        _currentSession = -1;
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
                    Log.Info($"transcript (session {msg.SessionId}): \"{text}\"");
                    if (!string.IsNullOrWhiteSpace(text))
                        // Trailing space so back-to-back dictations don't run together.
                        _postToUi(() => Inject(text.TrimEnd() + " "));
                    break;
                case ControlType.Error:
                    Log.Error($"Beta error: {msg.Message}");
                    Report(AlphaStatus.Error, msg.Message);
                    break;
            }
        }
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
