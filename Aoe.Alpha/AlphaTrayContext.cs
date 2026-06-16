namespace Aoe.Alpha;

/// <summary>Tray-only context: status icon, PTT keyboard hook, and the dictation service.</summary>
public sealed class AlphaTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Control _marshal;
    private readonly AlphaConfig _config;
    private readonly AlphaService _service;
    private readonly PushToTalkHook _hook;
    private Icon? _currentIcon;

    public AlphaTrayContext()
    {
        _config = AlphaConfig.Load();

        // A parentless control with a forced handle gives us a reliable UI-thread marshaller.
        _marshal = new Control();
        _ = _marshal.Handle;

        _tray = new NotifyIcon
        {
            Icon = NewIcon(Color.Gray),
            Visible = true,
            Text = "AOE Alpha - starting",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add($"AOE Alpha (PTT vk=0x{_config.PushToTalkVk:X2})").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;

        // A balloon toast pops up even when Windows hides the icon in the tray overflow,
        // so the user gets clear confirmation the agent is running.
        _tray.BalloonTipTitle = "AOE Alpha";
        _tray.BalloonTipText = $"Running in the system tray. Listening for PTT (vk=0x{_config.PushToTalkVk:X2}); connecting to Beta...";
        _tray.ShowBalloonTip(4000);

        _service = new AlphaService(_config, RunOnUi);
        _service.StatusChanged += OnStatusChanged;
        _service.Start();

        _hook = new PushToTalkHook(_config.PushToTalkVk);
        _hook.Pressed += _service.BeginCapture;
        _hook.Released += _service.EndCapture;
        try
        {
            _hook.Install();
        }
        catch (Exception ex)
        {
            OnStatusChanged(AlphaStatus.Error, $"hook: {ex.Message}");
        }
    }

    private void RunOnUi(Action action)
    {
        if (_marshal.IsHandleCreated)
        {
            try { _marshal.BeginInvoke(action); } catch { }
        }
    }

    private void OnStatusChanged(AlphaStatus status, string? detail)
    {
        RunOnUi(() =>
        {
            try
            {
                _tray.Icon = NewIcon(ColorFor(status));
                _tray.Text = Truncate($"AOE Alpha - {Describe(status, detail)}");
            }
            catch { /* tray disposing */ }
        });
    }

    /// <summary>Creates a fresh status icon and disposes the previously generated one.</summary>
    private Icon NewIcon(Color color)
    {
        var icon = TrayGraphics.MakeDotIcon(color);
        _currentIcon?.Dispose();
        _currentIcon = icon;
        return icon;
    }

    private static Color ColorFor(AlphaStatus status) => status switch
    {
        AlphaStatus.Connected => Color.LimeGreen,
        AlphaStatus.Recording => Color.Red,
        AlphaStatus.Transcribing => Color.Orange,
        AlphaStatus.Disconnected => Color.Gray,
        AlphaStatus.Error => Color.Red,
        _ => Color.Gray,
    };

    private static string Describe(AlphaStatus status, string? detail) => status switch
    {
        AlphaStatus.Disconnected => detail is null ? "disconnected" : $"disconnected ({detail})",
        AlphaStatus.Connected => detail is null ? "ready" : $"ready - {detail}",
        AlphaStatus.Recording => "recording",
        AlphaStatus.Transcribing => "transcribing",
        AlphaStatus.Error => $"error: {detail}",
        _ => status.ToString(),
    };

    private static string Truncate(string s) => s.Length <= 63 ? s : s[..63];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hook.Dispose();
            _service.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _currentIcon?.Dispose();
            _marshal.Dispose();
        }
        base.Dispose(disposing);
    }
}
