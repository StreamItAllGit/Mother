// @module: Mother.App.TrayHostContext
// @purpose: System tray ApplicationContext hosting Mother WebView2 window, TaskbarCreated restore sentinel, and IPC focus commands.
// @depends-on: System, System.Diagnostics, System.Drawing, System.IO, System.Runtime.InteropServices, System.Windows.Forms, Mother.Core, Mother.Core.Configuration
// @exports: TrayHostContext

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mother.Core;
using Mother.Core.Configuration;

namespace Mother.App;

public sealed class TrayHostContext : ApplicationContext
{
    private sealed class TaskbarSentinelWindow : NativeWindow, IDisposable
    {
        private readonly uint _taskbarCreatedMsg;
        private readonly Action _onTaskbarCreated;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        public TaskbarSentinelWindow(Action onTaskbarCreated)
        {
            _onTaskbarCreated = onTaskbarCreated;
            _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (_taskbarCreatedMsg != 0 && (uint)m.Msg == _taskbarCreatedMsg)
            {
                _onTaskbarCreated();
            }
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }

    private readonly MotherRuntime _runtime;
    private readonly string _baseUrl;
    private readonly Action _onShutdown;
    private readonly NotifyIcon _trayIcon;
    private readonly TaskbarSentinelWindow _sentinel;
    private readonly Icon? _appIcon;
    private readonly MainForm _mainForm;

    public new MainForm MainForm => _mainForm;

    public TrayHostContext(MotherRuntime runtime, string baseUrl, Action onShutdown)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _onShutdown = onShutdown ?? throw new ArgumentNullException(nameof(onShutdown));
        _appIcon = LoadAppIcon(runtime.Paths.BaseDirectory, runtime);
        _mainForm = new MainForm(runtime, baseUrl, _appIcon);
        // Ensure the hidden Win32 handle exists so tray-only IPC BeginInvoke and automation hooks never fail.
        _ = _mainForm.Handle;

        string version = string.IsNullOrWhiteSpace(runtime.Version)
            ? MotherBuildConstants.AppVersionFallback
            : runtime.Version;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Suite Dashboard", null, (_, _) => ShowMainWindow(null));
        menu.Items.Add("Open in Default Browser", null, (_, _) => OpenInBrowser(_baseUrl));
        menu.Items.Add("Run Fleet Doctor", null, (_, _) =>
        {
            var report = _runtime.RunDoctor();
            _trayIcon!.ShowBalloonTip(2500, "Mother Fleet Doctor", $"Overall: {report.Overall} ({report.SuiteChecks.Count} checks)", ToolTipIcon.Info);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Mother", null, (_, _) => ExitSuite());

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Application,
            Text = $"Mother v{version}",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow(null);

        _sentinel = new TaskbarSentinelWindow(() =>
        {
            _trayIcon.Visible = false;
            _trayIcon.Visible = true;
        });

        _runtime.Ipc.CommandReceived += OnIpcCommandReceived;

        string? browserTab = _runtime.Paths.Options.BrowserAppId;
        string? windowTab = _runtime.Paths.Options.SuiteTabAppId ?? _runtime.Paths.Options.WindowAppId;
        if (!string.IsNullOrWhiteSpace(browserTab))
        {
            string url = string.Equals(browserTab, "mother", StringComparison.OrdinalIgnoreCase)
                ? _baseUrl
                : $"{_baseUrl.TrimEnd('/')}/?tab={Uri.EscapeDataString(browserTab)}";
            OpenInBrowser(url);
        }
        else if (!_runtime.Paths.Options.TrayOnly)
        {
            ShowMainWindow(windowTab);
        }
    }

    public void ShowMainWindow(string? suiteTab)
    {
        if (_mainForm.IsDisposed) return;
        if (!_mainForm.Visible) _mainForm.Show();
        if (_mainForm.WindowState == FormWindowState.Minimized) _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
        if (!string.IsNullOrWhiteSpace(suiteTab))
        {
            _ = _mainForm.NavigateToTabAsync(suiteTab);
        }
    }

    private void OnIpcCommandReceived(string cmd)
    {
        if (_mainForm.IsDisposed || !_mainForm.IsHandleCreated) return;
        try
        {
            _mainForm.BeginInvoke(() =>
            {
                if (cmd.StartsWith("tab:", StringComparison.OrdinalIgnoreCase))
                {
                    ShowMainWindow(cmd.Substring(4));
                }
                else
                {
                    ShowMainWindow(null);
                }
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            _runtime.Diagnostics.Log("DEBUG", "mother", "TrayIpc", $"IPC focus command skipped during shutdown: {ex.Message}");
        }
    }

    private void ExitSuite()
    {
        _runtime.Ipc.CommandReceived -= OnIpcCommandReceived;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _sentinel.Dispose();
        _mainForm.ForceExit();
        _onShutdown();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtime.Ipc.CommandReceived -= OnIpcCommandReceived;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _sentinel.Dispose();
            _appIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Icon? LoadAppIcon(string baseDir, MotherRuntime runtime)
    {
        string[] candidates =
        {
            Path.Combine(baseDir, "Assets", "app_icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico")
        };
        foreach (string p in candidates)
        {
            if (!File.Exists(p)) continue;
            try
            {
                return new Icon(p);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                runtime.Diagnostics.Log("WARN", "mother", "TrayIcon", $"Unable to load icon '{p}': {ex.Message}");
            }
        }
        return null;
    }

    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _runtime.Diagnostics.Log("WARN", "mother", "TrayBrowser", $"Unable to launch browser for '{url}': {ex.Message}");
        }
    }
}
