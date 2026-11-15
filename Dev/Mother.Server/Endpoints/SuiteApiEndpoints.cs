// @module: Mother.Server.Endpoints.SuiteApiEndpoints
// @purpose: Maps canonical /api/v1/suite/* REST endpoints for fleet apps, manifests, health, settings, doctor, power, notifications, and vault metadata.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text.Json, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Mother.Core, Mother.Core.Configuration
// @exports: SuiteApiEndpoints

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Mother.Core;
using Mother.Core.Configuration;

namespace Mother.Server.Endpoints;

public static class SuiteApiEndpoints
{
    public sealed record ResidencyPatchRequest(string? ResidencyMode, string? Residency);
    public sealed record PowerActionRequest(string? Policy, string? Action);
    public sealed record RollbackRequest(string? SnapshotId);
    public sealed record NotificationTestRequest(string? AppId, string? Slug, string? Title, string? Description);

    public static void MapSuiteApiEndpoints(this WebApplication app, MotherRuntime runtime)
    {
        app.MapGet("/api/v1/health", () => Results.Ok(runtime.GetSuiteHealth()));
        app.MapGet("/api/v1/suite/health", () => Results.Ok(runtime.GetSuiteHealth()));

        app.MapGet("/api/v1/manifest", () => Results.Ok(BuildManifestPayload(runtime)));
        app.MapGet("/api/v1/suite/manifest", () => Results.Ok(BuildManifestPayload(runtime)));

        app.MapGet("/api/v1/suite/apps", () => Results.Ok(new
        {
            suiteId = "mother",
            testMode = runtime.Paths.Options.TestMode,
            apps = runtime.Supervisor.GetSnapshots()
        }));

        app.MapPost("/api/v1/suite/apps/reload", () =>
        {
            var reloaded = runtime.Manifests.Reload();
            runtime.Supervisor.SyncManifests(reloaded);
            return Results.Ok(new
            {
                reloadedCount = reloaded.Count,
                apps = runtime.Supervisor.GetSnapshots()
            });
        });

        app.MapPost("/api/v1/suite/apps/{appId}/residency", (string appId, ResidencyPatchRequest body) =>
        {
            string mode = body.ResidencyMode ?? body.Residency ?? string.Empty;
            var updated = runtime.Supervisor.SetResidency(appId, mode);
            if (updated is null)
            {
                return Results.NotFound(new { errorCode = "UNKNOWN_APP", errorMessage = $"Unknown sister app '{appId}'." });
            }
            return Results.Ok(new { app = runtime.Supervisor.GetSnapshot(appId) });
        });

        app.MapPost("/api/v1/suite/apps/{appId}/wake", async (string appId, HttpContext ctx) =>
        {
            var entry = runtime.Manifests.GetById(appId);
            if (entry is null)
            {
                return Results.NotFound(new { errorCode = "UNKNOWN_APP", errorMessage = $"Unknown sister app '{appId}'." });
            }
            var decision = runtime.Coexistence.ArbitrateColdWake(
                entry,
                runtime.Supervisor.GetSnapshots(),
                runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll()));
            if (!decision.Allowed)
            {
                return Results.Conflict(new
                {
                    errorCode = "COEXISTENCE_CONFLICT",
                    errorMessage = decision.Reason,
                    decision
                });
            }
            var outcome = await runtime.ColdWake.WakeAsync(entry, 8000, ctx.RequestAborted);
            return outcome.Woken
                ? Results.Ok(new { outcome, app = runtime.Supervisor.GetSnapshot(appId) })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });

        app.MapPost("/api/v1/suite/apps/{appId}/stop", async (string appId, HttpContext ctx) =>
        {
            if (runtime.Manifests.GetById(appId) is null)
            {
                return Results.NotFound(new { errorCode = "UNKNOWN_APP", errorMessage = $"Unknown sister app '{appId}'." });
            }
            bool stopped = await runtime.Supervisor.StopAppAsync(appId, 5000, ctx.RequestAborted);
            return Results.Ok(new { appId, stopped, app = runtime.Supervisor.GetSnapshot(appId) });
        });

        app.MapPost("/api/v1/suite/jobs/stop-all", async (HttpContext ctx) =>
        {
            var stopped = await runtime.Supervisor.StopAllAsync(ctx.RequestAborted);
            var cancelledPower = runtime.Power.CancelCountdown();
            return Results.Ok(new { stoppedApps = stopped, power = cancelledPower });
        });

        app.MapGet("/api/v1/suite/events/history", async (int? limit, HttpContext ctx) =>
        {
            var events = await runtime.Db.GetHealthEventsAsync(limit ?? 100, ctx.RequestAborted);
            return Results.Ok(new { events });
        });

        app.MapGet("/api/v1/suite/secrets/metadata", () =>
        {
            var keys = runtime.Vaults.InspectAll(runtime.Manifests.GetAll());
            return Results.Ok(new { keys });
        });

        app.MapGet("/api/v1/suite/notifications", async (int? limit, HttpContext ctx) =>
        {
            var notifications = await runtime.Notifications.GetHistoryAsync(limit ?? 100, ctx.RequestAborted);
            return Results.Ok(new { notifications });
        });

        app.MapPost("/api/v1/suite/notifications/test", async (NotificationTestRequest? body, HttpContext ctx) =>
        {
            var rec = await runtime.Notifications.DispatchAsync(
                body?.AppId ?? "mother",
                body?.Slug ?? "doctor-test-alert",
                body?.Title ?? "Suite Test Notification",
                body?.Description ?? "Dispatched via Mother Suite diagnostic endpoint.",
                new { testMode = runtime.Paths.Options.TestMode },
                ctx.RequestAborted);
            return Results.Ok(new { notification = rec });
        });

        MapDoctorPowerAndSettings(app, runtime);
    }

    private static void MapDoctorPowerAndSettings(WebApplication app, MotherRuntime runtime)
    {
        app.MapGet("/api/v1/suite/doctor", () => Results.Ok(runtime.RunDoctor()));
        app.MapPost("/api/v1/suite/doctor/run", () => Results.Ok(runtime.RunDoctor()));

        app.MapPost("/api/v1/suite/doctor/backup-dbs", async (HttpContext ctx) =>
        {
            string backupPath = await runtime.Doctor.BackupDatabaseAsync(ctx.RequestAborted);
            return Results.Ok(new { backupPath, fileName = Path.GetFileName(backupPath) });
        });

        app.MapPost("/api/v1/suite/doctor/export-bundle", async (HttpContext ctx) =>
        {
            var report = runtime.RunDoctor();
            string bundlePath = await runtime.Doctor.ExportDiagnosticBundleAsync(report, ctx.RequestAborted);
            return Results.Ok(new { bundlePath, fileName = Path.GetFileName(bundlePath), report });
        });

        app.MapPost("/api/v1/suite/doctor/repair", async (HttpContext ctx) =>
        {
            bool watchdogOk = runtime.Watchdog.EnsureRegistered(out string watchdogMessage);
            int purged = await runtime.TrafficCop.PurgeStaleLocksAsync(runtime.Manifests.GetAll(), ctx.RequestAborted);
            var report = runtime.RunDoctor();
            return Results.Ok(new { watchdogOk, watchdogMessage, purgedStaleLocks = purged, report });
        });

        app.MapGet("/api/v1/suite/power/status", () =>
            Results.Ok(runtime.Power.GetStatus(runtime.Watchdog.IsRegistered())));

        app.MapPost("/api/v1/suite/power/action", (PowerActionRequest body) =>
        {
            string action = (body.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action == "cancel")
            {
                return Results.Ok(runtime.Power.CancelCountdown());
            }
            if (!string.IsNullOrWhiteSpace(body.Policy))
            {
                return Results.Ok(runtime.Power.SetPolicy(body.Policy));
            }
            if (action == "evaluate")
            {
                var apps = runtime.Supervisor.GetSnapshots();
                int liveLocks = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll()).Count(l => l.Status == "live");
                return Results.Ok(runtime.Power.EvaluateTick(apps, liveLocks, runtime.Watchdog.IsRegistered()));
            }
            return Results.BadRequest(new { errorCode = "INVALID_POWER_ACTION", errorMessage = "Provide policy or action ('cancel'|'evaluate')." });
        });

        app.MapPost("/api/v1/suite/power/cancel-countdown", () => Results.Ok(runtime.Power.CancelCountdown()));

        app.MapGet("/api/v1/suite/settings", () => Results.Ok(new
        {
            settings = runtime.Settings.GetCurrent(),
            warning = runtime.Settings.LastDiagnosticWarning,
            snapshots = runtime.Settings.ListSnapshots()
        }));

        app.MapPut("/api/v1/suite/settings", (MotherSettings updated) =>
        {
            var saved = runtime.Settings.Save(updated);
            return Results.Ok(new
            {
                settings = saved,
                snapshots = runtime.Settings.ListSnapshots()
            });
        });

        app.MapGet("/api/v1/suite/settings/snapshots", () =>
            Results.Ok(new { snapshots = runtime.Settings.ListSnapshots() }));

        app.MapPost("/api/v1/suite/settings/rollback", (RollbackRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.SnapshotId))
            {
                return Results.BadRequest(new { errorCode = "MISSING_SNAPSHOT_ID", errorMessage = "snapshotId is required." });
            }
            try
            {
                var restored = runtime.Settings.Rollback(body.SnapshotId);
                return Results.Ok(new { settings = restored, snapshots = runtime.Settings.ListSnapshots() });
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { errorCode = "SNAPSHOT_NOT_FOUND", errorMessage = ex.Message });
            }
        });

        app.MapGet("/api/v1/suite/settings/export", () =>
            Results.Ok(runtime.Settings.Export(runtime.Version)));
    }

    private static object BuildManifestPayload(MotherRuntime runtime)
    {
        using var suiteDoc = runtime.Manifests.ReadRawSuiteManifest();
        return new
        {
            schemaVersion = 2,
            appId = "mother",
            name = "Mother",
            version = runtime.Version,
            sdkVersion = "0.4.0",
            brandAccent = "#38bdf8",
            port = runtime.ResolveListenPort(),
            testMode = runtime.Paths.Options.TestMode,
            capabilities = new[] { "fleet-supervisor", "cold-wake-proxy", "traffic-cop", "coexistence-arbitrator", "power-coordinator", "doctor" },
            suiteManifestLoaded = suiteDoc is not null,
            registeredAppIds = runtime.Manifests.GetAll().Select(m => m.AppId).ToArray()
        };
    }
}
