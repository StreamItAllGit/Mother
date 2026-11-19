// @module: Mother.Tests.ManifestAndTrafficCopTests
// @purpose: Validates v1/v2 sister manifest parsing, PID-safe .hklock scanning/purging, coexistence arbitration, and DPAPI metadata inspection.
// @depends-on: System, System.IO, System.Linq, System.Security.Cryptography, System.Text, System.Threading.Tasks, Xunit, Mother.Core.Features.Coexistence, Mother.Core.Features.Discovery, Mother.Core.Features.TrafficCop
// @exports: ManifestAndTrafficCopTests

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Mother.Core.Features.Discovery;
using Mother.Core.Features.TrafficCop;
using Xunit;

namespace Mother.Tests;

public sealed class ManifestAndTrafficCopTests
{
    [Fact]
    public void ManifestRegistry_ParsesBothV1AndV2ManifestsWithoutHardcodedFallbacks()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);

        File.WriteAllText(Path.Combine(manifestsDir, "housekeeping.json"), """
        {
          "app_id": "housekeeping",
          "display_name": "HouseKeeping",
          "version": "1.0.0",
          "port": 5178,
          "residency": "warm",
          "target_drives": ["D:\\", "E:\\"]
        }
        """);

        File.WriteAllText(Path.Combine(manifestsDir, "shadowlibrary.json"), """
        {
          "schemaVersion": 2,
          "appId": "shadowlibrary",
          "name": "ShadowLibrary",
          "version": "0.2.2-hf.2",
          "internalPort": 5179,
          "residencyMode": "cold",
          "sharedMounts": ["D:\\Media"],
          "mutuallyExclusiveWith": ["housekeeping"]
        }
        """);

        using var runtime = scope.CreateRuntime();
        var apps = runtime.Manifests.GetAll();
        var hk = apps.First(a => a.AppId == "housekeeping");
        var sl = apps.First(a => a.AppId == "shadowlibrary");

        Assert.Equal("v1", hk.DetectedSchemaFormat);
        Assert.Equal("v2", sl.DetectedSchemaFormat);
        Assert.Equal(5179, runtime.Manifests.GetEffectivePort(sl));
        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public async Task HkLockScanner_DistinguishesLiveAndStaleLocksAndPurgesOnlyStale()
    {
        using var scope = new TempWorkspaceScope();
        string livePath = Path.Combine(scope.DataDir, "live-test.hklock");
        string stalePath = Path.Combine(scope.DataDir, "stale-test.hklock");

        Assert.True(Foundation.Core.PidSafeLockfile.TryAcquire(
            livePath, "housekeeping", "verify", out var liveLock, out _, TimeSpan.FromMinutes(10)));
        using var lockGuard = liveLock;

        File.WriteAllText(stalePath, """
        {
          "schemaVersion": 1,
          "appId": "shadowlibrary",
          "pid": 99999999,
          "processStartTimeUtc": "2020-01-01T00:00:00.000Z",
          "hostName": "TEST",
          "operation": "scan",
          "acquiredAtUtc": "2020-01-01T00:00:00.000Z",
          "heartbeatAtUtc": "2020-01-01T00:00:00.000Z",
          "expiresAtUtc": "2020-01-01T00:10:00.000Z"
        }
        """);

        using var runtime = scope.CreateRuntime();
        var scanned = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll());
        Assert.Contains(scanned, l => l.LockId == "live-test.hklock" && l.Status == "live");
        Assert.Contains(scanned, l => l.LockId == "stale-test.hklock" && l.Status == "stale");

        int purged = await runtime.TrafficCop.PurgeStaleLocksAsync(runtime.Manifests.GetAll());
        Assert.Equal(1, purged);
        Assert.True(File.Exists(livePath));
        Assert.False(File.Exists(stalePath));
    }

    [Fact]
    public void CoexistenceArbitrator_BlocksMutuallyExclusiveColdWakeWhenPeerHasLiveLock()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);
        string sharedMount = Path.Combine(scope.DataDir, "shared-media");
        Directory.CreateDirectory(sharedMount);

        File.WriteAllText(Path.Combine(manifestsDir, "shadowlibrary.json"), $$"""
        {
          "schemaVersion": 2,
          "appId": "shadowlibrary",
          "name": "ShadowLibrary",
          "mutuallyExclusiveWith": ["housekeeping"],
          "sharedMounts": ["{{sharedMount.Replace("\\", "\\\\")}}"]
        }
        """);
        File.WriteAllText(Path.Combine(manifestsDir, "housekeeping.json"), $$"""
        {
          "schemaVersion": 2,
          "appId": "housekeeping",
          "name": "HouseKeeping",
          "sharedMounts": ["{{sharedMount.Replace("\\", "\\\\")}}"]
        }
        """);
        Assert.True(Foundation.Core.PidSafeLockfile.TryAcquire(
            Path.Combine(scope.DataDir, "hk.hklock"), "housekeeping", "dedup", out var liveLock, out _, TimeSpan.FromMinutes(5)));
        using var lockGuard = liveLock;

        using var runtime = scope.CreateRuntime();
        var sl = runtime.Manifests.GetById("shadowlibrary")!;
        var locks = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll());
        var decision = runtime.Coexistence.ArbitrateColdWake(sl, runtime.Supervisor.GetSnapshots(), locks);

        Assert.False(decision.Allowed);
        Assert.Equal("defer", decision.Decision);
        Assert.Equal("housekeeping", decision.BlockingAppId);
    }

    [Fact]
    public void CoexistenceArbitrator_ApplyRecommendationsWritesSharedCoexistenceJson()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();
        string sharedDir = Path.Combine(scope.DataDir, "shared-media");

        var updated = runtime.Coexistence.ApplyRecommendations(
            new[] { "housekeeping", "shadowlibrary" },
            "suite-safe-stagger",
            new[] { sharedDir });
        Assert.Single(updated);
        Assert.True(File.Exists(Path.Combine(sharedDir, ".coexistence.json")));
    }

    [Fact]
    public void SecretVaultInspector_ReadsKeyNamesWithoutDecryptingPlaintext()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);

        var vault = new Foundation.Core.DpapiSecretStore(scope.DataDir, "redroom.dpapi");
        vault.SetSecret("twitch_oauth_token", "TOP_SECRET_VALUE");
        vault.SetSecret("igdb_api_key", "ANOTHER_SECRET");

        File.WriteAllText(Path.Combine(manifestsDir, "redroom.json"), $$"""
        {
          "schemaVersion": 2,
          "appId": "redroom",
          "name": "RedRoom",
          "secretVaultPath": "{{vault.SecretsFilePath.Replace("\\", "\\\\")}}"
        }
        """);

        using var runtime = scope.CreateRuntime();
        var keys = runtime.Vaults.InspectAll(runtime.Manifests.GetAll());

        Assert.Contains(keys, k => k.AppId == "redroom" && k.KeyName == "twitch_oauth_token" && k.VaultSizeBytes > 0);
        Assert.Contains(keys, k => k.AppId == "redroom" && k.KeyName == "igdb_api_key");
    }

    [Fact]
    public void HkLockScanner_ParsesAcquiredAtUtcAndFallsBackToCreatedAtUtc()
    {
        using var scope = new TempWorkspaceScope();
        string primaryPath = Path.Combine(scope.DataDir, "acquired.hklock");
        string fallbackPath = Path.Combine(scope.DataDir, "legacy.hklock");

        File.WriteAllText(primaryPath, """
        {
          "appId": "shadowlibrary",
          "pid": 999991,
          "hostName": "ISOLATED-HOST",
          "operation": "archive-verify",
          "acquiredAtUtc": "2020-01-01T00:00:00.000Z",
          "createdAtUtc": "2019-12-31T23:59:59.000Z",
          "expiresAtUtc": "2020-01-01T00:05:00.000Z"
        }
        """);

        File.WriteAllText(fallbackPath, """
        {
          "appId": "housekeeping",
          "pid": 999992,
          "hostName": "ISOLATED-HOST",
          "operation": "legacy-job",
          "createdAtUtc": "2021-05-06T07:08:09.123Z"
        }
        """);

        var scanner = new HkLockScanner();
        var snapshots = scanner.Scan(new[] { scope.DataDir });

        var acquired = Assert.Single(snapshots, s => s.LockId == "acquired.hklock");
        Assert.Equal("2020-01-01T00:00:00.000Z", acquired.AcquiredAtUtc);
        Assert.Equal("stale", acquired.Status);

        var legacy = Assert.Single(snapshots, s => s.LockId == "legacy.hklock");
        Assert.Equal("2021-05-06T07:08:09.123Z", legacy.AcquiredAtUtc);
        Assert.Equal("stale", legacy.Status);
    }
}
