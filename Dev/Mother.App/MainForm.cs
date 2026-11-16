// @module: Mother.App.MainForm
// @purpose: Dark-theme WinForms WebView2 single-window host isolating WebView2 user-data under DataDirectory and supporting minimize-to-tray.
// @depends-on: System, System.Drawing, System.IO, System.Threading.Tasks, System.Windows.Forms, Microsoft.Web.WebView2.Core, Microsoft.Web.WebView2.WinForms, Mother.Core
// @exports: MainForm

using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Mother.Core;

namespace Mother.App;

public sealed class MainForm : Form
{
    private readonly MotherRuntime _runtime;
    private readonly string _baseUrl;
    private readonly WebView2 _webView;
    private bool _allowClose;
    private bool _initialized;

    public MainForm(MotherRuntime runtime, string baseUrl, Icon? appIcon)
    {
        _runtime = runtime;
        _baseUrl = baseUrl;

        Text = $"Mother v{runtime.Version}{(runtime.Paths.IsIsolatedOverride ? " [ISOLATED]" : string.Empty)}";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(360, 480);
        BackColor = Color.FromArgb(11, 15, 23);
        ForeColor = Color.FromArgb(248, 250, 252);
        StartPosition = FormStartPosition.CenterScreen;
        if (appIcon is not null) Icon = appIcon;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(11, 15, 23)
        };
        Controls.Add(_webView);

        Load += async (_, _) => await EnsureInitializedAsync();
        FormClosing += OnFormClosing;
    }

    public async Task NavigateToTabAsync(string? suiteTab)
    {
        await EnsureInitializedAsync();
        string target = string.IsNullOrWhiteSpace(suiteTab)
            ? _baseUrl
            : $"{_baseUrl.TrimEnd('/')}/?tab={Uri.EscapeDataString(suiteTab)}";
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate(target);
        }
        else
        {
            _webView.Source = new Uri(target);
        }
    }

    public void ForceExit()
    {
        _allowClose = true;
        Close();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized || IsDisposed) return;
        _initialized = true;
        try
        {
            string wvData = Path.Combine(_runtime.Paths.DataDirectory, "webview2");
            Directory.CreateDirectory(wvData);
            var env = await CoreWebView2Environment.CreateAsync(null, wvData);
            await _webView.EnsureCoreWebView2Async(env);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = _runtime.Paths.IsIsolatedOverride;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            string? tabId = _runtime.Paths.Options.SuiteTabAppId ?? _runtime.Paths.Options.WindowAppId;
            string initialUrl = string.IsNullOrWhiteSpace(tabId)
                ? _baseUrl
                : $"{_baseUrl.TrimEnd('/')}/?tab={Uri.EscapeDataString(tabId)}";
            _webView.CoreWebView2.Navigate(initialUrl);
        }
        catch (Exception ex)
        {
            _runtime.Diagnostics.Log("WARN", "mother", "WebView2", $"WebView2 init fallback: {ex.Message}");
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
        _webView.Dispose();
    }
}
