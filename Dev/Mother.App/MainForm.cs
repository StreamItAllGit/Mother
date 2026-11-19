// @module: Mother.App.MainForm
// @purpose: Dark-theme WinForms WebView2 single-window host isolating WebView2 user-data, handling missing WebView2 runtime gracefully, and supporting minimize-to-tray.
// @depends-on: System, System.Diagnostics, System.Drawing, System.IO, System.Threading.Tasks, System.Windows.Forms, Foundation.UI.Desktop, Microsoft.Web.WebView2.Core, Microsoft.Web.WebView2.WinForms, Mother.Core, Mother.Core.Configuration
// @exports: MainForm

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.UI.Desktop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Mother.Core;
using Mother.Core.Configuration;

namespace Mother.App;

public sealed class MainForm : Form
{
    private const string WebView2DownloadUrl = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";

    private static readonly Color BgCanvas = Color.FromArgb(11, 14, 20);
    private static readonly Color BgCard = Color.FromArgb(18, 23, 34);
    private static readonly Color BorderSubtle = Color.FromArgb(31, 41, 55);
    private static readonly Color AccentSky = Color.FromArgb(56, 189, 248);
    private static readonly Color TextPrimary = Color.FromArgb(226, 232, 240);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);

    private readonly MotherRuntime _runtime;
    private readonly string _baseUrl;
    private readonly WebView2 _webView;
    private string _currentTargetUrl;
    private Task? _initTask;
    private Panel? _fallbackPanel;
    private Label? _fallbackTitleLabel;
    private Label? _fallbackBodyLabel;
    private TextBox? _fallbackUrlBox;
    private bool _allowClose;
    private static int _loaderDllFolderConfigured;

    public bool IsFallbackActive => _fallbackPanel is not null && _fallbackPanel.Visible;

    public string? AllowedSnapshotRoot =>
        _runtime.Paths.IsSandboxActive
            ? null
            : (_runtime.Paths.IsIsolatedOverride ? _runtime.Paths.ContainmentRoot : _runtime.Paths.DataDirectory);

#if DEBUG || MOTHER_TEST_AUTOMATION
    public Automation.WebView2RenderSurfaceSnapshotProvider SnapshotProvider { get; }
#endif

    public MainForm(MotherRuntime runtime, string baseUrl, Icon? appIcon)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _currentTargetUrl = BuildTargetUrl(runtime.Paths.Options.SuiteTabAppId ?? runtime.Paths.Options.WindowAppId);

        string version = string.IsNullOrWhiteSpace(runtime.Version)
            ? MotherBuildConstants.AppVersionFallback
            : runtime.Version;

        Name = "MotherMainForm";
        Text = $"Mother v{version}{(runtime.Paths.IsIsolatedOverride ? " [ISOLATED]" : string.Empty)}";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(420, 480);
        BackColor = BgCanvas;
        ForeColor = TextPrimary;
        StartPosition = FormStartPosition.CenterScreen;
        if (appIcon is not null) Icon = appIcon;

        _webView = new WebView2
        {
            Name = "MotherWebView",
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = BgCanvas
        };
        Controls.Add(_webView);

#if DEBUG || MOTHER_TEST_AUTOMATION
        SnapshotProvider = new Automation.WebView2RenderSurfaceSnapshotProvider(this, _webView, _runtime.Diagnostics);
#endif

        Load += async (_, _) => await EnsureInitializedAsync();
        FormClosing += OnFormClosing;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DisposableWebViewHostForm.EnableImmersiveDarkTitleBar(Handle);
    }

    public async Task NavigateToTabAsync(string? suiteTab)
    {
        _currentTargetUrl = BuildTargetUrl(suiteTab);
        if (_fallbackUrlBox is not null && !_fallbackUrlBox.IsDisposed)
        {
            _fallbackUrlBox.Text = _currentTargetUrl;
        }

        await EnsureInitializedAsync();
        if (IsDisposed || _webView.IsDisposed || IsFallbackActive)
        {
            return;
        }

        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate(_currentTargetUrl);
        }
    }

    public void ForceExit()
    {
        _allowClose = true;
        Close();
    }

    private string BuildTargetUrl(string? suiteTab)
    {
        if (string.IsNullOrWhiteSpace(suiteTab))
        {
            return _baseUrl;
        }
        if (suiteTab.StartsWith("route:", StringComparison.OrdinalIgnoreCase))
        {
            string routeName = suiteTab["route:".Length..].Trim();
            return $"{_baseUrl.TrimEnd('/')}/?route={Uri.EscapeDataString(routeName)}";
        }
        return $"{_baseUrl.TrimEnd('/')}/?tab={Uri.EscapeDataString(suiteTab)}";
    }

    private Task EnsureInitializedAsync()
    {
        if (IsDisposed || _webView.IsDisposed || IsFallbackActive)
        {
            return Task.CompletedTask;
        }
        if (_webView.CoreWebView2 is not null)
        {
            return Task.CompletedTask;
        }
        return _initTask ??= InitializeWebView2CoreAsync();
    }

    private async Task InitializeWebView2CoreAsync()
    {
        try
        {
            string archFolder = Environment.Is64BitProcess ? "win-x64" : "win-x86";
            string nativeLoaderDir = Path.Combine(AppContext.BaseDirectory, "runtimes", archFolder, "native");
            if (File.Exists(Path.Combine(nativeLoaderDir, "WebView2Loader.dll")) &&
                Interlocked.Exchange(ref _loaderDllFolderConfigured, 1) == 0)
            {
                CoreWebView2Environment.SetLoaderDllFolderPath(nativeLoaderDir);
            }
            string wvData = Path.Combine(_runtime.Paths.DataDirectory, "webview2");
            Directory.CreateDirectory(wvData);
            var env = await CoreWebView2Environment.CreateAsync(null, wvData);
            if (IsDisposed || _webView.IsDisposed) return;

            await _webView.EnsureCoreWebView2Async(env);
            if (IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 is null) return;

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = _runtime.Paths.IsIsolatedOverride;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Navigate(_currentTargetUrl);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            _runtime.Diagnostics.Log("WARN", "mother", "WebView2", $"Microsoft Edge WebView2 Evergreen Runtime missing: {ex.Message}");
            ShowWebView2Fallback(isRuntimeMissing: true, ex.Message);
        }
        catch (Exception ex) when (!IsDisposed)
        {
            _runtime.Diagnostics.Log("WARN", "mother", "WebView2", $"WebView2 initialization failed: {ex.Message}");
            ShowWebView2Fallback(isRuntimeMissing: false, ex.Message);
        }
    }

    private void ShowWebView2Fallback(bool isRuntimeMissing, string errorDetail)
    {
        if (IsDisposed) return;

        _webView.Visible = false;
        if (_fallbackPanel is null)
        {
            BuildFallbackPanel();
        }

        if (_fallbackTitleLabel is not null)
        {
            _fallbackTitleLabel.Text = isRuntimeMissing
                ? "Microsoft Edge WebView2 Runtime Required"
                : "Embedded Desktop View Unavailable";
        }

        if (_fallbackBodyLabel is not null)
        {
            _fallbackBodyLabel.Text = isRuntimeMissing
                ? "Mother could not detect the Microsoft Edge WebView2 Evergreen Runtime on this machine.\n\n" +
                  "Install the WebView2 Evergreen Runtime from Microsoft to render the suite inside this window, " +
                  "or open the local Mother dashboard directly in your default web browser while Mother continues running."
                : $"The embedded WebView2 surface could not initialize ({errorDetail}).\n\n" +
                  "You can retry WebView2 initialization or open the local Mother dashboard in your default web browser.";
        }

        if (_fallbackUrlBox is not null)
        {
            _fallbackUrlBox.Text = _currentTargetUrl;
        }

        _fallbackPanel!.Visible = true;
        _fallbackPanel.BringToFront();
    }

    private void BuildFallbackPanel()
    {
        var host = new TableLayoutPanel
        {
            Name = "WebViewFallbackPanel",
            Dock = DockStyle.Fill,
            BackColor = BgCanvas,
            ColumnCount = 3,
            RowCount = 3
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        var card = new Panel
        {
            Name = "WebViewFallbackCard",
            BackColor = BgCard,
            MinimumSize = new Size(540, 320),
            MaximumSize = new Size(640, 460),
            AutoSize = true,
            Padding = new Padding(28, 24, 28, 24)
        };
        card.Paint += (_, e) =>
        {
            using var borderPen = new Pen(BorderSubtle, 1);
            e.Graphics.DrawRectangle(borderPen, 0, 0, card.Width - 1, card.Height - 1);
            using var accentBrush = new SolidBrush(AccentSky);
            e.Graphics.FillRectangle(accentBrush, 0, 0, card.Width, 4);
        };

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = BgCard
        };

        var badge = new Label
        {
            Text = "MOTHER DESKTOP FALLBACK",
            ForeColor = AccentSky,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        _fallbackTitleLabel = new Label
        {
            Name = "WebViewFallbackTitle",
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 10)
        };

        _fallbackBodyLabel = new Label
        {
            Name = "WebViewFallbackGuidance",
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9.75f, FontStyle.Regular),
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 14)
        };

        var urlLabel = new Label
        {
            Text = "Local Suite Dashboard URL:",
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        _fallbackUrlBox = new TextBox
        {
            Name = "WebViewFallbackUrl",
            Text = _currentTargetUrl,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = BgCanvas,
            ForeColor = AccentSky,
            Font = new Font("Consolas", 9.5f, FontStyle.Regular),
            Width = 540,
            Margin = new Padding(0, 0, 0, 16)
        };

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = BgCard,
            Margin = new Padding(0)
        };

        var btnOpenBrowser = CreateActionButton("btnOpenDashboardBrowser", "Open in Browser", isPrimary: true);
        btnOpenBrowser.Click += (_, _) => LaunchExternalUrl(_currentTargetUrl, "DashboardBrowserLaunch");

        var btnGetRuntime = CreateActionButton("btnInstallWebView2", "Get WebView2 Runtime", isPrimary: false);
        btnGetRuntime.Click += (_, _) => LaunchExternalUrl(WebView2DownloadUrl, "WebView2DownloadLaunch");

        var btnRetry = CreateActionButton("btnRetryWebView2", "Retry", isPrimary: false);
        btnRetry.Click += async (_, _) => await RetryWebView2Async();

        actions.Controls.Add(btnOpenBrowser);
        actions.Controls.Add(btnGetRuntime);
        actions.Controls.Add(btnRetry);

        stack.Controls.Add(badge);
        stack.Controls.Add(_fallbackTitleLabel);
        stack.Controls.Add(_fallbackBodyLabel);
        stack.Controls.Add(urlLabel);
        stack.Controls.Add(_fallbackUrlBox);
        stack.Controls.Add(actions);

        card.Controls.Add(stack);
        host.Controls.Add(card, 1, 1);
        Controls.Add(host);
        _fallbackPanel = host;
    }

    private static Button CreateActionButton(string name, string text, bool isPrimary)
    {
        var btn = new Button
        {
            Name = name,
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(124, 34),
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9f, isPrimary ? FontStyle.Bold : FontStyle.Regular),
            BackColor = isPrimary ? AccentSky : Color.FromArgb(30, 41, 59),
            ForeColor = isPrimary ? BgCanvas : TextPrimary
        };
        btn.FlatAppearance.BorderColor = isPrimary ? AccentSky : BorderSubtle;
        btn.FlatAppearance.BorderSize = 1;
        return btn;
    }

    private async Task RetryWebView2Async()
    {
        if (IsDisposed || _webView.IsDisposed) return;
        if (_fallbackPanel is not null)
        {
            _fallbackPanel.Visible = false;
        }
        _webView.Visible = true;
        _initTask = null;
        await EnsureInitializedAsync();
    }

    private void LaunchExternalUrl(string url, string slice)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _runtime.Diagnostics.Log("WARN", "mother", slice, $"Unable to open URL '{url}': {ex.Message}");
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
#if DEBUG || MOTHER_TEST_AUTOMATION
        SnapshotProvider.Dispose();
#endif
        _webView.Dispose();
    }
}
