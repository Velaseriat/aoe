using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aoe.Protocol;

public enum FrameKind : byte
{
    Control = 0,
    Audio = 1,
}

/// <summary>One decoded frame off the wire.</summary>
public readonly record struct Frame(FrameKind Kind, byte[] Payload);

/// <summary>
/// Wire format: [4-byte big-endian payload length][1-byte kind][payload].
/// Control payloads are UTF-8 JSON <see cref="ControlMessage"/>; audio payloads are raw
/// little-endian 16-bit PCM samples.
/// </summary>
public static class FrameProtocol
{
    public const int DefaultPort = 38473;

    /// <summary>Max payload we will accept for a single frame (guards against bad data).</summary>
    public const int MaxPayloadBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteControlAsync(Stream stream, ControlMessage message, CancellationToken ct = default)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await WriteFrameAsync(stream, FrameKind.Control, json, ct).ConfigureAwait(false);
    }

    public static Task WriteAudioAsync(Stream stream, ReadOnlyMemory<byte> pcm, CancellationToken ct = default) =>
        WriteFrameAsync(stream, FrameKind.Audio, pcm, ct);

    public static async Task WriteFrameAsync(Stream stream, FrameKind kind, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        byte[] header = new byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), payload.Length);
        header[4] = (byte)kind;
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads exactly one frame. Returns null on a clean end-of-stream.</summary>
    public static async Task<Frame?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[5];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
        if (length < 0 || length > MaxPayloadBytes)
            throw new InvalidDataException($"Frame length {length} out of range.");

        var kind = (FrameKind)header[4];
        byte[] payload = new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, payload, ct).ConfigureAwait(false))
            return null;

        return new Frame(kind, payload);
    }

    public static ControlMessage DecodeControl(in Frame frame)
    {
        if (frame.Kind != FrameKind.Control)
            throw new InvalidOperationException($"Expected control frame, got {frame.Kind}.");
        return JsonSerializer.Deserialize<ControlMessage>(frame.Payload, JsonOptions)
            ?? throw new InvalidDataException("Control frame contained null JSON.");
    }

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer[read..], ct).ConfigureAwait(false);
            if (n == 0)
                return false;
            read += n;
        }
        return true;
    }
}
