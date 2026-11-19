// @module: Mother.Tests.V030PublicBetaTests
// @purpose: Verifies v0.3.0 universal core (0-app empty state, Doctor pass, 503 NO_HTTP_ENDPOINT, ISO QR SVG), Settings v1->v2 migration, discovery precedence, launcher args, manifest v2, and fail-closed exit codes.
// @depends-on: System, System.Collections.Generic, System.IO, System.Net, System.Net.Http, System.Net.Sockets, System.Text, System.Text.Json, System.Threading.Tasks, Microsoft.AspNetCore.Builder, Foundation.Core, Foundation.Installer, Mother.App, Mother.Core, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Installer, Mother.Launcher, Mother.Server, Xunit
// @exports: V030PublicBetaTests

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Microsoft.AspNetCore.Builder;
using Mother.Core;
using Mother.Core.Configuration;
using Mother.Installer;
using Mother.Launcher;
using Mother.Server;
using Xunit;

namespace Mother.Tests;

public sealed class V030PublicBetaTests
{
    [Fact]
    public void SettingsMigration_MigratesLegacyV1FileToDataConfigSettingsJsonAndPreservesUnknowns()
    {
        using var scope = new TempWorkspaceScope();
        string legacyPath = Path.Combine(scope.DataDir, "settings.json");
        string expectedNewPath = Path.Combine(scope.DataDir, "config", "settings.json");
        File.WriteAllText(legacyPath, """
        {
          "schemaVersion": 1,
          "useGlobalFormatRules": true,
          "port": 5111,
          "allowTailscaleBind": true,
          "coldWakeIdleMinutes": 42,
          "sleepInhibitDuringActiveLock": false,
          "autoAlignWithSisterApps": true,
          "desktopToastNotifications": false,
          "discordWebhookNotifications": true,
          "customPluginKey": "keep-me-intact"
        }
        """);

        using var runtime = new MotherRuntime(
            new[] { "--data-dir", scope.DataDir, "--log-dir", scope.LogDir, "--programs-dir", scope.ProgramsDir },
            scope.BaseDir,
            skipIpcMutex: true);

        Assert.Equal(Path.GetFullPath(expectedNewPath), Path.GetFullPath(runtime.Paths.ConfigFilePath));
        Assert.True(File.Exists(expectedNewPath));
        Assert.True(File.Exists(legacyPath + ".migrated.bak"));
        Assert.False(File.Exists(legacyPath));

        MotherSettings cfg = runtime.Settings.GetCurrent();
        Assert.Equal(2, cfg.SchemaVersion);
        Assert.Equal(5111, cfg.Port);
        Assert.True(cfg.ShouldAllowTailscaleBind);
        Assert.Equal(42, cfg.IdleTimeoutMinutes);
        Assert.False(cfg.ShouldInhibitSleepDuringActiveLock);
        Assert.True(cfg.ShouldAutoAlignSisterApps);
        Assert.False(cfg.ShouldShowDesktopToasts);
        Assert.True(cfg.ShouldSendDiscordWebhooks);
        Assert.NotNull(cfg.ExtensionData);
        Assert.True(cfg.ExtensionData!.ContainsKey("customPluginKey"));
        Assert.Equal("keep-me-intact", cfg.ExtensionData["customPluginKey"].GetString());
        Assert.False(cfg.ExtensionData.ContainsKey("coldWakeIdleMinutes"));
        Assert.False(cfg.ExtensionData.ContainsKey("allowTailscaleBind"));
    }

    [Fact]
    public void SettingsStore_ExplicitConfigUpgradeAndRollbackNormalizeV1Payloads()
    {
        using var scope = new TempWorkspaceScope();
        File.WriteAllText(scope.ConfigFile, """
        {
          "schemaVersion": 1,
          "coldWakeIdleMinutes": 77,
          "autoAlignWithSisterApps": true,
          "thirdPartyNote": 123
        }
        """);

        using var runtime = scope.CreateRuntime();
        MotherSettings migrated = runtime.Settings.GetCurrent();
        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(77, migrated.IdleTimeoutMinutes);
        Assert.True(migrated.ShouldAutoAlignSisterApps);
        Assert.Equal(123, migrated.ExtensionData!["thirdPartyNote"].GetInt32());

        MotherSettings updated = runtime.Settings.GetCurrent();
        updated.IdleTimeoutMinutes = 99;
        runtime.Settings.Save(updated);

        string historyDir = Path.Combine(scope.DataDir, "config_history");
        Directory.CreateDirectory(historyDir);
        string syntheticV1Snap = Path.Combine(historyDir, "settings.json.19990101_000000_v1test.bak");
        File.WriteAllText(syntheticV1Snap, """
        {
          "schemaVersion": 1,
          "coldWakeIdleMinutes": 23,
          "discordWebhookNotifications": true
        }
        """);

        MotherSettings rolled = runtime.Settings.Rollback("settings.json.19990101_000000_v1test.bak");
        Assert.Equal(2, rolled.SchemaVersion);
        Assert.Equal(23, rolled.IdleTimeoutMinutes);
        Assert.True(rolled.ShouldSendDiscordWebhooks);
        Assert.False(rolled.ExtensionData is not null && rolled.ExtensionData.ContainsKey("coldWakeIdleMinutes"));
    }

    [Fact]
    public async Task UniversalCore_ZeroAppsDoctorPasses_ProxyRejectsPortZero_QrUsesIsoQuietZone_AndPutAcceptsV1Aliases()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);
        File.WriteAllText(Path.Combine(manifestsDir, "cli-worker.json"), """
        {
          "schemaVersion": 2,
          "appId": "cli-worker",
          "name": "CLI Worker",
          "internalPort": 0,
          "residencyMode": "cold"
        }
        """);

        int port = GetFreeTcpPort();
        using var runtime = scope.CreateRuntime("--port", port.ToString());

        using (var emptyScope = new TempWorkspaceScope())
        using (var emptyRuntime = emptyScope.CreateRuntime())
        {
            Assert.Empty(emptyRuntime.Manifests.GetAll());
            var doctor = emptyRuntime.RunDoctor();
            Assert.Equal("pass", doctor.Overall);
            Assert.Equal("0.3.0", doctor.SuiteVersion);
        }

        WebApplication app = MotherWebHost.BuildWebApplication(runtime);
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            HttpResponseMessage healthResp = await http.GetAsync("/api/v1/suite/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
            string healthBody = await healthResp.Content.ReadAsStringAsync();
            Assert.Contains("\"sdkVersion\":\"0.5.1\"", healthBody);

            HttpResponseMessage proxyResp = await http.GetAsync("/app/cli-worker/");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, proxyResp.StatusCode);
            string proxyBody = await proxyResp.Content.ReadAsStringAsync();
            Assert.Contains("NO_HTTP_ENDPOINT", proxyBody);

            HttpResponseMessage qrDefaultResp = await http.GetAsync("/api/v1/suite/qr.svg");
            Assert.Equal(HttpStatusCode.OK, qrDefaultResp.StatusCode);
            string svgDefault = await qrDefaultResp.Content.ReadAsStringAsync();

            HttpResponseMessage qrResp = await http.GetAsync("/api/v1/suite/qr.svg?url=" + Uri.EscapeDataString("https://mother-node.example.ts.net:5100/?tab=cli-worker"));
            Assert.Equal(HttpStatusCode.OK, qrResp.StatusCode);
            string svg = await qrResp.Content.ReadAsStringAsync();
            Assert.Contains("viewBox=\"0 0 ", svg);
            Assert.Contains("#0b0e14", svg);
            Assert.Contains("#ffffff", svg);
            Assert.NotEqual(svgDefault, svg);

            HttpResponseMessage putResp = await http.PutAsync("/api/v1/suite/settings", new StringContent("""
            {
              "coldWakeIdleMinutes": 31,
              "autoAlignWithSisterApps": true,
              "preservedUiHint": "beta"
            }
            """, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
            MotherSettings saved = runtime.Settings.GetCurrent();
            Assert.Equal(2, saved.SchemaVersion);
            Assert.Equal(31, saved.IdleTimeoutMinutes);
            Assert.True(saved.ShouldAutoAlignSisterApps);
            Assert.Equal("beta", saved.ExtensionData!["preservedUiHint"].GetString());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void ManifestRegistry_EnforcesHighestToLowestPrecedence_DataManifestsBeatUmbrella()
    {
        using var scope = new TempWorkspaceScope();
        string dataManifests = Path.Combine(scope.DataDir, "manifests");
        string umbrellaEcho = Path.Combine(scope.ProgramsDir, "echo-app");
        Directory.CreateDirectory(dataManifests);
        Directory.CreateDirectory(umbrellaEcho);

        File.WriteAllText(Path.Combine(dataManifests, "echo-app.suite.manifest.json"), """
        { "schemaVersion": 2, "appId": "echo-app", "name": "Echo Override", "version": "9.9.9", "internalPort": 5999 }
        """);
        File.WriteAllText(Path.Combine(umbrellaEcho, "suite.manifest.json"), """
        { "schemaVersion": 2, "appId": "echo-app", "name": "Echo Umbrella", "version": "1.0.0", "internalPort": 5888 }
        """);

        using var runtime = scope.CreateRuntime();
        var apps = runtime.Manifests.GetAll();
        Assert.Single(apps);
        Assert.Equal("Echo Override", apps[0].Name);
        Assert.Equal(5999, apps[0].InternalPort);
    }

    [Fact]
    public void LauncherArgs_InstallerManifest_And_ReleaseAutomationFailClosedWork()
    {
        string callerCwd = Path.Combine(Path.GetTempPath(), "MotherCallerTest");
        string[] normalized = MotherLauncherArgsHelper.NormalizeArgs(
            new[] { "--programs-dir", "relative-apps", "--suite-tab", "echo-app", "--diagnose" },
            callerCwd);
        Assert.Equal(Path.GetFullPath(Path.Combine(callerCwd, "relative-apps")), normalized[1]);
        Assert.Equal("echo-app", normalized[3]);
        Assert.True(MotherLauncherArgsHelper.ShouldWaitForChild(new[] { "--diagnose" }));
        Assert.True(MotherLauncherArgsHelper.ShouldWaitForChild(new[] { "--automation-port", "51999", "--automation-token", "t" }));
        Assert.False(MotherLauncherArgsHelper.ShouldWaitForChild(new[] { "--suite-tab", "echo-app" }));
        Assert.False(MotherLauncherArgsHelper.ShouldWaitForChild(new[] { "--programs-dir", "apps" }));

        InstallerMarkupManifest? markup = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(
            Path.Combine(Path.GetTempPath(), "no_disk_manifest_" + Guid.NewGuid().ToString("N")),
            typeof(Mother.Installer.Program).Assembly);
        Assert.NotNull(markup);
        Assert.Equal(2, markup!.SchemaVersion);
        Assert.Equal("mother", markup.AppId);
        Assert.True(markup.AllowAutoStart);

        Assert.Equal(3, SandboxEnvironmentSeam.ContainmentExitCode);
        Assert.Equal(2, Mother.App.Program.Main(new[] { "--automation-port", "51999", "--automation-token", "rel-test" }));
        Assert.Equal(2, Mother.Installer.Program.Main(new[] { "--automation-port", "51999", "--automation-token", "rel-test" }));
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
