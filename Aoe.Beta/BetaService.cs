using System.Net;
using System.Net.Sockets;
using Aoe.Protocol;
using NAudio.Wave;

namespace Aoe.Beta;

public enum BetaStatus
{
    Listening,
    ClientConnected,
    Recording,
    Error,
}

/// <summary>
/// TCP control server that captures the default microphone on command and streams 16 kHz mono
/// 16-bit PCM to the connected Alpha client.
/// </summary>
public sealed class BetaService : IDisposable
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();

    private WaveInEvent? _waveIn;
    private FramedPeer? _peer;
    private long _activeSession;
    private volatile bool _capturing;

    public event Action<BetaStatus, string?>? StatusChanged;

    public BetaService(int port) => _port = port;

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        try
        {
            listener.Start();
            Report(BetaStatus.Listening, $"Listening on port {_port}");

            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                await HandleClientAsync(client, ct).ConfigureAwait(false);
                Report(BetaStatus.Listening, $"Listening on port {_port}");
            }
        }
        catch (Exception ex)
        {
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
            Report(BetaStatus.ClientConnected, RemoteName(client));

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Frame? frame = await peer.ReadFrameAsync(ct).ConfigureAwait(false);
                    if (frame is null)
                        break; // client disconnected

                    if (frame.Value.Kind != FrameKind.Control)
                        continue;

                    var msg = FrameProtocol.DecodeControl(frame.Value);
                    await HandleControlAsync(peer, msg, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Report(BetaStatus.Error, ex.Message);
            }
            finally
            {
                StopCapture();
                _peer = null;
                peer.Dispose();
            }
        }
    }

    private async Task HandleControlAsync(FramedPeer peer, ControlMessage msg, CancellationToken ct)
    {
        switch (msg.Type)
        {
            case ControlType.StartCapture:
                StartCapture(msg.SessionId);
                await peer.SendControlAsync(
                    ControlMessage.CaptureStarted(msg.SessionId, SampleRate, Channels), ct).ConfigureAwait(false);
                break;

            case ControlType.StopCapture:
                StopCapture();
                await peer.SendControlAsync(ControlMessage.CaptureStopped(msg.SessionId), ct).ConfigureAwait(false);
                break;
        }
    }

    private void StartCapture(long sessionId)
    {
        StopCapture();
        _activeSession = sessionId;

        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = 50,
        };
        waveIn.DataAvailable += OnDataAvailable;
        _waveIn = waveIn;
        _capturing = true;
        waveIn.StartRecording();
        Report(BetaStatus.Recording, "Recording");
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

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_capturing || _peer is not { } peer || e.BytesRecorded <= 0)
            return;

        // Copy only the valid bytes; the NAudio buffer is reused.
        byte[] pcm = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);

        try
        {
            // Synchronous send preserves frame order from the capture thread.
            peer.SendAudioAsync(pcm).GetAwaiter().GetResult();
        }
        catch
        {
            // Connection likely dropped; capture will be torn down by the read loop.
        }
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
