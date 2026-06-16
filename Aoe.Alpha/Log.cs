namespace Aoe.Alpha;

/// <summary>Lightweight append-only debug log written next to the executable (aoe-alpha.log).</summary>
public static class Log
{
    private static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "aoe-alpha.log");
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    public static void Write(string level, string message)
    {
        try
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {level,-5} {message}{Environment.NewLine}";
            lock (Gate) { File.AppendAllText(Path, line); }
        }
        catch { /* never let logging break the app */ }
    }
}
