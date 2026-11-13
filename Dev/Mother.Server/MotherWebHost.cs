// @module: Mother.Server.MotherWebHost
// @purpose: Configures Kestrel HTTP host, Tailscale CIDR allowlist filter, static wwwroot assets, PWA files, and all Mother endpoints.
// @depends-on: System, System.IO, System.Net, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Hosting, Microsoft.AspNetCore.Http, Microsoft.Extensions.FileProviders, Mother.Core, Mother.Server.Endpoints
// @exports: MotherWebHost

using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Mother.Core;
using Mother.Server.Endpoints;

namespace Mother.Server;

public static class MotherWebHost
{
    public static WebApplication BuildWebApplication(MotherRuntime runtime, string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? Array.Empty<string>(),
            ContentRootPath = runtime.Paths.BaseDirectory
        });

        int port = runtime.ResolveListenPort();
        bool allowTailscale = runtime.Settings.GetCurrent().AllowTailscaleBind;

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (allowTailscale)
            {
                kestrel.ListenAnyIP(port);
            }
            else
            {
                kestrel.Listen(IPAddress.Loopback, port);
            }
        });

        var app = builder.Build();

        if (allowTailscale)
        {
            app.Use(async (ctx, next) =>
            {
                if (!TailscaleAndQrEndpoints.IsLoopbackOrTailscale(ctx.Connection.RemoteIpAddress))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new
                    {
                        errorCode = "FORBIDDEN_SOURCE_IP",
                        errorMessage = "Mother accepts connections only from localhost or Tailscale (100.64.0.0/10)."
                    });
                    return;
                }
                await next();
            });
        }

        string? wwwroot = ResolveWwwRoot(runtime);
        if (wwwroot is not null)
        {
            var provider = new PhysicalFileProvider(wwwroot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
        }

        app.MapGet("/favicon.ico", () => Results.NoContent());

        app.MapGet("/manifest.webmanifest", () =>
        {
            if (wwwroot is not null)
            {
                string file = Path.Combine(wwwroot, "manifest.webmanifest");
                if (File.Exists(file))
                {
                    return Results.Content(File.ReadAllText(file), "application/manifest+json; charset=utf-8");
                }
            }
            return Results.NotFound();
        });

        app.MapGet("/sw.js", () =>
        {
            if (wwwroot is not null)
            {
                string file = Path.Combine(wwwroot, "sw.js");
                if (File.Exists(file))
                {
                    return Results.Content(File.ReadAllText(file), "application/javascript; charset=utf-8");
                }
            }
            return Results.NotFound();
        });

        app.MapSuiteApiEndpoints(runtime);
        app.MapTrafficCopAndCoexistenceEndpoints(runtime);
        app.MapSseAndTerminalEndpoints(runtime);
        app.MapTailscaleAndQrEndpoints(runtime);
        app.MapColdWakeProxyEndpoints(runtime);

        app.MapFallback(async ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/app"))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    errorCode = "NOT_FOUND",
                    errorMessage = $"Endpoint '{ctx.Request.Path}' was not found."
                });
                return;
            }

            if (wwwroot is not null)
            {
                string index = Path.Combine(wwwroot, "index.html");
                if (File.Exists(index))
                {
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.SendFileAsync(index);
                    return;
                }
            }
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        });

        return app;
    }

    private static string? ResolveWwwRoot(MotherRuntime runtime)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(runtime.Paths.BaseDirectory, "wwwroot"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mother.Server", "wwwroot"))
        };

        foreach (string dir in candidates)
        {
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "index.html")))
            {
                return dir;
            }
        }
        return null;
    }
}
