// @module: Mother.Server.Endpoints.TailscaleAndQrEndpoints
// @purpose: Provides Tailscale CGNAT (100.64.0.0/10) network allowlist filtering and ISO/IEC 18004 SVG QR code generation via Foundation.Core.QrCodeEncoder.
// @depends-on: System, System.Net, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Foundation.Core, Mother.Core
// @exports: TailscaleAndQrEndpoints

using System;
using System.Net;
using Foundation.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Mother.Core;

namespace Mother.Server.Endpoints;

public static class TailscaleAndQrEndpoints
{
    public static bool IsLoopbackOrTailscale(IPAddress? address)
    {
        if (address is null) return true;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] bytes = address.GetAddressBytes();
        // Tailscale CGNAT range: 100.64.0.0/10 -> byte[0] == 100, (byte[1] & 0xC0) == 64 (64..127)
        return bytes.Length == 4 && bytes[0] == 100 && (bytes[1] & 0xC0) == 64;
    }

    public static void MapTailscaleAndQrEndpoints(this WebApplication app, MotherRuntime runtime)
    {
        app.MapGet("/api/v1/suite/qr.svg", (HttpContext ctx, string? url) =>
        {
            string target = string.IsNullOrWhiteSpace(url)
                ? $"http://127.0.0.1:{runtime.ResolveListenPort()}/"
                : url.Trim();

            try
            {
                string svg = BuildQrSvg(target);
                ctx.Response.Headers.CacheControl = "no-store";
                return Results.Content(svg, "image/svg+xml; charset=utf-8");
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new
                {
                    errorCode = "QR_PAYLOAD_INVALID",
                    errorMessage = ex.Message
                });
            }
        });
    }

    /// <summary>
    /// Generates an ISO/IEC 18004 (Versions 1-10, ECC Level M) SVG QR code with a 4-module quiet zone (#0b0e14 on #ffffff).
    /// </summary>
    public static string BuildQrSvg(string payload) =>
        QrCodeEncoder.RenderSvg(
            payload,
            darkColor: "#0b0e14",
            lightColor: "#ffffff",
            quietZoneModules: 4,
            ariaLabel: "Suite access QR code");
}
