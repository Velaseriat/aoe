namespace Aoe.Protocol;

/// <summary>
/// Wraps a duplex <see cref="Stream"/> with frame read/write. Writes are serialized so audio and
/// control frames from different threads never interleave on the wire.
/// </summary>
public sealed class FramedPeer : IDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FramedPeer(Stream stream) => _stream = stream;

    public async Task SendControlAsync(ControlMessage message, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try { await FrameProtocol.WriteControlAsync(_stream, message, ct).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try { await FrameProtocol.WriteAudioAsync(_stream, pcm, ct).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    public Task<Frame?> ReadFrameAsync(CancellationToken ct = default) =>
        FrameProtocol.ReadFrameAsync(_stream, ct);

    public void Dispose() => _writeLock.Dispose();
}
