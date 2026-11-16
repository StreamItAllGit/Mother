// @module: Mother.Core.Features.Power.FleetPowerCoordinator
// @purpose: Post-Run Power Coordinator (StayAwake/Sleep/Shutdown) with 60s idle window and 60s cancellable countdown; hard-disabled in isolated/dry-run mode.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Linq, System.Runtime.InteropServices, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics
// @exports: FleetPowerCoordinator

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;

namespace Mother.Core.Features.Power;

public sealed class FleetPowerCoordinator
{
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private readonly object _sync = new();
    private readonly PathResolver _paths;
    private readonly SettingsStore _settings;
    private readonly PowerSleepInhibitor _inhibitor;
    private readonly MotherDiagnostics _diag;

    private string _policy;
    private bool _fleetBusy;
    private int _activeJobs;
    private int _queuedJobs;
    private bool _sawBusySincePolicyArmed;
    private DateTime? _idleEnteredUtc;
    private bool _countdownActive;
    private DateTime? _countdownStartedUtc;
    private int _secondsRemaining;

    public bool IsSimulated => _paths.IsIsolatedOverride || _paths.Options.DryRun;
    public int SimulatedPowerExecutions { get; private set; }
    public event Action<PowerStatusDto>? CountdownTicked;

    public FleetPowerCoordinator(
        PathResolver paths,
        SettingsStore settings,
        PowerSleepInhibitor inhibitor,
        MotherDiagnostics diag)
    {
        _paths = paths;
        _settings = settings;
        _inhibitor = inhibitor;
        _diag = diag;
        _policy = _settings.GetCurrent().PostRunPowerPolicy;
    }

    public PowerStatusDto SetPolicy(string policy)
    {
        string norm = policy.Trim() switch
        {
            var p when p.Equals("Sleep", StringComparison.OrdinalIgnoreCase) => "Sleep",
            var p when p.Equals("Shutdown", StringComparison.OrdinalIgnoreCase) => "Shutdown",
            _ => "StayAwake"
        };

        MotherSettings cfg = _settings.GetCurrent();
        cfg.PostRunPowerPolicy = norm;
        _settings.Save(cfg);

        lock (_sync)
        {
            _policy = norm;
            _sawBusySincePolicyArmed = _fleetBusy;
            if (norm == "StayAwake")
            {
                _countdownActive = false;
                _countdownStartedUtc = null;
                _secondsRemaining = 0;
            }
        }
        _diag.Log("INFO", "mother", "Power", $"Post-Run Power Policy set to '{norm}'.");
        return GetStatus(false);
    }

    public PowerStatusDto CancelCountdown()
    {
        lock (_sync)
        {
            _countdownActive = false;
            _countdownStartedUtc = null;
            _secondsRemaining = 0;
            _idleEnteredUtc = null;
            _policy = "StayAwake";
        }
        MotherSettings cfg = _settings.GetCurrent();
        cfg.PostRunPowerPolicy = "StayAwake";
        _settings.Save(cfg);
        _diag.Log("INFO", "mother", "Power", "Post-run power countdown cancelled; policy reset to StayAwake.");
        var status = GetStatus(false);
        CountdownTicked?.Invoke(status);
        return status;
    }

    public PowerStatusDto EvaluateTick(
        IReadOnlyList<SisterAppRuntimeDto> apps,
        int liveLockCount,
        bool watchdogRegistered,
        DateTime? nowUtcOverride = null)
    {
        DateTime nowUtc = nowUtcOverride ?? DateTime.UtcNow;
        MotherSettings cfg = _settings.GetCurrent();
        _inhibitor.UpdateInhibition(cfg.SleepInhibitDuringActiveLock && liveLockCount > 0);

        int active = apps.Sum(a => a.ActiveJobs);
        int queued = apps.Sum(a => a.QueuedJobs);
        bool busy = active > 0 || queued > 0 || liveLockCount > 0;

        PowerStatusDto snapshot;
        bool shouldExecuteAction = false;
        string actionToRun = "StayAwake";

        lock (_sync)
        {
            _activeJobs = active;
            _queuedJobs = queued;
            _fleetBusy = busy;

            if (busy)
            {
                _sawBusySincePolicyArmed = true;
                _idleEnteredUtc = null;
                if (_countdownActive)
                {
                    _countdownActive = false;
                    _countdownStartedUtc = null;
                    _secondsRemaining = 0;
                }
            }
            else if (_policy != "StayAwake" && _sawBusySincePolicyArmed)
            {
                _idleEnteredUtc ??= nowUtc;
                double idleSec = (nowUtc - _idleEnteredUtc.Value).TotalSeconds;
                if (!_countdownActive && idleSec >= cfg.PostRunIdleSeconds)
                {
                    _countdownActive = true;
                    _countdownStartedUtc = nowUtc;
                    _secondsRemaining = cfg.PostRunCountdownSeconds;
                }
                else if (_countdownActive && _countdownStartedUtc.HasValue)
                {
                    int elapsed = (int)Math.Floor((nowUtc - _countdownStartedUtc.Value).TotalSeconds);
                    _secondsRemaining = Math.Max(0, cfg.PostRunCountdownSeconds - elapsed);
                    if (_secondsRemaining == 0)
                    {
                        _countdownActive = false;
                        _sawBusySincePolicyArmed = false;
                        actionToRun = _policy;
                        shouldExecuteAction = true;
                    }
                }
            }

            snapshot = BuildStatusUnsynchronized(nowUtc, watchdogRegistered);
        }

        if (_countdownActive) CountdownTicked?.Invoke(snapshot);

        if (shouldExecuteAction)
        {
            ExecutePowerTransition(actionToRun);
        }

        return snapshot;
    }

    public PowerStatusDto GetStatus(bool watchdogRegistered)
    {
        lock (_sync)
        {
            return BuildStatusUnsynchronized(DateTime.UtcNow, watchdogRegistered);
        }
    }

    private PowerStatusDto BuildStatusUnsynchronized(DateTime nowUtc, bool watchdogRegistered)
    {
        int idleElapsed = _idleEnteredUtc.HasValue ? (int)Math.Max(0, (nowUtc - _idleEnteredUtc.Value).TotalSeconds) : 0;
        return new PowerStatusDto(
            Policy: _policy,
            FleetBusy: _fleetBusy,
            ActiveJobCount: _activeJobs,
            QueuedJobCount: _queuedJobs,
            SleepInhibited: _inhibitor.IsInhibiting,
            CountdownActive: _countdownActive,
            SecondsRemaining: _secondsRemaining,
            IdleSecondsElapsed: idleElapsed,
            Simulated: IsSimulated,
            WatchdogRegistered: watchdogRegistered);
    }

    private void ExecutePowerTransition(string action)
    {
        if (IsSimulated || !OperatingSystem.IsWindows())
        {
            SimulatedPowerExecutions++;
            _diag.Log("INFO", "mother", "Power", $"[SIMULATED] Executed post-run power transition '{action}' (hard-disabled in isolated/--dry-run mode).");
            return;
        }

        _diag.Log("WARN", "mother", "Power", $"Executing post-run OS power action: {action}");
        if (action == "Sleep")
        {
            SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
        }
        else if (action == "Shutdown")
        {
            var psi = new ProcessStartInfo("shutdown.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/t");
            psi.ArgumentList.Add("5");
            Process.Start(psi);
        }
    }
}
