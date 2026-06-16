namespace Aoe.Protocol;

/// <summary>
/// Capture intent. Dictation transcribes to text on Alpha. Assistant is reserved for a
/// future STT -> LLM -> TTS round trip (a different joystick direction).
/// </summary>
public enum Mode
{
    Dictation = 0,
    Assistant = 1,
}

/// <summary>Discriminator for <see cref="ControlMessage"/>.</summary>
public enum ControlType
{
    StartCapture = 0,
    StopCapture = 1,
    CaptureStarted = 2,
    CaptureStopped = 3,
    Error = 4,

    /// <summary>Beta -&gt; Alpha: the transcribed text for a finished capture session.</summary>
    Transcript = 5,
}

/// <summary>
/// A single control message exchanged over the control frame (kind 0). All fields are optional
/// and only meaningful for certain <see cref="Type"/> values; this avoids JSON polymorphism.
/// </summary>
public sealed class ControlMessage
{
    public ControlType Type { get; set; }

    /// <summary>Correlates a capture session across start/audio/stop.</summary>
    public long SessionId { get; set; }

    /// <summary>Set on <see cref="ControlType.StartCapture"/>.</summary>
    public Mode Mode { get; set; }

    /// <summary>Set on <see cref="ControlType.CaptureStarted"/>.</summary>
    public int SampleRate { get; set; }

    /// <summary>Set on <see cref="ControlType.CaptureStarted"/>.</summary>
    public int Channels { get; set; }

    /// <summary>Set on <see cref="ControlType.Error"/>.</summary>
    public string? Message { get; set; }

    /// <summary>Set on <see cref="ControlType.Transcript"/>: the recognized text.</summary>
    public string? Text { get; set; }

    public static ControlMessage StartCapture(long sessionId, Mode mode) =>
        new() { Type = ControlType.StartCapture, SessionId = sessionId, Mode = mode };

    public static ControlMessage StopCapture(long sessionId) =>
        new() { Type = ControlType.StopCapture, SessionId = sessionId };

    public static ControlMessage CaptureStarted(long sessionId, int sampleRate, int channels) =>
        new()
        {
            Type = ControlType.CaptureStarted,
            SessionId = sessionId,
            SampleRate = sampleRate,
            Channels = channels,
        };

    public static ControlMessage CaptureStopped(long sessionId) =>
        new() { Type = ControlType.CaptureStopped, SessionId = sessionId };

    public static ControlMessage ErrorMessage(string message) =>
        new() { Type = ControlType.Error, Message = message };

    public static ControlMessage Transcript(long sessionId, string text) =>
        new() { Type = ControlType.Transcript, SessionId = sessionId, Text = text };
}
