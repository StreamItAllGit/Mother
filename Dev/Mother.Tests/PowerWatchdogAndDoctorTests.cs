// @module: Mother.Tests.PowerWatchdogAndDoctorTests
// @purpose: Verifies simulated Power coordinator countdown/cancellation, simulated Watchdog registration, Fleet Doctor export, and Notifications.
// @depends-on: System, System.IO, System.Threading.Tasks, Xunit, Mother.Core.Contracts
// @exports: PowerWatchdogAndDoctorTests

using System.IO;
using System.Threading.Tasks;
using Mother.Core.Contracts;
using Xunit;

namespace Mother.Tests;

public sealed class PowerWatchdogAndDoctorTests
{
    [Fact]
    public void PowerCoordinator_IsSimulatedInIsolatedModeAndSupportsCountdownCancel()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        Assert.True(runtime.Power.IsSimulated);
        var s = runtime.Settings.GetCurrent();
        s.PostRunIdleSeconds = 5;
        s.PostRunCountdownSeconds = 30;
        runtime.Settings.Save(s);
        runtime.Power.SetPolicy("Sleep");

        var t0 = System.DateTime.UtcNow;
        var busyApp = new[]
        {
            new SisterAppRuntimeDto(
                "housekeeping", "HouseKeeping", "1.0.0", "#f59e0b", 5178, 5178,
                "warm", "online", true, 123, 1, 0, 0, false, "http://127.0.0.1:5178/",
                System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>(),
                System.Array.Empty<string>(), null, null)
        };
        var idleApp = new[]
        {
            new SisterAppRuntimeDto(
                "housekeeping", "HouseKeeping", "1.0.0", "#f59e0b", 5178, 5178,
                "warm", "online", true, 123, 0, 0, 0, false, "http://127.0.0.1:5178/",
                System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>(),
                System.Array.Empty<string>(), null, null)
        };

        runtime.Power.EvaluateTick(busyApp, liveLockCount: 0, watchdogRegistered: true, nowUtcOverride: t0);
        runtime.Power.EvaluateTick(idleApp, liveLockCount: 0, watchdogRegistered: true, nowUtcOverride: t0.AddSeconds(1));
        var status = runtime.Power.EvaluateTick(idleApp, liveLockCount: 0, watchdogRegistered: true, nowUtcOverride: t0.AddSeconds(7));

        Assert.True(status.Simulated);
        Assert.True(status.CountdownActive);

        var cancelled = runtime.Power.CancelCountdown();
        Assert.False(cancelled.CountdownActive);
        Assert.Equal(0, runtime.Power.SimulatedPowerExecutions);
    }

    [Fact]
    public void WatchdogScheduler_SimulatesRegistrationInIsolatedModeWithoutTouchingWindowsTaskScheduler()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        Assert.True(runtime.Watchdog.IsSimulated);
        Assert.False(runtime.Watchdog.IsRegistered());
        Assert.True(runtime.Watchdog.EnsureRegistered(out string msg));
        Assert.True(runtime.Watchdog.IsRegistered());
        Assert.Contains("SIMULATED", msg, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FleetDoctor_RunsChecksAndExportsDiagnosticZipBundle()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        var report = runtime.RunDoctor();
        Assert.NotEmpty(report.SuiteChecks);
        Assert.Equal(1, report.SqliteUserVersion);

        string zipPath = await runtime.Doctor.ExportDiagnosticBundleAsync(report);
        Assert.True(File.Exists(zipPath));
        Assert.True(new FileInfo(zipPath).Length > 0);
    }

    [Fact]
    public async Task NotificationDispatcher_RecordsSimulatedAlertInSqlite()
    {
        using var scope = new TempWorkspaceScope();
        using var runtime = scope.CreateRuntime();

        var rec = await runtime.Notifications.DispatchAsync("mother", "unit-test", "Unit Test Alert", "Test body");
        Assert.Equal("simulated", rec.Status);

        var history = await runtime.Notifications.GetHistoryAsync(10);
        Assert.Single(history);
        Assert.Equal("unit-test", history[0].Slug);
    }

    [Fact]
    public void ProcessSupervisor_UpdatesResidencyAndReflectsInSettingsOverrides()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);
        File.WriteAllText(Path.Combine(manifestsDir, "housekeeping.json"), """
        { "schemaVersion": 2, "appId": "housekeeping", "name": "HouseKeeping", "residencyMode": "warm" }
        """);
        using var runtime = scope.CreateRuntime();

        var updated = runtime.Supervisor.SetResidency("housekeeping", "cold");
        Assert.NotNull(updated);
        Assert.Equal("cold", updated!.ResidencyMode);
        Assert.Equal("cold", runtime.Settings.GetCurrent().ResidencyOverrides["housekeeping"]);
    }

    [Fact]
    public async Task ColdWakeCoordinator_RefusesDisabledApplication()
    {
        using var scope = new TempWorkspaceScope();
        string manifestsDir = Path.Combine(scope.DataDir, "manifests");
        Directory.CreateDirectory(manifestsDir);
        File.WriteAllText(Path.Combine(manifestsDir, "housekeeping.json"), """
        { "schemaVersion": 2, "appId": "housekeeping", "name": "HouseKeeping", "residencyMode": "warm" }
        """);
        using var runtime = scope.CreateRuntime();

        var entry = runtime.Supervisor.SetResidency("housekeeping", "disabled")!;
        var outcome = await runtime.ColdWake.WakeAsync(entry, timeoutMs: 1000);
        Assert.False(outcome.Woken);
        Assert.Contains("disabled", outcome.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
