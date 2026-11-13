// @module: Mother.Server.Endpoints.TrafficCopAndCoexistenceEndpoints
// @purpose: Exposes Resource Traffic Cop (.hklock/deferrals) and Fleet Coexistence Arbitrator REST endpoints.
// @depends-on: System, System.Collections.Generic, System.Linq, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Mother.Core
// @exports: TrafficCopAndCoexistenceEndpoints

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Mother.Core;

namespace Mother.Server.Endpoints;

public static class TrafficCopAndCoexistenceEndpoints
{
    public sealed record RecordDeferralRequest(string AppId, string JobId, string DriveMount, string? BlockingAppId, string? LockFilePath, int Minutes = 30);
    public sealed record ApplyRecommendationsRequest(IReadOnlyList<string>? TargetAppIds, string? PresetId, IReadOnlyList<string>? SharedRoots);
    public sealed record ArbitrateRequest(string? TargetAppId);

    public static void MapTrafficCopAndCoexistenceEndpoints(this WebApplication app, MotherRuntime runtime)
    {
        app.MapGet("/api/v1/suite/scheduler/traffic-cop", async (HttpContext ctx) =>
        {
            var status = await runtime.TrafficCop.GetStatusAsync(runtime.Manifests.GetAll(), ctx.RequestAborted);
            return Results.Ok(status);
        });

        app.MapPost("/api/v1/suite/scheduler/traffic-cop", async (RecordDeferralRequest body, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.AppId) || string.IsNullOrWhiteSpace(body.JobId))
            {
                return Results.BadRequest(new { errorCode = "INVALID_DEFERRAL", errorMessage = "appId and jobId are required." });
            }
            var rec = await runtime.TrafficCop.RecordDeferralAsync(
                body.AppId,
                body.JobId,
                string.IsNullOrWhiteSpace(body.DriveMount) ? "local" : body.DriveMount,
                body.BlockingAppId ?? "unknown",
                body.LockFilePath ?? string.Empty,
                body.Minutes > 0 ? body.Minutes : 30,
                ctx.RequestAborted);
            return Results.Ok(new { deferral = rec });
        });

        app.MapGet("/api/v1/suite/traffic-cop/locks", () =>
            Results.Ok(new { locks = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll()) }));

        app.MapPost("/api/v1/suite/traffic-cop/purge-stale", async (HttpContext ctx) =>
        {
            int purged = await runtime.TrafficCop.PurgeStaleLocksAsync(runtime.Manifests.GetAll(), ctx.RequestAborted);
            return Results.Ok(new
            {
                purgedCount = purged,
                locks = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll())
            });
        });

        app.MapGet("/api/v1/suite/coexistence/status", () => Results.Ok(EvaluateCoexistence(runtime)));
        app.MapGet("/api/v1/coexistence/status", () => Results.Ok(EvaluateCoexistence(runtime)));

        app.MapGet("/api/v1/suite/coexistence/conflicts", () =>
        {
            var status = EvaluateCoexistence(runtime);
            return Results.Ok(new { conflicts = status.Conflicts, sharedRoots = status.SharedRoots });
        });

        app.MapGet("/api/v1/suite/coexistence/deferrals", async (int? limit, HttpContext ctx) =>
        {
            var deferrals = await runtime.Db.GetDeferralsAsync(limit ?? 100, ctx.RequestAborted);
            return Results.Ok(new { deferrals });
        });

        app.MapPost("/api/v1/suite/coexistence/apply-recommendations", (ApplyRecommendationsRequest body) =>
        {
            var status = EvaluateCoexistence(runtime);
            var roots = body.SharedRoots is { Count: > 0 } ? body.SharedRoots : status.SharedRoots;
            string preset = string.IsNullOrWhiteSpace(body.PresetId) ? "suite-safe-stagger" : body.PresetId!;
            var updated = runtime.Coexistence.ApplyRecommendations(body.TargetAppIds, preset, roots);
            return Results.Ok(new
            {
                appliedPreset = preset,
                updatedApps = updated,
                status = EvaluateCoexistence(runtime)
            });
        });

        app.MapPost("/api/v1/suite/coexistence/arbitrate", (ArbitrateRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.TargetAppId))
            {
                return Results.BadRequest(new { errorCode = "MISSING_APP_ID", errorMessage = "targetAppId is required." });
            }
            var entry = runtime.Manifests.GetById(body.TargetAppId);
            if (entry is null)
            {
                return Results.NotFound(new { errorCode = "UNKNOWN_APP", errorMessage = $"Unknown app '{body.TargetAppId}'." });
            }
            var decision = runtime.Coexistence.ArbitrateColdWake(
                entry,
                runtime.Supervisor.GetSnapshots(),
                runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll()));
            return Results.Ok(decision);
        });
    }

    private static Mother.Core.Contracts.CoexistenceStatusDto EvaluateCoexistence(MotherRuntime runtime)
    {
        var apps = runtime.Supervisor.GetSnapshots();
        var locks = runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll());
        return runtime.Coexistence.Evaluate(apps, locks);
    }
}
