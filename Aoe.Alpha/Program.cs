namespace Aoe.Alpha;

static class Program
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "aoe-alpha.log");

    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Fatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) =>
            Fatal("Application.ThreadException", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Log("UnobservedTaskException", e.Exception);

        try
        {
            Log("startup", null);
            ApplicationConfiguration.Initialize();
            Application.Run(new AlphaTrayContext());
            Log("clean exit", null);
        }
        catch (Exception ex)
        {
            Fatal("Main", ex);
        }
    }

    private static void Fatal(string where, Exception? ex)
    {
        Log(where, ex);
        try
        {
            MessageBox.Show(
                $"AOE Alpha crashed in {where}:\n\n{ex}",
                "AOE Alpha",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { /* nothing more we can do */ }
    }

    private static void Log(string where, Exception? ex)
    {
        try
        {
            string line = $"[{DateTime.Now:O}] {where}{(ex is null ? "" : ": " + ex)}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch { /* ignore logging failures */ }
    }
}
