// @module: Mother.App.Program
// @purpose: Mother desktop & daemon entry point enforcing single-instance IPC, sandbox containment exit codes, optional automation loopback, and WinForms tray hosting.
// @depends-on: System, System.Text.Json, System.Threading, System.Windows.Forms, Foundation.Core, Microsoft.AspNetCore.Builder, Mother.Core, Mother.Core.Configuration, Mother.Server
// @exports: Program

using System;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Foundation.Core;
using Microsoft.AspNetCore.Builder;
using Mother.Core;
using Mother.Core.Configuration;
using Mother.Server;

namespace Mother.App;

public static class Program
{
    public static bool HasAutomationArgs(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = (args[i] ?? string.Empty).Trim();
            if (arg.Equals("--automation-port", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--automation-token", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--automation-token=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    [STAThread]
    public static int Main(string[] args)
    {
#if !(DEBUG || MOTHER_TEST_AUTOMATION)
        if (HasAutomationArgs(args))
        {
            Console.Error.WriteLine(
                "Fail-closed: --automation-port and --automation-token are disabled in Release builds. " +
                "Build with Debug or -p:MotherTestAutomation=true (ExitCode=2).");
            return 2;
        }
#endif

        try
        {
            using var runtime = new MotherRuntime(args, skipIpcMutex: false);
            string version = string.IsNullOrWhiteSpace(runtime.Version)
                ? MotherBuildConstants.AppVersionFallback
                : runtime.Version;

            if (runtime.Paths.Options.ShowVersion)
            {
                Console.WriteLine(version);
                return 0;
            }

            if (runtime.Paths.Options.ShowHelp)
            {
                Console.WriteLine($"Mother v{version} — Single Shared Suite Daemon");
                Console.WriteLine("Usage: Mother.exe [--window | --tray-only | --browser | --diagnose] [--suite-tab <appId>] [--programs-dir <dir>] [--data-dir <dir>] [--config <file>] [--log-dir <dir>] [--port <port>] [--dry-run]");
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
                catch (Exception ex)
                {
                    runtime.Diagnostics.Log("WARN", "mother", "Shutdown", $"Web host stop skipped: {ex.Message}");
                }
                cts.Cancel();
            });

#if DEBUG || MOTHER_TEST_AUTOMATION
            using var automationHost = Automation.MotherAutomationHost.TryStart(args, trayContext.MainForm, runtime);
#endif

            Application.Run(trayContext);

            try
            {
                webApp.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                runtime.Diagnostics.Log("WARN", "mother", "Shutdown", $"Web host dispose error: {ex.Message}");
            }
            return 0;
        }
        catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
        {
            Console.Error.WriteLine($"Sandbox containment violation (ExitCode={SandboxEnvironmentSeam.ContainmentExitCode}): {ex.Message}");
            return SandboxEnvironmentSeam.ContainmentExitCode;
        }
    }
}
