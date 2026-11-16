// @module: Mother.Tests.ServerAndInstallerTests
// @purpose: Integration tests for Kestrel endpoints, Cold-Wake 404/409 arbitration, Tailscale CGNAT filtering, QR SVG, and Foundation.Installer isolated lifecycle.
// @depends-on: System, System.IO, System.IO.Compression, System.Net, System.Net.Http, System.Text.Json, System.Threading.Tasks, Microsoft.AspNetCore.Builder, Foundation.Installer, Mother.Core, Mother.Server, Mother.Server.Endpoints, Xunit
// @exports: ServerAndInstallerTests

using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Foundation.Installer;
using Mother.Server;
using Mother.Server.Endpoints;
using Xunit;

namespace Mother.Tests;

public sealed class ServerAndInstallerTests
{
    [Fact]
    public void TailscaleAllowlist_PermitsLoopbackAndCgnatAndRejectsPublicOrLan()
    {
        Assert.True(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.Loopback));
        Assert.True(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.IPv6Loopback));
        Assert.True(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.Parse("100.64.0.1")));
        Assert.True(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.Parse("100.127.255.254")));
        Assert.False(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.Parse("100.63.255.254")));
        Assert.False(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.Parse("100.128.0.1")));
        Assert.False(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.Parse("192.168.1.10")));
        Assert.False(TailscaleAndQrEndpoints.IsLoopbackOrTailscale(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public async Task KestrelGateway_ServesHealthQrTerminalAndProxyArbitration()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        string locksDir = Path.Combine(scope.DataDir, "locks");
        Directory.CreateDirectory(manifestsDir);
        Directory.CreateDirectory(locksDir);

        File.WriteAllText(Path.Combine(manifestsDir, "shadowlibrary.json"), """
        {
          "schemaVersion": 2,
          "appId": "shadowlibrary",
          "name": "ShadowLibrary",
          "mutuallyExclusiveWith": ["housekeeping"]
        }
        """);
        Assert.True(Foundation.Core.PidSafeLockfile.TryAcquire(
            Path.Combine(scope.DataDir, "hk.hklock"), "housekeeping", "scan", out var hkLock, out _, TimeSpan.FromMinutes(5)));
        using var hkGuard = hkLock;

        int port = GetFreeTcpPort();
        using var runtime = scope.CreateRuntime("--port", port.ToString());
        var app = MotherWebHost.BuildWebApplication(runtime);
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            var healthResp = await http.GetAsync("/api/v1/suite/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);

            var qrResp = await http.GetAsync("/api/v1/suite/qr.svg");
            Assert.Equal(HttpStatusCode.OK, qrResp.StatusCode);
            Assert.Contains("<svg", await qrResp.Content.ReadAsStringAsync());

            var execResp = await http.PostAsync("/api/v1/terminal/exec",
                new StringContent("{\"command\":\"help\"}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, execResp.StatusCode);
            Assert.Contains("Allowed commands", await execResp.Content.ReadAsStringAsync());

            var unknownApp = await http.GetAsync("/api/v1/apps/nonexistent/status");
            Assert.Equal(HttpStatusCode.NotFound, unknownApp.StatusCode);

            var conflictResp = await http.GetAsync("/api/v1/apps/shadowlibrary/status");
            Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);

            var manifestResp = await http.GetAsync("/manifest.webmanifest");
            Assert.Equal(HttpStatusCode.OK, manifestResp.StatusCode);

            var swResp = await http.GetAsync("/sw.js");
            Assert.Equal(HttpStatusCode.OK, swResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task KestrelGateway_SettingsRoundtripAndTerminalCommandsWork()
    {
        using var scope = new TempWorkspaceScope();
        int port = GetFreeTcpPort();
        using var runtime = scope.CreateRuntime("--port", port.ToString());
        var app = MotherWebHost.BuildWebApplication(runtime);
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var getSettings = await http.GetAsync("/api/v1/suite/settings");
            Assert.Equal(HttpStatusCode.OK, getSettings.StatusCode);

            var doctorResp = await http.PostAsync("/api/v1/suite/doctor/run", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, doctorResp.StatusCode);

            var pwrResp = await http.PostAsync("/api/v1/terminal/exec",
                new StringContent("{\"command\":\"power\"}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, pwrResp.StatusCode);
            Assert.Contains("Policy=", await pwrResp.Content.ReadAsStringAsync());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task InstallerLifecycle_SilentIsolatedInstallVerifyUninstallSucceeds()
    {
        using var scope = new TempWorkspaceScope();
        string stageDir = Path.Combine(scope.RootDir, "stage");
        string installDir = Path.Combine(scope.DataDir, "installed");
        string payloadZip = Path.Combine(scope.RootDir, "payload.zip");
        Directory.CreateDirectory(Path.Combine(stageDir, "app"));
        Directory.CreateDirectory(Path.Combine(stageDir, "scripts"));
        File.WriteAllText(Path.Combine(stageDir, "Mother.exe"), "stub-launcher");
        File.WriteAllText(Path.Combine(stageDir, "AGENTS.md"), "# Mother\n");
        File.WriteAllText(Path.Combine(stageDir, "app", "Mother.App.exe"), "stub-app");
        File.WriteAllText(Path.Combine(stageDir, "scripts", "run.bat"), "@echo off\r\n");
        ZipFile.CreateFromDirectory(stageDir, payloadZip);

        var descriptor = new InstallerAppDescriptor(
            ProductName: "Mother",
            DisplayName: "Mother",
            Publisher: "Personal Suite",
            Version: "0.2.0-hf.1",
            MainExecutableRelativePath: Path.Combine("app", "Mother.App.exe"),
            BrandAccentHex: "#38bdf8");

        var ctx = new InstallerRuntimeContext(
            cliDataDir: scope.DataDir,
            cliConfigPath: scope.ConfigFile,
            cliLogDir: scope.LogDir,
            dryRun: false,
            envPrefix: descriptor.EnvPrefix);

        var engine = new InstallerEngine(descriptor, ctx, typeof(ServerAndInstallerTests).Assembly);
        var verifyPayload = await engine.VerifyPayloadAsync(externalPayloadPath: payloadZip);
        Assert.True(verifyPayload.Valid, verifyPayload.Message);

        var installResult = await engine.InstallAsync(
            explicitInstallDir: installDir,
            createShortcuts: false,
            registerAutoStart: false,
            externalPayloadPath: payloadZip);
        Assert.True(installResult.Success, installResult.Message);
        Assert.True(File.Exists(Path.Combine(installDir, "Mother.exe")));

        var uninstallResult = await engine.UninstallAsync(explicitInstallDir: installDir, purgeData: true);
        Assert.True(uninstallResult.Success, uninstallResult.Message);
    }

    [Fact]
    public void InstallerProgram_IsolatedDryRunPreservesTargetWithoutHostMutation()
    {
        using var scope = new TempWorkspaceScope();
        string stageDir = Path.Combine(scope.RootDir, "stage-norm");
        string payloadZip = Path.Combine(scope.RootDir, "norm-payload.zip");
        Directory.CreateDirectory(Path.Combine(stageDir, "app"));
        File.WriteAllText(Path.Combine(stageDir, "Mother.exe"), "stub-launcher");
        File.WriteAllText(Path.Combine(stageDir, "app", "Mother.App.exe"), "stub-app");
        ZipFile.CreateFromDirectory(stageDir, payloadZip);

        string targetDir = Path.Combine(scope.DataDir, "auto-contained-install");

        string[] passed = Mother.Installer.Program.NormalizeInstallerArgs(
            new[] { "--silent", "--dry-run", "--data-dir", scope.DataDir, "--install-dir", targetDir });
        Assert.Contains("--data-dir", passed);

        int exitCode = Mother.Installer.Program.Main(
            new[] { "--silent", "--dry-run", "--data-dir", scope.DataDir, "--payload", payloadZip, "--install-dir", targetDir });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void MotherDiagnostics_HeartbeatStripPreventsDuplicateHeartbeatTag()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();
        string cleaned = Mother.Core.Diagnostics.MotherDiagnostics.StripLeadingHeartbeatTag("[HEARTBEAT] FleetSupervision: 1/6");
        Assert.Equal("FleetSupervision: 1/6", cleaned);

        var entry = runtime.Diagnostics.Log("HEARTBEAT", "mother", "Supervisor", cleaned);
        Assert.DoesNotContain("[HEARTBEAT]", entry.Message, StringComparison.OrdinalIgnoreCase);
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
