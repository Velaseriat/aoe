using Aoe.Protocol;

namespace Aoe.Beta;

/// <summary>Tray-only application context: no main window, just a status icon and the service.</summary>
public sealed class BetaTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly BetaService _service;
    private readonly SynchronizationContext _ui;
    private Icon? _currentIcon;

    public BetaTrayContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        int port = ResolvePort();

        _tray = new NotifyIcon
        {
            Icon = NewIcon(Color.Gray),
            Visible = true,
            Text = "AOE Beta - starting",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("AOE Beta (mic)").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;

        // A balloon toast pops up even when Windows hides the icon in the tray overflow.
        _tray.BalloonTipTitle = "AOE Beta";
        _tray.BalloonTipText = $"Running in the system tray. Listening for Alpha on port {port}.";
        _tray.ShowBalloonTip(4000);

        _service = new BetaService(port);
        _service.StatusChanged += OnStatusChanged;
        _service.Start();
    }

    private void OnStatusChanged(BetaStatus status, string? detail)
    {
        _ui.Post(_ =>
        {
            try
            {
                _tray.Icon = NewIcon(ColorFor(status));
                _tray.Text = Truncate($"AOE Beta - {Describe(status, detail)}");
            }
            catch { /* tray may be disposing */ }
        }, null);
    }

    /// <summary>Creates a fresh status icon and disposes the previously generated one.</summary>
    private Icon NewIcon(Color color)
    {
        var icon = TrayGraphics.MakeDotIcon(color);
        _currentIcon?.Dispose();
        _currentIcon = icon;
        return icon;
    }

    private static Color ColorFor(BetaStatus status) => status switch
    {
        BetaStatus.ClientConnected => Color.LimeGreen,
        BetaStatus.Recording => Color.Red,
        BetaStatus.Listening => Color.Gray,
        BetaStatus.Error => Color.Red,
        _ => Color.Gray,
    };

    private static string Describe(BetaStatus status, string? detail) => status switch
    {
        BetaStatus.Listening => detail ?? "listening",
        BetaStatus.ClientConnected => $"connected ({detail})",
        BetaStatus.Recording => "recording",
        BetaStatus.Error => $"error: {detail}",
        _ => status.ToString(),
    };

    // NotifyIcon.Text has a 63-char limit.
    private static string Truncate(string s) => s.Length <= 63 ? s : s[..63];

    private static int ResolvePort()
    {
        string? env = Environment.GetEnvironmentVariable("AOE_PORT");
        return int.TryParse(env, out int p) ? p : FrameProtocol.DefaultPort;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _service.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _currentIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
