using System.Net;
using System.Net.Sockets;
using Aoe.Protocol;
using NAudio.Wave;

namespace Aoe.Beta;

public enum BetaStatus
{
    SpeachesUnavailable,
    Ready,
    Recording,
    Error,

    /// <summary>Transient: acknowledged the capture request from Alpha (cyan flash).</summary>
    AckRequest,

    /// <summary>Transient: received the transcript back from Speaches (magenta flash).</summary>
    GotResult,
}

/// <summary>
/// Beta owns the microphone AND the Speaches connection. On command from Alpha it captures the
/// mic locally, then transcribes via Speaches and sends only the resulting text back to Alpha.
/// The tray icon reflects Speaches connectivity (gray/green) and recording (red).
/// </summary>
public sealed class BetaService : IDisposable
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    private readonly BetaConfig _config;
    private readonly SpeachesClient _speaches;
    private readonly CancellationTokenSource _cts = new();

    private WaveInEvent? _waveIn;
    private FramedPeer? _peer;
    private MemoryStream _audioBuffer = new();
    private readonly object _bufLock = new();
    private volatile bool _capturing;
    private volatile bool _recording;
    private volatile bool _speachesHealthy;

    private const int FlashMs = 500;
    private long _statusToken;
    private long _flashUntilTicks;

    public event Action<BetaStatus, string?>? StatusChanged;

    public BetaService(BetaConfig config)
    {
        _config = config;
        _speaches = new SpeachesClient(config);
    }

    public void Start()
    {
        _ = Task.Run(() => RunAsync(_cts.Token));
        _ = Task.Run(() => HealthLoopAsync(_cts.Token));
    }

    // ---- Speaches health monitoring (drives gray vs green when idle) ----

    private async Task HealthLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool healthy = await _speaches.IsHealthyAsync(ct).ConfigureAwait(false);
            if (healthy != _speachesHealthy)
            {
                _speachesHealthy = healthy;
                Log.Info($"Speaches health: {(healthy ? "up" : "down")}");
                RefreshIdleStatus();
            }
            else
            {
                _speachesHealthy = healthy;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(7), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Sets the idle icon (green if Speaches is up, else gray). No-op while recording or flashing.</summary>
    private void RefreshIdleStatus()
    {
        if (_recording)
            return;
        if (Interlocked.Read(ref _flashUntilTicks) > DateTime.UtcNow.Ticks)
            return;
        Report(_speachesHealthy ? BetaStatus.Ready : BetaStatus.SpeachesUnavailable,
               _speachesHealthy ? "Speaches ready" : "Speaches unavailable");
    }

    /// <summary>Shows a transient status for ~0.5s, then reverts to the idle icon.</summary>
    private void Flash(BetaStatus status, string? detail)
    {
        long token = Interlocked.Increment(ref _statusToken);
        Interlocked.Exchange(ref _flashUntilTicks, DateTime.UtcNow.AddMilliseconds(FlashMs).Ticks);
        Report(status, detail);
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(FlashMs, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (Interlocked.Read(ref _statusToken) != token)
                return; // a newer flash/status superseded this one
            Interlocked.Exchange(ref _flashUntilTicks, 0);
            RefreshIdleStatus();
        });
    }

    // ---- Alpha control connection ----

    private async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _config.Port);
        try
        {
            listener.Start();
            Log.Info($"listening on port {_config.Port}");
            RefreshIdleStatus();

            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                await HandleClientAsync(client, ct).ConfigureAwait(false);
                RefreshIdleStatus();
            }
        }
        catch (Exception ex)
        {
            Log.Error("listener error", ex);
            Report(BetaStatus.Error, ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            client.NoDelay = true;
            var peer = new FramedPeer(stream);
            _peer = peer;
            Log.Info($"Alpha connected: {RemoteName(client)}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Frame? frame = await peer.ReadFrameAsync(ct).ConfigureAwait(false);
                    if (frame is null)
                        break;
                    if (frame.Value.Kind != FrameKind.Control)
                        continue;

                    var msg = FrameProtocol.DecodeControl(frame.Value);
                    HandleControl(peer, msg);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error("client loop error", ex);
            }
            finally
            {
                Log.Info("Alpha disconnected");
                StopCapture();
                _peer = null;
                peer.Dispose();
            }
        }
    }

    private void HandleControl(FramedPeer peer, ControlMessage msg)
    {
        switch (msg.Type)
        {
            case ControlType.StartCapture:
                StartCapture(msg.SessionId);
                break;
            case ControlType.StopCapture:
                FinishCapture(peer, msg.SessionId);
                break;
        }
    }

    // ---- Microphone capture ----

    private void StartCapture(long sessionId)
    {
        StopCapture();
        lock (_bufLock) { _audioBuffer = new MemoryStream(); }

        try
        {
            var waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = 50,
            };
            waveIn.DataAvailable += OnDataAvailable;
            _waveIn = waveIn;
            _capturing = true;
            _recording = true;
            waveIn.StartRecording();
            Log.Info($"capture started: session {sessionId}, {SampleRate} Hz x{Channels}");
            Report(BetaStatus.Recording, "Recording");
        }
        catch (Exception ex)
        {
            // Most commonly: no recording device / mic permission denied.
            Log.Error("failed to start microphone capture", ex);
            _recording = false;
            Report(BetaStatus.Error, $"mic: {ex.Message}");
        }
    }

    private void StopCapture()
    {
        _capturing = false;
        if (_waveIn is { } waveIn)
        {
            _waveIn = null;
            try
            {
                waveIn.DataAvailable -= OnDataAvailable;
                waveIn.StopRecording();
                waveIn.Dispose();
            }
            catch { /* ignore teardown races */ }
        }
    }

    /// <summary>Stops capture, then transcribes the buffered audio and sends text back to Alpha.</summary>
    private void FinishCapture(FramedPeer peer, long sessionId)
    {
        StopCapture();
        _recording = false;

        byte[] pcm;
        lock (_bufLock) { pcm = _audioBuffer.ToArray(); }

        // Cyan: acknowledge we received the STT request from Alpha.
        Flash(BetaStatus.AckRequest, "request received");

        double seconds = pcm.Length / (double)(SampleRate * Channels * 2);
        if (pcm.Length == 0)
        {
            Log.Warn($"session {sessionId}: no audio captured");
            return;
        }

        Log.Info($"session {sessionId}: {pcm.Length} bytes (~{seconds:F1}s); transcribing");
        // Fire-and-forget so rapid-fire PTT requests don't block each other; text returns as ready.
        _ = TranscribeAndReplyAsync(peer, sessionId, pcm, _cts.Token);
    }

    private async Task TranscribeAndReplyAsync(FramedPeer peer, long sessionId, byte[] pcm, CancellationToken ct)
    {
        try
        {
            byte[] wav = WavBuilder.BuildPcm16(pcm, SampleRate, Channels);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string text = await _speaches.TranscribeAsync(wav, ct).ConfigureAwait(false);
            sw.Stop();
            Log.Info($"session {sessionId}: transcript ({sw.ElapsedMilliseconds} ms): \"{text}\"");
            // Magenta: got the transcript back from Speaches.
            Flash(BetaStatus.GotResult, "transcript received");
            await peer.SendControlAsync(ControlMessage.Transcript(sessionId, text), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error($"session {sessionId}: transcription failed", ex);
            Flash(BetaStatus.Error, ex.Message);
            try { await peer.SendControlAsync(ControlMessage.ErrorMessage($"transcription: {ex.Message}"), ct).ConfigureAwait(false); }
            catch { /* Alpha may have disconnected */ }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_capturing || e.BytesRecorded <= 0)
            return;
        lock (_bufLock) { _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded); }
    }

    private static string RemoteName(TcpClient client)
    {
        try { return client.Client.RemoteEndPoint?.ToString() ?? "client"; }
        catch { return "client"; }
    }

    private void Report(BetaStatus status, string? detail) => StatusChanged?.Invoke(status, detail);

    public void Dispose()
    {
        _cts.Cancel();
        StopCapture();
        _cts.Dispose();
    }
}
