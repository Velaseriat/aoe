using System.Net;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Aoe.Alpha;

/// <summary>
/// A borderless, non-activating, top-most popup that renders the assistant's markdown answer with a
/// WebView2. Reused across answers (WebView2 is initialized once). Auto-dismisses, pauses on hover,
/// and closes on click. Sizes its height to the rendered content.
/// </summary>
public sealed class AnswerPopup : Form
{
    private const int FixedWidth = 460;
    private const int MaxHeight = 600;
    private const int MinHeight = 70;
    private const int EdgeMargin = 16;
    private const int AutoDismissMs = 14000;

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private readonly WebView2 _web;
    private readonly System.Windows.Forms.Timer _dismiss;
    private bool _ready;
    private bool _hovered;
    private (string Question, string Markdown)? _pending;

    public AnswerPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(31, 31, 35);
        Width = FixedWidth;
        Height = MinHeight;
        Visible = false;

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(_web);

        _dismiss = new System.Windows.Forms.Timer { Interval = AutoDismissMs };
        _dismiss.Tick += (_, _) => { if (!_hovered) Hide(); };

        _ = InitAsync();
    }

    /// <summary>Don't steal focus from the app the user is typing into.</summary>
    protected override bool ShowWithoutActivation => true;

    private async Task InitAsync()
    {
        try
        {
            string userData = Path.Combine(Path.GetTempPath(), "AoeAlpha.WebView2");
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);

            CoreWebView2Settings s = _web.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;

            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _web.NavigationCompleted += OnNavigationCompleted;

            _ready = true;
            if (_pending is { } p)
            {
                _pending = null;
                Render(p.Question, p.Markdown);
            }
        }
        catch (Exception ex)
        {
            Log.Error("WebView2 init failed", ex);
        }
    }

    /// <summary>True once WebView2 is initialized and the popup can render.</summary>
    public bool IsReady => _ready;

    /// <summary>Render and show an answer. Safe to call before init completes (it queues).</summary>
    public void ShowAnswer(string question, string markdown)
    {
        if (!_ready)
        {
            _pending = (question, markdown);
            return;
        }
        Render(question, markdown);
    }

    private void Render(string question, string markdown)
    {
        string body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        string q = WebUtility.HtmlEncode(question ?? string.Empty);
        string html = Template.Replace("{QUESTION}", q).Replace("{BODY}", body);
        _web.CoreWebView2.NavigateToString(html);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _ = ResizeToContentAsync();
    }

    private async Task ResizeToContentAsync()
    {
        try
        {
            string raw = await _web.CoreWebView2.ExecuteScriptAsync(
                "document.documentElement.scrollHeight");
            if (!int.TryParse(raw.Trim('"'), out int cssHeight))
                cssHeight = 200;

            double scale = DeviceDpi / 96.0;
            int h = (int)Math.Round(cssHeight * scale);
            h = Math.Clamp(h, MinHeight, MaxHeight);

            Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Height = h;
            Location = new Point(wa.Right - Width - EdgeMargin, wa.Bottom - Height - EdgeMargin);

            Show();
            TopMost = true;
            _dismiss.Stop();
            _dismiss.Start();
        }
        catch (Exception ex)
        {
            Log.Warn($"popup resize failed: {ex.Message}");
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string msg = e.TryGetWebMessageAsString();
        switch (msg)
        {
            case "close":
                Hide();
                break;
            case "hover":
                _hovered = true;
                break;
            case "unhover":
                _hovered = false;
                _dismiss.Stop();
                _dismiss.Start();
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dismiss.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }

    private const string Template = """
        <!doctype html><html><head><meta charset="utf-8"><style>
        :root{color-scheme:dark}
        html,body{margin:0;padding:0}
        body{background:#1f1f23;color:#e6e6e6;font-family:'Segoe UI',sans-serif;font-size:14px;line-height:1.45;padding:12px 16px}
        .q{font-size:12px;color:#8a8a8a;margin-bottom:8px;border-bottom:1px solid #34343a;padding-bottom:6px}
        h1,h2,h3,h4{margin:.3em 0 .35em;font-size:1.05em;color:#9cdcfe}
        p{margin:.4em 0}
        ul,ol{margin:.3em 0 .3em 1.25em;padding:0}
        li{margin:.15em 0}
        code{background:#2a2a30;padding:1px 5px;border-radius:3px;font-family:Consolas,monospace;font-size:.92em}
        pre{background:#2a2a30;padding:9px 11px;border-radius:6px;overflow:auto}
        pre code{background:none;padding:0}
        a{color:#4ea1ff}
        ::-webkit-scrollbar{width:9px}::-webkit-scrollbar-thumb{background:#3a3a42;border-radius:5px}
        </style></head><body>
        <div class="q">{QUESTION}</div>
        <div class="a">{BODY}</div>
        <script>
        const post=m=>window.chrome.webview.postMessage(m);
        document.body.addEventListener('click',()=>post('close'));
        document.body.addEventListener('mouseenter',()=>post('hover'));
        document.body.addEventListener('mouseleave',()=>post('unhover'));
        </script></body></html>
        """;
}
