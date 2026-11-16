// @module: Mother.Server.Endpoints.TerminalCommandDispatcher
// @purpose: Dispatches whitelisted suite terminal commands (inspection, lock purge, manifest reload, wake/stop, and validated power control).
// @depends-on: System, System.Linq, System.Text.Json, System.Threading, System.Threading.Tasks, Mother.Core
// @exports: TerminalDispatchResult, TerminalCommandDispatcher

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core;

namespace Mother.Server.Endpoints;

public sealed record TerminalDispatchResult(bool Accepted, string Output);

public static class TerminalCommandDispatcher
{
    public static async Task<TerminalDispatchResult> ExecuteAsync(
        MotherRuntime runtime,
        string? rawCommand,
        CancellationToken ct)
    {
        string raw = (rawCommand ?? string.Empty).Trim();
        string[] tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return new TerminalDispatchResult(false, "Empty command. Type 'help' for allowed suite commands.");
        }

        string cmd = tokens[0].ToLowerInvariant();
        string? arg1 = tokens.Length > 1 ? tokens[1] : null;

        switch (cmd)
        {
            case "help":
                return new TerminalDispatchResult(true,
                    "Allowed commands: help, status, health, apps, traffic, locks, coexistence, doctor, logs [count], purge-stale, reload, wake <appId>, stop <appId>, power [StayAwake|Sleep|Shutdown]");

            case "status":
            {
                var health = runtime.GetSuiteHealth();
                var apps = runtime.Supervisor.GetSnapshots();
                var locks = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll());
                var pwr = runtime.Power.GetStatus(runtime.Watchdog.IsRegistered());
                return new TerminalDispatchResult(true,
                    $"Mother v{health.Version} (sdk {health.SdkVersion}, isolatedMode={health.IsolatedMode}): {apps.Count(a => a.IsOnline)}/{apps.Count} online, Power={pwr.Policy}, Locks={locks.Count(l => l.Status == "live")} live/{locks.Count(l => l.Status == "stale")} stale.");
            }

            case "health":
                return new TerminalDispatchResult(true, JsonSerializer.Serialize(runtime.GetSuiteHealth()));

            case "apps":
                return new TerminalDispatchResult(true,
                    string.Join(", ", runtime.Supervisor.GetSnapshots().Select(a => $"{a.AppId}:{a.State}({a.Residency})")));

            case "traffic":
            {
                var status = await runtime.TrafficCop.GetStatusAsync(runtime.Manifests.GetAll(), ct).ConfigureAwait(false);
                return new TerminalDispatchResult(true,
                    $"TrafficCop: liveLocks={status.ActiveLockCount}, staleLocks={status.StaleLockCount}, recentDeferrals={status.RecentDeferrals.Count}");
            }

            case "locks":
            {
                var locks = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll());
                return new TerminalDispatchResult(true,
                    $"Locks={locks.Count} ({locks.Count(l => l.Status == "live")} live, {locks.Count(l => l.Status == "stale")} stale)");
            }

            case "coexistence":
            {
                var coex = runtime.Coexistence.Evaluate(
                    runtime.Supervisor.GetSnapshots(),
                    runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll()));
                return new TerminalDispatchResult(true,
                    $"Conflicts={coex.Conflicts.Count}, SharedRoots={coex.SharedRoots.Count}");
            }

            case "doctor":
            {
                var doc = runtime.RunDoctor();
                return new TerminalDispatchResult(true,
                    $"Doctor={doc.Overall}, checks={doc.SuiteChecks.Count}, crashes={doc.CrashBundleCount}");
            }

            case "logs":
            {
                int count = 20;
                if (!string.IsNullOrWhiteSpace(arg1) && (!int.TryParse(arg1, out count) || count <= 0))
                {
                    return new TerminalDispatchResult(false, $"Invalid log line count '{arg1}'. Usage: logs [1..100]");
                }
                count = Math.Clamp(count, 1, 100);
                var lines = runtime.Diagnostics.GetRecentLines(count);
                if (lines.Count == 0)
                {
                    return new TerminalDispatchResult(true, "(no log entries yet)");
                }
                string formatted = string.Join("\n", lines.Select(l => $"[{l.TimestampUtc}] [{l.Level}] [{l.AppId}/{l.Slice}] {l.Message}"));
                return new TerminalDispatchResult(true, formatted);
            }

            case "purge-stale":
            {
                int purged = await runtime.TrafficCop.PurgeStaleLocksAsync(runtime.Manifests.GetAll(), ct).ConfigureAwait(false);
                return new TerminalDispatchResult(true, $"Purged {purged} stale .hklock files.");
            }

            case "reload":
            {
                var apps = runtime.Manifests.Reload();
                runtime.Supervisor.SyncManifests(apps);
                return new TerminalDispatchResult(true, $"Reloaded {apps.Count} sister manifest(s).");
            }

            case "wake":
            {
                if (string.IsNullOrWhiteSpace(arg1))
                {
                    return new TerminalDispatchResult(false, "Usage: wake <appId>");
                }
                var entry = runtime.Manifests.GetById(arg1);
                if (entry is null)
                {
                    return new TerminalDispatchResult(false, $"Unknown app '{arg1}'.");
                }
                var decision = runtime.Coexistence.ArbitrateColdWake(
                    entry,
                    runtime.Supervisor.GetSnapshots(),
                    runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll()));
                if (!decision.Allowed)
                {
                    return new TerminalDispatchResult(false, $"Wake blocked for '{entry.AppId}': {decision.Reason}");
                }
                var wake = await runtime.ColdWake.WakeAsync(entry, 8000, ct).ConfigureAwait(false);
                return new TerminalDispatchResult(wake.Woken, wake.Message);
            }

            case "stop":
            {
                if (string.IsNullOrWhiteSpace(arg1))
                {
                    return new TerminalDispatchResult(false, "Usage: stop <appId>");
                }
                var entry = runtime.Manifests.GetById(arg1);
                if (entry is null)
                {
                    return new TerminalDispatchResult(false, $"Unknown app '{arg1}'.");
                }
                bool stopped = await runtime.Supervisor.StopAppAsync(entry.AppId, ct: ct).ConfigureAwait(false);
                return new TerminalDispatchResult(stopped, stopped ? $"Stopped {entry.Name}." : $"Failed to stop {entry.Name}.");
            }

            case "power":
            {
                if (string.IsNullOrWhiteSpace(arg1))
                {
                    var pwr = runtime.Power.GetStatus(runtime.Watchdog.IsRegistered());
                    return new TerminalDispatchResult(true,
                        $"Policy={pwr.Policy}, Busy={pwr.FleetBusy}, Countdown={(pwr.CountdownActive ? pwr.SecondsRemaining + "s" : "off")}");
                }

                string? canonical = arg1.Trim().ToLowerInvariant() switch
                {
                    "stayawake" => "StayAwake",
                    "sleep" => "Sleep",
                    "shutdown" => "Shutdown",
                    _ => null
                };
                if (canonical is null)
                {
                    return new TerminalDispatchResult(false,
                        $"Invalid power policy '{arg1}'. Expected one of: StayAwake, Sleep, Shutdown.");
                }

                var updated = runtime.Power.SetPolicy(canonical);
                return new TerminalDispatchResult(true, $"Policy={updated.Policy}, Simulated={updated.Simulated}");
            }

            default:
                return new TerminalDispatchResult(false, $"Unknown command '{cmd}'. Type 'help' for allowed suite commands.");
        }
    }
}
