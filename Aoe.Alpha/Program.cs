namespace Aoe.Alpha;

static class Program
{
    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Fatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) =>
            Fatal("Application.ThreadException", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Log.Error("UnobservedTaskException", e.Exception);

        try
        {
            Log.Info("==== startup ====");
            ApplicationConfiguration.Initialize();
            Application.Run(new AlphaTrayContext());
            Log.Info("clean exit");
        }
        catch (Exception ex)
        {
            Fatal("Main", ex);
        }
    }

    private static void Fatal(string where, Exception? ex)
    {
        Log.Error($"FATAL in {where}", ex);
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
}
