// @module: Mother.Server.Endpoints.ColdWakeProxyMiddleware
// @purpose: Reverse proxy and Cold-Wake coordinator for /api/v1/apps/{appId}/... and /app/{appId}/... with coexistence arbitration.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net, System.Net.Http, System.Threading.Tasks, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Mother.Core
// @exports: ColdWakeProxyMiddleware

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mother.Core;

namespace Mother.Server.Endpoints;

public static class ColdWakeProxyMiddleware
{
    private static readonly HttpClient SharedProxyClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Public", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade", "Host"
    };

    public static void MapColdWakeProxyEndpoints(this WebApplication app, MotherRuntime runtime)
    {
        app.Map("/api/v1/apps/{appId}/{**catchAll}", (HttpContext ctx, string appId, string? catchAll) =>
            ProxyAsync(ctx, runtime, appId, isApiRoute: true, catchAll));

        app.Map("/api/v1/apps/{appId}", (HttpContext ctx, string appId) =>
            ProxyAsync(ctx, runtime, appId, isApiRoute: true, catchAll: null));

        app.Map("/app/{appId}/{**catchAll}", (HttpContext ctx, string appId, string? catchAll) =>
            ProxyAsync(ctx, runtime, appId, isApiRoute: false, catchAll));

        app.Map("/app/{appId}", async (HttpContext ctx, string appId) =>
        {
            string rawPath = ctx.Request.Path.Value ?? string.Empty;
            if (!rawPath.EndsWith('/'))
            {
                string qs = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value! : string.Empty;
                ctx.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
                ctx.Response.Headers.Location = $"/app/{appId}/{qs}";
                return;
            }
            await ProxyAsync(ctx, runtime, appId, isApiRoute: false, catchAll: null);
        });
    }

    private static async Task ProxyAsync(
        HttpContext ctx,
        MotherRuntime runtime,
        string appId,
        bool isApiRoute,
        string? catchAll)
    {
        var entry = runtime.Manifests.GetById(appId);
        if (entry is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new
            {
                errorCode = "UNKNOWN_APP",
                errorMessage = $"Sister app '{appId}' is not registered in Mother."
            }, ctx.RequestAborted);
            return;
        }

        if (entry.ResidencyMode == "disabled")
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsJsonAsync(new
            {
                errorCode = "APP_DISABLED",
                errorMessage = $"Sister app '{entry.Name}' is currently disabled."
            }, ctx.RequestAborted);
            return;
        }

        var snapshot = runtime.Supervisor.GetSnapshot(appId);
        bool isOnline = snapshot?.IsOnline == true;

        if (!isOnline)
        {
            var decision = runtime.Coexistence.ArbitrateColdWake(
                entry,
                runtime.Supervisor.GetSnapshots(),
                runtime.TrafficCop.GetCurrentLocks(runtime.Manifests.GetAll()));
            if (!decision.Allowed)
            {
                await runtime.TrafficCop.RecordDeferralAsync(
                    entry.AppId,
                    $"cold-wake-{Guid.NewGuid():N}".Substring(0, 18),
                    entry.TargetDrives.FirstOrDefault() ?? "local",
                    decision.BlockingAppId ?? "peer",
                    string.Empty,
                    30,
                    ctx.RequestAborted);
                ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    errorCode = "COEXISTENCE_CONFLICT",
                    errorMessage = decision.Reason,
                    decision
                }, ctx.RequestAborted);
                return;
            }

            bool allowWake = isApiRoute ? entry.WakeOnApiRequest : entry.WakeOnUiNavigation;
            if (allowWake)
            {
                var wake = await runtime.ColdWake.WakeAsync(entry, 8000, ctx.RequestAborted);
                if (!wake.Woken)
                {
                    ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await ctx.Response.WriteAsJsonAsync(new
                    {
                        errorCode = "COLD_WAKE_FAILED",
                        errorMessage = wake.Message,
                        appId = entry.AppId
                    }, ctx.RequestAborted);
                    return;
                }
            }
            else
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    errorCode = "APP_OFFLINE",
                    errorMessage = $"Sister app '{entry.Name}' is offline and auto-wake is disabled for this route."
                }, ctx.RequestAborted);
                return;
            }
        }

        runtime.Supervisor.TouchActivity(entry.AppId);
        int port = runtime.Manifests.GetEffectivePort(entry);
        string subPath = string.IsNullOrEmpty(catchAll) ? string.Empty : catchAll.TrimStart('/');
        string upstreamPath = isApiRoute
            ? (string.IsNullOrEmpty(subPath) ? "/api/v1" : $"/api/v1/{subPath}")
            : (string.IsNullOrEmpty(subPath) ? "/" : $"/{subPath}");
        string targetUri = $"http://127.0.0.1:{port}{upstreamPath}{ctx.Request.QueryString}";

        using var reqMsg = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), targetUri);
        if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
            ms.Position = 0;
            reqMsg.Content = new StreamContent(ms);
        }

        foreach (var header in ctx.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key)) continue;
            if (header.Key.Equals("X-Forwarded-Prefix", StringComparison.OrdinalIgnoreCase)) continue;
            if (!reqMsg.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                reqMsg.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }
        reqMsg.Headers.TryAddWithoutValidation("X-Forwarded-Prefix", $"/app/{entry.AppId}");

        try
        {
            using var respMsg = await SharedProxyClient.SendAsync(
                reqMsg,
                HttpCompletionOption.ResponseHeadersRead,
                ctx.RequestAborted);
            ctx.Response.StatusCode = (int)respMsg.StatusCode;
            foreach (var h in respMsg.Headers)
            {
                if (!HopByHopHeaders.Contains(h.Key))
                    ctx.Response.Headers[h.Key] = h.Value.ToArray();
            }
            foreach (var h in respMsg.Content.Headers)
            {
                if (!HopByHopHeaders.Contains(h.Key))
                    ctx.Response.Headers[h.Key] = h.Value.ToArray();
            }
            if (respMsg.Content.Headers.ContentType?.MediaType?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
            {
                ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            }
            await respMsg.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ctx.RequestAborted.IsCancellationRequested || ctx.Response.HasStarted)
            {
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
            await ctx.Response.WriteAsJsonAsync(new
            {
                errorCode = "UPSTREAM_UNREACHABLE",
                errorMessage = $"Failed to reach {entry.Name} on 127.0.0.1:{port}: {ex.Message}"
            }, ctx.RequestAborted);
        }
    }
}
