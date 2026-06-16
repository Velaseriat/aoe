using System.Text;

namespace Aoe.Alpha;

/// <summary>Wraps raw little-endian 16-bit PCM samples in a minimal WAV (RIFF) container.</summary>
public static class WavBuilder
{
    public static byte[] BuildPcm16(byte[] pcm, int sampleRate, int channels)
    {
        const int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int dataLen = pcm.Length;

        using var ms = new MemoryStream(44 + dataLen);
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                       // PCM fmt chunk size
        w.Write((short)1);                 // audio format = PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write((short)bitsPerSample);

        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        w.Write(pcm);

        w.Flush();
        return ms.ToArray();
    }
}
