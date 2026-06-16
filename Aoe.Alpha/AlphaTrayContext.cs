using Aoe.Protocol;

namespace Aoe.Alpha;

/// <summary>Tray-only context: status icon, PTT keyboard hook, and the dictation service.</summary>
public sealed class AlphaTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Control _marshal;
    private readonly AlphaConfig _config;
    private readonly AlphaService _service;
    private readonly PushToTalkHook _hook;
    private readonly AnswerPopup _popup;
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
        menu.Items.Add($"Dictate vk=0x{_config.PushToTalkVk:X2}  -  Assistant vk=0x{_config.AssistantPushToTalkVk:X2}").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;

        // A balloon toast pops up even when Windows hides the icon in the tray overflow,
        // so the user gets clear confirmation the agent is running.
        _tray.BalloonTipTitle = "AOE Alpha";
        _tray.BalloonTipText = $"Running in the tray. Dictate (vk=0x{_config.PushToTalkVk:X2}), Assistant (vk=0x{_config.AssistantPushToTalkVk:X2}); connecting to Beta...";
        _tray.ShowBalloonTip(4000);

        _popup = new AnswerPopup();

        _service = new AlphaService(_config, RunOnUi);
        _service.StatusChanged += OnStatusChanged;
        _service.Notify += OnNotify;
        _service.Start();

        _hook = new PushToTalkHook(new[] { _config.PushToTalkVk, _config.AssistantPushToTalkVk });
        _hook.Pressed += vk => _service.BeginCapture(vk, ModeForVk(vk));
        _hook.Released += vk => _service.EndCapture(vk);
        try
        {
            _hook.Install();
        }
        catch (Exception ex)
        {
            OnStatusChanged(AlphaStatus.Error, $"hook: {ex.Message}");
        }
    }

    private Mode ModeForVk(int vk) =>
        vk == _config.AssistantPushToTalkVk ? Mode.Assistant : Mode.Dictation;

    private void RunOnUi(Action action)
    {
        if (_marshal.IsHandleCreated)
        {
            try { _marshal.BeginInvoke(action); } catch { }
        }
    }

    private void OnNotify(string title, string message, string? imageUrl)
    {
        RunOnUi(() =>
        {
            try
            {
                // Prefer the rich WebView2 popup; fall back to the balloon if it isn't ready yet.
                if (_popup.IsReady)
                {
                    _popup.ShowAnswer(title, message, imageUrl);
                    return;
                }

                _tray.BalloonTipTitle = Truncate(string.IsNullOrWhiteSpace(title) ? "AOE Assistant" : title);
                _tray.BalloonTipText = string.IsNullOrEmpty(message) ? "(no answer)" : ClampForToast(StripMarkup(message));
                _tray.ShowBalloonTip(8000);
            }
            catch { /* tray disposing */ }
        });
    }

    private static string StripMarkup(string s) =>
        s.Replace("**", "").Replace("__", "").Replace("`", "").Replace("#", "");

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
        AlphaStatus.Thinking => Color.DeepSkyBlue,
        AlphaStatus.Error => Color.Gold,
        AlphaStatus.Disconnected => Color.Gray,
        _ => Color.Gray,
    };

    private static string Describe(AlphaStatus status, string? detail) => status switch
    {
        AlphaStatus.Disconnected => detail is null ? "disconnected" : $"disconnected ({detail})",
        AlphaStatus.Connected => detail is null ? "ready" : $"ready - {detail}",
        AlphaStatus.Recording => "recording",
        AlphaStatus.Thinking => "thinking",
        AlphaStatus.Error => $"error: {detail}",
        _ => status.ToString(),
    };

    private static string Truncate(string s) => s.Length <= 63 ? s : s[..63];

    /// <summary>Balloon body is capped at 255 chars; trim at a word boundary and add an ellipsis.</summary>
    private static string ClampForToast(string s)
    {
        if (s.Length <= 255)
            return s;
        string head = s[..254];
        int lastSpace = head.LastIndexOf(' ');
        if (lastSpace > 200)
            head = head[..lastSpace];
        return head.TrimEnd() + "\u2026";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hook.Dispose();
            _service.Dispose();
            _popup.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _currentIcon?.Dispose();
            _marshal.Dispose();
        }
        base.Dispose(disposing);
    }
}
