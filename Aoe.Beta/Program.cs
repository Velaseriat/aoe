namespace Aoe.Beta;

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
            Application.Run(new BetaTrayContext());
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
                $"AOE Beta crashed in {where}:\n\n{ex}",
                "AOE Beta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { /* nothing more we can do */ }
    }
}
