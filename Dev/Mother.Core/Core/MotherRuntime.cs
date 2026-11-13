// @module: Mother.Core.MotherRuntime
// @purpose: Central runtime facade composing Core services and vertical feature slices with coordinated shutdown and background loop.
// @depends-on: System, System.Collections.Generic, System.Linq, System.Threading, System.Threading.Tasks, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics, Mother.Core.Features.Coexistence, Mother.Core.Features.Discovery, Mother.Core.Features.Doctor, Mother.Core.Features.Notifications, Mother.Core.Features.Power, Mother.Core.Features.Supervision, Mother.Core.Features.TrafficCop, Mother.Core.Ipc, Mother.Core.Persistence
// @exports: SuiteHealthDto, MotherRuntime

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;
using Mother.Core.Features.Coexistence;
using Mother.Core.Features.Discovery;
using Mother.Core.Features.Doctor;
using Mother.Core.Features.Notifications;
using Mother.Core.Features.Power;
using Mother.Core.Features.Supervision;
using Mother.Core.Features.TrafficCop;
using Mother.Core.Ipc;
using Mother.Core.Persistence;

namespace Mother.Core;

public sealed record SuiteHealthDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("suiteId")] string SuiteId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sdkVersion")] string SdkVersion,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("uptimeSeconds")] long UptimeSeconds,
    [property: JsonPropertyName("testMode")] bool TestMode,
    [property: JsonPropertyName("sqliteUserVersion")] int SqliteUserVersion,
    [property: JsonPropertyName("registeredApps")] int RegisteredApps,
    [property: JsonPropertyName("warmApps")] int WarmApps,
    [property: JsonPropertyName("coldApps")] int ColdApps,
    [property: JsonPropertyName("disabledApps")] int DisabledApps
);

public sealed class MotherRuntime : IDisposable
{
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly CancellationTokenSource _bgCts = new();
    private Task? _bgTask;
    private bool _disposed;

    public string Version { get; }
    public PathResolver Paths { get; }
    public SettingsStore Settings { get; }
    public MotherDb Db { get; }
    public MotherDiagnostics Diagnostics { get; }
    public SupervisorIpc Ipc { get; }
    public ManifestRegistryService Manifests { get; }
    public ProcessSupervisor Supervisor { get; }
    public ColdWakeCoordinator ColdWake { get; }
    public HealthPollerService Poller { get; }
    public WatchdogSchedulerService Watchdog { get; }
    public ResourceTrafficCopService TrafficCop { get; }
    public FleetCoexistenceArbitrator Coexistence { get; }
    public SecretVaultInspector Vaults { get; }
    public PowerSleepInhibitor SleepInhibitor { get; }
    public FleetPowerCoordinator Power { get; }
    public NotificationDispatcher Notifications { get; }
    public FleetDoctorService Doctor { get; }

    public event Action<CoexistenceStatusDto>? ConflictsUpdated;

    public MotherRuntime(IReadOnlyList<string>? args = null, string? baseDirectoryOverride = null, bool skipIpcMutex = false)
    {
        Paths = new PathResolver(args, baseDirectoryOverride);
        Version = PathResolver.ReadAppVersion(Paths.BaseDirectory);
        Settings = new SettingsStore(Paths);
        Diagnostics = new MotherDiagnostics(Paths, Version);
        Diagnostics.RegisterGlobalCrashHandlers(() => Supervisor?.GetSnapshots());
        Db = new MotherDb(Paths);
        Ipc = skipIpcMutex ? null! : new SupervisorIpc(Paths);
        Manifests = new ManifestRegistryService(Paths, Settings, Diagnostics);
        Supervisor = new ProcessSupervisor(Paths, Settings, Diagnostics);
        Supervisor.SyncManifests(Manifests.GetAll());
        ColdWake = new ColdWakeCoordinator(Supervisor, Diagnostics);
        Poller = new HealthPollerService(Settings, Supervisor, Db, Diagnostics);
        Watchdog = new WatchdogSchedulerService(Paths, Diagnostics);
        TrafficCop = new ResourceTrafficCopService(Paths, Db, Diagnostics);
        Coexistence = new FleetCoexistenceArbitrator(Paths, Settings, Diagnostics);
        Vaults = new SecretVaultInspector(Paths);
        SleepInhibitor = new PowerSleepInhibitor(Paths, Diagnostics);
        Power = new FleetPowerCoordinator(Paths, Settings, SleepInhibitor, Diagnostics);
        Notifications = new NotificationDispatcher(Paths, Db, Diagnostics);
        Doctor = new FleetDoctorService(Paths, Db, Diagnostics, Version);
    }

    public int ResolveListenPort() =>
        Paths.Options.PortOverride ?? (Paths.Options.TestMode ? Settings.GetCurrent().TestModePort : Settings.GetCurrent().Port);

    public SuiteHealthDto GetSuiteHealth()
    {
        var apps = Manifests.GetAll();
        return new SuiteHealthDto(
            Status: "ok",
            SuiteId: "mother",
            Version: Version,
            SdkVersion: "0.3.0",
            Pid: Environment.ProcessId,
            UptimeSeconds: Math.Max(0, (long)(DateTime.UtcNow - _startedUtc).TotalSeconds),
            TestMode: Paths.Options.TestMode,
            SqliteUserVersion: Db.GetUserVersion(),
            RegisteredApps: apps.Count,
            WarmApps: apps.Count(a => a.ResidencyMode == "warm"),
            ColdApps: apps.Count(a => a.ResidencyMode == "cold"),
            DisabledApps: apps.Count(a => a.ResidencyMode == "disabled"));
    }

    public void StartBackgroundSupervision()
    {
        if (_bgTask is not null) return;
        Ipc?.StartCommandListener();
        _bgTask = Task.Run(() => BackgroundLoopAsync(_bgCts.Token), _bgCts.Token);
    }

    public DoctorReportDto RunDoctor()
    {
        var manifests = Manifests.GetAll();
        var locks = TrafficCop.GetCurrentLocks(manifests);
        return Doctor.RunChecks(manifests, locks, Watchdog.IsRegistered());
    }

    private async Task BackgroundLoopAsync(CancellationToken ct)
    {
        using var hb = new CancellationTokenSource();
        var heartbeat = Diagnostics.CreateHeartbeat("FleetSupervision");
        long tick = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                tick++;
                var manifests = Manifests.GetAll();
                await Poller.PollOnceAsync(manifests, Paths.Options.TestMode, ct).ConfigureAwait(false);
                var apps = Supervisor.GetSnapshots();
                var locks = TrafficCop.GetCurrentLocks(manifests);
                int liveLocks = locks.Count(l => l.Status == "live");
                Power.EvaluateTick(apps, liveLocks, Watchdog.IsRegistered());
                ConflictsUpdated?.Invoke(Coexistence.Evaluate(apps, locks));
                heartbeat.ReportProgress(tick, Math.Max(1, manifests.Count), $"{apps.Count(a => a.IsOnline)} online");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("WARN", "mother", "SupervisionLoop", $"Background tick error: {ex.Message}");
            }

            try
            {
                await Task.Delay(Poller.GetPollIntervalMs(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bgCts.Cancel();
        try { _bgTask?.Wait(1000); } catch { }
        _bgCts.Dispose();
        try { Supervisor.StopAllAsync().Wait(1500); } catch { }
        Supervisor.Dispose();
        SleepInhibitor.Dispose();
        Ipc?.Dispose();
    }
}
