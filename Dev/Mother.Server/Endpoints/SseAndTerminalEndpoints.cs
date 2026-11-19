// @module: Mother.Server.Endpoints.SseAndTerminalEndpoints
// @purpose: Streams suite-wide SSE events (/api/v1/suite/events) and serves docked Foundation Terminal history, stream, clear, and whitelisted CLI commands.
// @depends-on: System, System.Collections.Generic, System.Linq, System.Text.Json, System.Threading, System.Threading.Channels, System.Threading.Tasks, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Mother.Core, Mother.Core.Contracts, Mother.Core.Persistence
// @exports: SseAndTerminalEndpoints

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Mother.Core;
using Mother.Core.Contracts;
using Mother.Core.Persistence;

namespace Mother.Server.Endpoints;

public static class SseAndTerminalEndpoints
{
    public sealed record TerminalExecRequest(string? Command);

    private sealed record SseEventEnvelope(string EventType, object Payload);

    public static void MapSseAndTerminalEndpoints(this WebApplication app, MotherRuntime runtime)
    {
        app.MapGet("/api/v1/suite/events", async (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var channel = Channel.CreateBounded<SseEventEnvelope>(new BoundedChannelOptions(128)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

            void OnAppChanged(SisterAppRuntimeDto dto) => channel.Writer.TryWrite(new("app_status", dto));
            void OnLocksChanged(IReadOnlyList<HkLockSnapshotDto> locks) => channel.Writer.TryWrite(new("lock_changed", new { locks }));
            void OnPowerTick(PowerStatusDto power) => channel.Writer.TryWrite(new("power_countdown", power));
            void OnConflictUpdate(CoexistenceStatusDto coex) => channel.Writer.TryWrite(new("conflict_changed", coex));
            void OnLogLine(TerminalLogEntryDto log) => channel.Writer.TryWrite(new("log_line", log));
            void OnAlert(Mother.Core.Persistence.NotificationHistoryRecord rec) => channel.Writer.TryWrite(new("alert_raised", rec));

            runtime.Supervisor.AppStateChanged += OnAppChanged;
            runtime.TrafficCop.LocksChanged += OnLocksChanged;
            runtime.Power.CountdownTicked += OnPowerTick;
            runtime.ConflictsUpdated += OnConflictUpdate;
            runtime.Diagnostics.LogLineEmitted += OnLogLine;
            runtime.Notifications.AlertRaised += OnAlert;

            try
            {
                await WriteSseAsync(ctx, "heartbeat", new
                {
                    tsUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    isolatedMode = runtime.Paths.IsIsolatedOverride,
                    version = runtime.Version
                }, ctx.RequestAborted);

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
                var readTask = channel.Reader.WaitToReadAsync(ctx.RequestAborted).AsTask();
                var tickTask = timer.WaitForNextTickAsync(ctx.RequestAborted).AsTask();

                while (!ctx.RequestAborted.IsCancellationRequested)
                {
                    var completed = await Task.WhenAny(readTask, tickTask);
                    if (completed == tickTask)
                    {
                        await tickTask;
                        await WriteSseAsync(ctx, "heartbeat", new
                        {
                            tsUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                            uptimeSeconds = runtime.GetSuiteHealth().UptimeSeconds
                        }, ctx.RequestAborted);
                        tickTask = timer.WaitForNextTickAsync(ctx.RequestAborted).AsTask();
                    }
                    else
                    {
                        if (await readTask)
                        {
                            while (channel.Reader.TryRead(out var env))
                            {
                                await WriteSseAsync(ctx, env.EventType, env.Payload, ctx.RequestAborted);
                            }
                            readTask = channel.Reader.WaitToReadAsync(ctx.RequestAborted).AsTask();
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                /* normal SSE client disconnect */
            }
            finally
            {
                runtime.Supervisor.AppStateChanged -= OnAppChanged;
                runtime.TrafficCop.LocksChanged -= OnLocksChanged;
                runtime.Power.CountdownTicked -= OnPowerTick;
                runtime.ConflictsUpdated -= OnConflictUpdate;
                runtime.Diagnostics.LogLineEmitted -= OnLogLine;
                runtime.Notifications.AlertRaised -= OnAlert;
                channel.Writer.TryComplete();
            }
        });

        app.MapGet("/api/v1/terminal/history", (int? limit) =>
            Results.Ok(new { lines = runtime.Diagnostics.GetRecentLines(limit ?? 250) }));

        app.MapPost("/api/v1/terminal/clear", () =>
        {
            runtime.Diagnostics.ClearBuffer();
            return Results.Ok(new { cleared = true });
        });

        app.MapGet("/api/v1/terminal/stream", async (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            var channel = Channel.CreateBounded<TerminalLogEntryDto>(128);
            void OnLog(TerminalLogEntryDto entry) => channel.Writer.TryWrite(entry);
            runtime.Diagnostics.LogLineEmitted += OnLog;
            try
            {
                foreach (var item in runtime.Diagnostics.GetRecentLines(50))
                {
                    await WriteSseAsync(ctx, "log_line", item, ctx.RequestAborted);
                }
                await foreach (var entry in channel.Reader.ReadAllAsync(ctx.RequestAborted))
                {
                    await WriteSseAsync(ctx, "log_line", entry, ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                /* normal terminal SSE client disconnect */
            }
            finally
            {
                runtime.Diagnostics.LogLineEmitted -= OnLog;
                channel.Writer.TryComplete();
            }
        });

        app.MapPost("/api/v1/terminal/exec", async (TerminalExecRequest body, HttpContext ctx) =>
        {
            string raw = (body.Command ?? string.Empty).Trim();
            TerminalDispatchResult result = await TerminalCommandDispatcher.ExecuteAsync(runtime, raw, ctx.RequestAborted);
            runtime.Diagnostics.Log("INFO", "mother", "Terminal", $"> {raw}\n{result.Output}");
            return Results.Ok(new { command = raw, accepted = result.Accepted, output = result.Output });
        });
    }

    private static async Task WriteSseAsync(HttpContext ctx, string eventName, object payload, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(payload);
        await ctx.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}
