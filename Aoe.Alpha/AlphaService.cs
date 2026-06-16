using System.Net.Sockets;
using Aoe.Protocol;

namespace Aoe.Alpha;

public enum AlphaStatus
{
    Disconnected,
    Connected,
    Recording,
    Transcribing,
    Error,
}

/// <summary>
/// Orchestrates the dictation loop: maintains the connection to Beta, drives capture on PTT,
/// collects streamed audio, transcribes via Speaches, and injects the text on the UI thread.
/// </summary>
public sealed class AlphaService : IDisposable
{
    private readonly AlphaConfig _config;
    private readonly SpeachesClient _speaches;
    private readonly Action<Action> _postToUi;
    private readonly CancellationTokenSource _cts = new();

    private FramedPeer? _peer;
    private volatile bool _connected;

    private long _sessionCounter;
    private long _currentSession = -1;
    private int _sampleRate = BetaDefaults.SampleRate;
    private int _channels = BetaDefaults.Channels;

    private readonly object _bufLock = new();
    private MemoryStream _audioBuffer = new();

    public event Action<AlphaStatus, string?>? StatusChanged;

    public AlphaService(AlphaConfig config, Action<Action> postToUi)
    {
        _config = config;
        _postToUi = postToUi;
        _speaches = new SpeachesClient(config);
    }

    public void Start() => _ = Task.Run(() => ConnectLoopAsync(_cts.Token));

    /// <summary>Called on PTT key-down (UI thread).</summary>
    public void BeginCapture()
    {
        FramedPeer? peer = _peer;
        if (!_connected || peer is null)
        {
            Report(AlphaStatus.Disconnected, "Beta not connected");
            return;
        }

        long id = Interlocked.Increment(ref _sessionCounter);
        _currentSession = id;
        lock (_bufLock) { _audioBuffer = new MemoryStream(); }
        Report(AlphaStatus.Recording, null);
        _ = SafeSendAsync(peer, ControlMessage.StartCapture(id, Mode.Dictation));
    }

    /// <summary>Called on PTT key-up (UI thread).</summary>
    public void EndCapture()
    {
        FramedPeer? peer = _peer;
        long id = _currentSession;
        if (peer is null || id < 0)
            return;
        _ = SafeSendAsync(peer, ControlMessage.StopCapture(id));
        // Finalization happens when Beta replies CaptureStopped (ensures all audio arrived).
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
                Report(AlphaStatus.Connected, $"{_config.BetaHost}:{_config.BetaPort}");

                await ReadLoopAsync(peer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
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

            if (frame.Value.Kind == FrameKind.Audio)
            {
                lock (_bufLock) { _audioBuffer.Write(frame.Value.Payload, 0, frame.Value.Payload.Length); }
                continue;
            }

            var msg = FrameProtocol.DecodeControl(frame.Value);
            switch (msg.Type)
            {
                case ControlType.CaptureStarted:
                    _sampleRate = msg.SampleRate;
                    _channels = msg.Channels;
                    break;
                case ControlType.CaptureStopped:
                    await FinalizeAsync(ct).ConfigureAwait(false);
                    break;
                case ControlType.Error:
                    Report(AlphaStatus.Error, msg.Message);
                    break;
            }
        }
    }

    private async Task FinalizeAsync(CancellationToken ct)
    {
        byte[] pcm;
        lock (_bufLock) { pcm = _audioBuffer.ToArray(); }
        _currentSession = -1;

        if (pcm.Length == 0)
        {
            Report(AlphaStatus.Connected, "no audio");
            return;
        }

        Report(AlphaStatus.Transcribing, null);
        try
        {
            byte[] wav = WavBuilder.BuildPcm16(pcm, _sampleRate, _channels);
            string text = await _speaches.TranscribeAsync(wav, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
                _postToUi(() => Inject(text));
            Report(AlphaStatus.Connected, null);
        }
        catch (Exception ex)
        {
            Report(AlphaStatus.Error, ex.Message);
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
        }
        catch (Exception ex)
        {
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

    private static class BetaDefaults
    {
        public const int SampleRate = 16000;
        public const int Channels = 1;
    }
}
