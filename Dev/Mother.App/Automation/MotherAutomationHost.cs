// @module: Mother.App.Automation.MotherAutomationHost
// @purpose: Debug/TestAutomation-only loopback server host and STA-safe non-blocking WebView2 RGBA frame buffer provider.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Drawing.Imaging, System.IO, System.Runtime.InteropServices, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Automation, Microsoft.Web.WebView2.Core, Microsoft.Web.WebView2.WinForms, Mother.Core, Mother.Core.Diagnostics
// @exports: WebView2RenderSurfaceSnapshotProvider, MotherAutomationHost

#if DEBUG || MOTHER_TEST_AUTOMATION
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Automation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Mother.Core;
using Mother.Core.Diagnostics;

namespace Mother.App.Automation;

/// <summary>
/// Provides thread-safe RGBA framebuffer snapshots from WebView2 without blocking the UI STA thread
/// or holding locks across awaits.
/// </summary>
public sealed class WebView2RenderSurfaceSnapshotProvider : IRenderSurfaceSnapshotProvider, IDisposable
{
    private const int RefreshIntervalMs = 250;

    private readonly MainForm _mainForm;
    private readonly WebView2 _webView;
    private readonly MotherDiagnostics? _diagnostics;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly object _frameSync = new();

    private byte[]? _latestRgba;
    private int _latestWidth;
    private int _latestHeight;
    private int _captureInFlight;
    private int _disposed;
    private bool _pumpActive;
    private CoreWebView2? _hookedCoreWebView;

    public WebView2RenderSurfaceSnapshotProvider(
        MainForm mainForm,
        WebView2 webView,
        MotherDiagnostics? diagnostics = null)
    {
        _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _diagnostics = diagnostics;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
        _refreshTimer.Tick += (_, _) => QueueCaptureOnUi();

        _webView.CoreWebView2InitializationCompleted += OnCoreWebView2InitializationCompleted;
        if (_webView.CoreWebView2 is not null)
        {
            AttachCoreWebView2(_webView.CoreWebView2);
        }
    }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _pumpActive = true;
        if (!_refreshTimer.Enabled)
        {
            _refreshTimer.Start();
        }
        RequestRefresh();
    }

    public void Stop()
    {
        _pumpActive = false;
        if (_refreshTimer.Enabled)
        {
            _refreshTimer.Stop();
        }
    }

    public void RequestRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0 || _mainForm.IsDisposed || _webView.IsDisposed)
        {
            return;
        }

        try
        {
            if (_mainForm.IsHandleCreated)
            {
                _mainForm.BeginInvoke(QueueCaptureOnUi);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            _diagnostics?.Log("DEBUG", "mother", "AutomationSnapshot", $"Snapshot refresh skipped during shutdown: {ex.Message}");
        }
    }

    public bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes)
    {
        RequestRefresh();

        if (!_mainForm.IsFallbackActive && _webView.Visible && _webView.CoreWebView2 is not null)
        {
            lock (_frameSync)
            {
                if (_latestRgba is not null && _latestWidth > 0 && _latestHeight > 0)
                {
                    width = _latestWidth;
                    height = _latestHeight;
                    rgbaBytes = (byte[])_latestRgba.Clone();
                    return true;
                }
            }
        }

        width = 0;
        height = 0;
        rgbaBytes = Array.Empty<byte>();
        return false;
    }

    private void OnCoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (e.IsSuccess && _webView.CoreWebView2 is not null)
        {
            AttachCoreWebView2(_webView.CoreWebView2);
            if (_pumpActive)
            {
                QueueCaptureOnUi();
            }
        }
    }

    private void AttachCoreWebView2(CoreWebView2 core)
    {
        if (ReferenceEquals(_hookedCoreWebView, core)) return;
        DetachCoreWebView2();
        _hookedCoreWebView = core;
        core.DOMContentLoaded += OnDomOrNavigationReady;
        core.NavigationCompleted += OnNavigationCompleted;
    }

    private void DetachCoreWebView2()
    {
        if (_hookedCoreWebView is null) return;
        try
        {
            _hookedCoreWebView.DOMContentLoaded -= OnDomOrNavigationReady;
            _hookedCoreWebView.NavigationCompleted -= OnNavigationCompleted;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException)
        {
            _diagnostics?.Log("DEBUG", "mother", "AutomationSnapshot", $"CoreWebView2 event detach skipped: {ex.Message}");
        }
        _hookedCoreWebView = null;
    }

    private void OnDomOrNavigationReady(object? sender, CoreWebView2DOMContentLoadedEventArgs e) => QueueCaptureOnUi();

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => QueueCaptureOnUi();

    private void QueueCaptureOnUi()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _ = CaptureFrameAsync();
    }

    private async Task CaptureFrameAsync()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _mainForm.IsDisposed ||
            _mainForm.IsFallbackActive ||
            !_mainForm.Visible ||
            _webView.IsDisposed ||
            !_webView.Visible ||
            _webView.CoreWebView2 is null ||
            _webView.Width <= 0 ||
            _webView.Height <= 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _captureInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
            byte[] pngBytes;
            using (var ms = new MemoryStream())
            {
                await _webView.CoreWebView2
                    .CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms)
                    .ConfigureAwait(true);
                pngBytes = ms.ToArray();
            }

            if (pngBytes.Length == 0 || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            (int width, int height, byte[] rgba) = await Task.Run(() => DecodePngToRgba(pngBytes)).ConfigureAwait(false);
            if (width <= 0 || height <= 0 || rgba.Length == 0 || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            lock (_frameSync)
            {
                _latestWidth = width;
                _latestHeight = height;
                _latestRgba = rgba;
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException)
        {
            _diagnostics?.Log("DEBUG", "mother", "AutomationSnapshot", $"WebView2 preview capture skipped: {ex.Message}");
        }
        catch (Exception ex)
        {
            _diagnostics?.Log("WARN", "mother", "AutomationSnapshot", $"WebView2 preview capture failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _captureInFlight, 0);
        }
    }

    private static (int Width, int Height, byte[] Rgba) DecodePngToRgba(byte[] pngBytes)
    {
        using var pngStream = new MemoryStream(pngBytes, writable: false);
        using var bmp = new Bitmap(pngStream);
        int width = bmp.Width;
        int height = bmp.Height;
        if (width <= 0 || height <= 0)
        {
            return (0, 0, Array.Empty<byte>());
        }

        var rect = new Rectangle(0, 0, width, height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int absStride = Math.Abs(stride);
            byte[] rowBuf = new byte[absStride];
            byte[] rgba = new byte[checked(width * height * 4)];

            for (int y = 0; y < height; y++)
            {
                IntPtr rowPtr = stride >= 0
                    ? IntPtr.Add(data.Scan0, y * stride)
                    : IntPtr.Add(data.Scan0, (height - 1 - y) * absStride);
                Marshal.Copy(rowPtr, rowBuf, 0, absStride);

                int dstRow = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = x * 4;
                    int dstIdx = dstRow + srcIdx;
                    rgba[dstIdx] = rowBuf[srcIdx + 2];
                    rgba[dstIdx + 1] = rowBuf[srcIdx + 1];
                    rgba[dstIdx + 2] = rowBuf[srcIdx];
                    rgba[dstIdx + 3] = rowBuf[srcIdx + 3];
                }
            }

            return (width, height, rgba);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _pumpActive = false;
        try
        {
            _webView.CoreWebView2InitializationCompleted -= OnCoreWebView2InitializationCompleted;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            _diagnostics?.Log("DEBUG", "mother", "AutomationSnapshot", $"WebView2 event unsubscribe skipped: {ex.Message}");
        }

        DetachCoreWebView2();
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
    }
}

/// <summary>
/// Manages the opt-in Foundation.Automation loopback server lifecycle around MainForm.
/// </summary>
public sealed class MotherAutomationHost : IDisposable
{
    private readonly WebView2RenderSurfaceSnapshotProvider _snapshotProvider;
    private readonly MotherDiagnostics _diagnostics;
    private bool _disposed;

    public AutomationLoopbackServer Server { get; }
    public int AutomationPort => Server.AutomationPort;

    private MotherAutomationHost(
        AutomationLoopbackServer server,
        WebView2RenderSurfaceSnapshotProvider snapshotProvider,
        MotherDiagnostics diagnostics)
    {
        Server = server;
        _snapshotProvider = snapshotProvider;
        _diagnostics = diagnostics;
    }

    public static MotherAutomationHost? TryStart(string[]? args, MainForm mainForm, MotherRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(runtime);

        string[] normalizedArgs = ExpandInlineAutomationArgs(args);
        AutomationLoopbackServer? server = AutomationCliHelper.TryCreateFromArgs(
            "mother",
            normalizedArgs,
            mainForm,
            mainForm.SnapshotProvider,
            mainForm.AllowedSnapshotRoot);
        if (server is null)
        {
            return null;
        }

        server.Start();
        mainForm.SnapshotProvider.Start();
        runtime.Diagnostics.Log(
            "INFO",
            "mother",
            "Automation",
            $"Automation loopback listening on 127.0.0.1:{server.AutomationPort}.");
        return new MotherAutomationHost(server, mainForm.SnapshotProvider, runtime.Diagnostics);
    }

    private static string[] ExpandInlineAutomationArgs(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return Array.Empty<string>();
        }

        var expanded = new List<string>(args.Length + 2);
        foreach (string raw in args)
        {
            if (raw.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add("--automation-port");
                expanded.Add(raw["--automation-port=".Length..].Trim().Trim('"'));
            }
            else if (raw.StartsWith("--automation-token=", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add("--automation-token");
                expanded.Add(raw["--automation-token=".Length..].Trim().Trim('"'));
            }
            else
            {
                expanded.Add(raw);
            }
        }
        return expanded.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _snapshotProvider.Stop();
        try
        {
            Server.Dispose();
        }
        catch (Exception ex)
        {
            _diagnostics.Log("WARN", "mother", "Automation", $"Automation server dispose warning: {ex.Message}");
        }
    }
}
#endif
