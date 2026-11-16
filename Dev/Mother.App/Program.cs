// @module: Mother.App.Program
// @purpose: Mother desktop & daemon entry point enforcing single-instance IPC, diagnose mode, and WinForms tray hosting.
// @depends-on: System, System.Text.Json, System.Threading, System.Windows.Forms, Microsoft.AspNetCore.Builder, Mother.Core, Mother.Server
// @exports: Program

using System;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Mother.Core;
using Mother.Server;

namespace Mother.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var runtime = new MotherRuntime(args, skipIpcMutex: false);

        if (runtime.Paths.Options.ShowVersion)
        {
            Console.WriteLine(runtime.Version);
            return 0;
        }

        if (runtime.Paths.Options.ShowHelp)
        {
            Console.WriteLine($"Mother v{runtime.Version} — Single Shared Suite Daemon");
            Console.WriteLine("Usage: Mother.exe [--window | --tray-only | --browser | --diagnose] [--suite-tab <appId>] [--data-dir <dir>] [--config <file>] [--log-dir <dir>] [--port <port>] [--dry-run]");
            return 0;
        }

        if (runtime.Paths.Options.Diagnose)
        {
            var report = runtime.RunDoctor();
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return string.Equals(report.Overall, "FAIL", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        if (!runtime.Ipc.IsPrimaryInstance)
        {
            string? targetTab = runtime.Paths.Options.SuiteTabAppId ?? runtime.Paths.Options.WindowAppId;
            string cmd = string.IsNullOrWhiteSpace(targetTab)
                ? "show"
                : $"tab:{targetTab}";
            runtime.Ipc.TrySendCommandToPrimary(cmd);
            return 0;
        }

        runtime.StartBackgroundSupervision();
        var webApp = MotherWebHost.BuildWebApplication(runtime, args);

        webApp.StartAsync().GetAwaiter().GetResult();
        string baseUrl = $"http://127.0.0.1:{runtime.ResolveListenPort()}/";

        ApplicationConfiguration.Initialize();
        using var cts = new CancellationTokenSource();
        using var trayContext = new TrayHostContext(runtime, baseUrl, () =>
        {
            try
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                webApp.StopAsync(stopCts.Token).GetAwaiter().GetResult();
            }
            catch { }
            cts.Cancel();
        });
        Application.Run(trayContext);
        try { webApp.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        return 0;
    }
}
