// @module: Mother.Tests.V020FeaturesTests
// @purpose: Verifies Mother v0.2.0 sibling containment, schedule/command manifest parsing, WorkerArgs builder, scheduler deferral, idle hibernation, subpath proxy, terminal commands, and InstallerEngineCore options.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Net, System.Net.Http, System.Net.Sockets, System.Text, System.Text.Json, System.Threading.Tasks, Microsoft.AspNetCore.Builder, Foundation.Core, Mother.Core, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Features.Discovery, Mother.Core.Features.Supervision, Mother.Installer, Mother.Server, Xunit
// @exports: V020FeaturesTests

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Foundation.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Features.Discovery;
using Mother.Core.Features.Supervision;
using Mother.Installer;
using Mother.Server;
using Xunit;

namespace Mother.Tests;

public sealed class V020FeaturesTests
{
    [Fact]
    public void PathResolver_SiblingSandboxRootAllowsSiblingAndRejectsHostEscape()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"mother-sib-{Guid.NewGuid():N}");
        string sandboxRoot = Path.Combine(tempRoot, "Sandbox");
        string dataDir = Path.Combine(sandboxRoot, "data");
        string appDir = Path.Combine(sandboxRoot, "app");
        string siblingGreenroom = Path.Combine(sandboxRoot, "sister-apps", "greenroom");
        string hostEscape = Path.Combine(tempRoot, "outside-host");

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(siblingGreenroom);
        Directory.CreateDirectory(hostEscape);

        try
        {
            var resolver = new PathResolver(new[] { "--test-mode", "--data-dir", dataDir }, appDir);
            Assert.Equal(Path.GetFullPath(siblingGreenroom), resolver.AssertContainedIfSandbox(siblingGreenroom));
            Assert.Throws<InvalidOperationException>(() => resolver.AssertContainedIfSandbox(hostEscape));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Temp sibling cleanup note: {ex.Message}"); }
        }
    }

    [Fact]
    public void ManifestParser_ParsesSchedulesCommandsAndResidencyAliases()
    {
        const string json = """
        {
          "schemaVersion": 2,
          "appId": "greenroom",
          "name": "Greenroom",
          "version": "0.2.0",
          "residencyMode": "on-demand",
          "commands": {
            "startDaemon": "python -m greenroom companion --quiet",
            "statusCheck": "python -m greenroom status"
          },
          "schedules": [
            {
              "scheduleId": "plex-sync",
              "name": "Nightly Plex Sync",
              "cadence": "daily",
              "timeUtc": "04:15",
              "runIfMissed": true,
              "targetDrives": ["D:\\"],
              "triggerEndpoint": "/api/v1/plex/sync"
            }
          ]
        }
        """;

        SisterAppEntry? entry = ManifestParser.Parse("greenroom.manifest.json", json);
        Assert.NotNull(entry);
        Assert.Equal("cold", entry!.ResidencyMode);
        Assert.Equal("python -m greenroom companion --quiet", entry.Commands?.StartDaemon);
        Assert.Single(entry.Schedules);
        Assert.Equal("plex-sync", entry.Schedules[0].ScheduleId);
        Assert.Equal("04:15", entry.Schedules[0].TimeUtc);
        Assert.Equal("/api/v1/plex/sync", entry.Schedules[0].TriggerEndpoint);
    }

    [Fact]
    public void WorkerLaunchCommandBuilder_UsesWorkerArgsAndValidatesSandboxWorkingDir()
    {
        using var scope = new TempWorkspaceScope();
        var resolver = new PathResolver(new[] { "--test-mode", "--data-dir", scope.DataDir }, scope.BaseDir);

        string installDir = Path.Combine(scope.ProgramsDir, "greenroom");
        Directory.CreateDirectory(installDir);
        string workerBat = Path.Combine(installDir, "worker.bat");
        File.WriteAllText(workerBat, "@echo off\r\n");

        var manifest = new SisterAppEntry
        {
            AppId = "greenroom",
            Name = "Greenroom",
            InstallDir = installDir,
            Executable = "worker.bat",
            WorkerArgs = new[] { "--worker-tag", "alpha" },
            InternalPort = 8420,
            SandboxPort = 15420
        };

        var psi = WorkerLaunchCommandBuilder.BuildStartInfo(manifest, resolver, 15420, 15100, out string exe, out string reason);
        Assert.NotNull(psi);
        Assert.Equal(workerBat, exe);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(installDir, psi!.WorkingDirectory);
        Assert.Contains("--worker-tag", psi.ArgumentList);
        Assert.Contains("--managed-by-mother", psi.ArgumentList);
        Assert.Contains("15420", psi.ArgumentList);
        Assert.Equal("1", psi.Environment["MANAGED_BY_SUITE"]);
        Assert.Equal("http://127.0.0.1:15100", psi.Environment["MOTHER_GATEWAY_URL"]);

        string outsideDir = Path.Combine(Path.GetTempPath(), $"escape-{Guid.NewGuid():N}");
        var escapedManifest = manifest with { InstallDir = outsideDir, Executable = "outside.exe" };
        Assert.Throws<InvalidOperationException>(() =>
            WorkerLaunchCommandBuilder.BuildStartInfo(escapedManifest, resolver, 15420, 15100, out _, out _));
    }

    [Fact]
    public async Task ScheduleEvaluator_DefersBlockedScheduleAndPersistsSchedulerStateJson()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);

        File.WriteAllText(Path.Combine(manifestsDir, "housekeeping.json"), """
        { "schemaVersion": 2, "appId": "housekeeping", "name": "HouseKeeping" }
        """);
        File.WriteAllText(Path.Combine(manifestsDir, "shadowlibrary.json"), """
        {
          "schemaVersion": 2,
          "appId": "shadowlibrary",
          "name": "ShadowLibrary",
          "residencyMode": "cold",
          "mutuallyExclusiveWith": ["housekeeping"],
          "schedules": [
            {
              "scheduleId": "verify-d",
              "name": "Verify D Drive",
              "cadence": "daily",
              "timeUtc": "00:00",
              "runIfMissed": true,
              "targetDrives": ["D:\\"]
            }
          ]
        }
        """);

        Assert.True(PidSafeLockfile.TryAcquire(
            Path.Combine(scope.DataDir, "hk.hklock"), "housekeeping", "scan", out var hkLock, out _, TimeSpan.FromMinutes(5)));
        using var guard = hkLock;

        using var runtime = scope.CreateRuntime();
        await runtime.Scheduler.EvaluateDueSchedulesAsync(
            runtime.Manifests.GetAll(),
            default,
            utcNowOverride: DateTime.UtcNow.AddHours(1));

        var deferrals = await runtime.Db.GetDeferralsAsync(10);
        Assert.Contains(deferrals, d => d.AppId == "shadowlibrary" && d.JobId == "verify-d");

        string stateFile = Path.Combine(scope.DataDir, "scheduler-state.json");
        Assert.True(File.Exists(stateFile));
        Assert.Contains("verify-d", File.ReadAllText(stateFile));
    }

    [Fact]
    public async Task ColdWakeCoordinator_HibernatesIdleColdWorkerAfterTimeout()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        string workerDir = Path.Combine(scope.ProgramsDir, "coldworker");
        Directory.CreateDirectory(manifestsDir);
        Directory.CreateDirectory(workerDir);

        string batPath = Path.Combine(workerDir, "sleep-worker.bat");
        File.WriteAllText(batPath, "@echo off\r\nping -n 30 127.0.0.1 >nul\r\n");

        File.WriteAllText(Path.Combine(manifestsDir, "redroom.json"), $$"""
        {
          "schemaVersion": 2,
          "appId": "redroom",
          "name": "Redroom",
          "installDir": "{{workerDir.Replace("\\", "\\\\")}}",
          "executable": "sleep-worker.bat",
          "residencyMode": "cold",
          "idleTimeoutMinutes": 5,
          "sandboxPort": {{GetFreeTcpPort()}}
        }
        """);

        using var runtime = scope.CreateRuntime();
        Assert.True(runtime.Supervisor.TryStartProcess("redroom", out string msg), msg);
        runtime.Supervisor.UpdateProbeResult("redroom", isOnline: true, activeJobs: 0, queuedJobs: 0);
        Assert.NotNull(runtime.Supervisor.GetSnapshot("redroom")!.Pid);

        IReadOnlyList<string> hibernated = await runtime.ColdWake.EvaluateIdleHibernationAsync(
            runtime.Manifests.GetAll(),
            runtime.Supervisor.GetSnapshots(),
            utcNowOverride: DateTime.UtcNow.AddMinutes(10));

        Assert.Contains("redroom", hibernated);
        Assert.Null(runtime.Supervisor.GetSnapshot("redroom")!.Pid);
    }

    [Fact]
    public async Task KestrelGateway_SchedulerDefer_SubpathRedirect_AndExpandedTerminalCommandsWork()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);

        int upstreamPort = GetFreeTcpPort();
        string? observedPrefix = null;
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls($"http://127.0.0.1:{upstreamPort}");
        var upstreamApp = upstreamBuilder.Build();
        upstreamApp.MapGet("/", () => Results.Content("<html><body>Greenroom Root</body></html>", "text/html"));
        upstreamApp.MapGet("/sse", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            await ctx.Response.WriteAsync("event: init\ndata: {\"ok\":true}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ctx.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // Upstream exits cleanly once the proxied client aborts the SSE stream.
            }
        });
        upstreamApp.MapGet("/ping", (HttpContext ctx) =>
        {
            observedPrefix = ctx.Request.Headers["X-Forwarded-Prefix"].ToString();
            return Results.Ok(new { ok = true, prefix = observedPrefix });
        });
        await upstreamApp.StartAsync();

        File.WriteAllText(Path.Combine(manifestsDir, "greenroom.json"), $$"""
        {
          "schemaVersion": 2,
          "appId": "greenroom",
          "name": "Greenroom",
          "internalPort": 8420,
          "sandboxPort": {{upstreamPort}},
          "residencyMode": "warm"
        }
        """);

        int motherPort = GetFreeTcpPort();
        using var runtime = scope.CreateRuntime("--port", motherPort.ToString());
        runtime.Supervisor.UpdateProbeResult("greenroom", isOnline: true, activeJobs: 0, queuedJobs: 0);

        var motherApp = MotherWebHost.BuildWebApplication(runtime);
        await motherApp.StartAsync();

        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{motherPort}") };

            var deferResp = await http.PostAsync("/api/v1/scheduler/defer",
                new StringContent("""{"appId":"greenroom","scheduleId":"plex-sync","deferMinutes":20,"deferReason":"Disk busy"}""",
                    Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, deferResp.StatusCode);
            string deferJson = await deferResp.Content.ReadAsStringAsync();
            Assert.Contains("plex-sync", deferJson);

            var getDeferResp = await http.GetAsync("/api/v1/scheduler/defer");
            Assert.Equal(HttpStatusCode.OK, getDeferResp.StatusCode);
            Assert.Contains("plex-sync", await getDeferResp.Content.ReadAsStringAsync());

            var redirectResp = await http.GetAsync("/app/greenroom?tab=library");
            Assert.Equal(HttpStatusCode.TemporaryRedirect, redirectResp.StatusCode);
            Assert.Equal("/app/greenroom/?tab=library", redirectResp.Headers.Location?.OriginalString);

            var rootUiResp = await http.GetAsync("/app/greenroom/?tab=library");
            Assert.Equal(HttpStatusCode.OK, rootUiResp.StatusCode);
            string rootUiHtml = await rootUiResp.Content.ReadAsStringAsync();
            Assert.Contains("Greenroom Root", rootUiHtml);

            using (var sseCts = new System.Threading.CancellationTokenSource())
            using (var sseReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "/app/greenroom/sse"))
            using (var sseResp = await http.SendAsync(sseReq, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, sseCts.Token))
            {
                Assert.Equal(HttpStatusCode.OK, sseResp.StatusCode);
                await using var sseStream = await sseResp.Content.ReadAsStreamAsync(sseCts.Token);
                using var sseReader = new System.IO.StreamReader(sseStream);
                string? firstSseLine = await sseReader.ReadLineAsync(sseCts.Token);
                Assert.Equal("event: init", firstSseLine);
                sseCts.Cancel();
            }

            var pingResp = await http.GetAsync("/app/greenroom/ping");
            Assert.Equal(HttpStatusCode.OK, pingResp.StatusCode);
            Assert.Equal("/app/greenroom", observedPrefix);

            foreach (string cmd in new[] { "status", "traffic", "logs 5", "reload", "power Sleep" })
            {
                var termResp = await http.PostAsync("/api/v1/terminal/exec",
                    new StringContent(JsonSerializer.Serialize(new { command = cmd }), Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, termResp.StatusCode);
                string body = await termResp.Content.ReadAsStringAsync();
                Assert.Contains("\"accepted\":true", body);
            }

            var badPowerResp = await http.PostAsync("/api/v1/terminal/exec",
                new StringContent("""{"command":"power InvalidMode"}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, badPowerResp.StatusCode);
            string badBody = await badPowerResp.Content.ReadAsStringAsync();
            Assert.Contains("\"accepted\":false", badBody);
            Assert.Contains("Invalid power policy", badBody);
        }
        finally
        {
            await motherApp.StopAsync();
            await motherApp.DisposeAsync();
            await upstreamApp.StopAsync();
            await upstreamApp.DisposeAsync();
        }
    }

    [Fact]
    public void MotherInstallerRunner_BuildOptionsAndDryRunMigrateLegacyWork()
    {
        using var scope = new TempWorkspaceScope();
        var sandbox = new SandboxEnvironment("mother", new[] { "--test-mode", "--data-dir", scope.DataDir }, scope.BaseDir, "install-config.json");
        var theme = MotherInstallerRunner.BuildOptions(sandbox, "0.2.0");

        Assert.Equal("StreamItAll", theme.StudioName);
        Assert.Equal("Mother", theme.SuiteName);
        Assert.Equal(Path.Combine("StreamItAll", "Mother", "Mother"), theme.UmbrellaSubpath);
        Assert.Equal("full", theme.DefaultProfileId);
        Assert.True(theme.AllowAutoStart);
        Assert.True(theme.AllowLegacyMigration);

        string stageDir = Path.Combine(scope.RootDir, "inst-stage");
        string payloadZip = Path.Combine(scope.RootDir, "inst-payload.zip");
        Directory.CreateDirectory(Path.Combine(stageDir, "app"));
        File.WriteAllText(Path.Combine(stageDir, "Mother.exe"), "stub-launcher");
        File.WriteAllText(Path.Combine(stageDir, "app", "Mother.App.exe"), "stub-app");
        ZipFile.CreateFromDirectory(stageDir, payloadZip);

        string targetDir = Path.Combine(scope.DataDir, "umbrella-install");
        int code = Mother.Installer.Program.Main(new[]
        {
            "--test-mode", "--silent", "--dry-run", "--headless", "--migrate-legacy",
            "--payload", payloadZip, "--install-dir", targetDir
        });
        Assert.Equal(0, code);

        Directory.CreateDirectory(Path.Combine(targetDir, "app"));
        string canaryExe = Path.Combine(targetDir, "app", "Mother.App.exe");
        File.WriteAllText(canaryExe, "keep-me");
        int dryUninstallCode = Mother.Installer.Program.Main(new[]
        {
            "--test-mode", "--silent", "--uninstall", "--dry-run", "--headless",
            "--install-dir", targetDir
        });
        Assert.Equal(0, dryUninstallCode);
        Assert.True(File.Exists(canaryExe), "--uninstall --dry-run must preserve installed files");

        string siblingInstallDir = Path.Combine(scope.RootDir, "Sandbox", "installed", "Mother");
        Directory.CreateDirectory(siblingInstallDir);
        string siblingAppDir = Path.Combine(siblingInstallDir, "app");
        Directory.CreateDirectory(siblingAppDir);
        string siblingCanary = Path.Combine(siblingAppDir, "Mother.App.exe");
        File.WriteAllText(siblingCanary, "keep-sibling");
        string siblingDataDir = Path.Combine(scope.RootDir, "Sandbox", "data");
        Directory.CreateDirectory(siblingDataDir);
        int siblingUninstallCode = Mother.Installer.Program.Main(new[]
        {
            "--test-mode", "--silent", "--uninstall", "--dry-run", "--headless",
            "--data-dir", siblingDataDir, "--install-dir", siblingInstallDir
        });
        Assert.Equal(0, siblingUninstallCode);
        Assert.True(File.Exists(siblingCanary), "Sibling sandbox --uninstall --dry-run must succeed without escaping");

        var noShortcutsPlan = MotherInstallerIntegration.PlanLivePostInstallActions(
            isContainedSandbox: false,
            dryRun: false,
            isWindows: true,
            createShortcuts: false,
            autoStart: true);
        Assert.False(noShortcutsPlan.WriteShortcuts);
        Assert.True(noShortcutsPlan.WriteAutoStart);
        Assert.True(noShortcutsPlan.WriteRegistry);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
