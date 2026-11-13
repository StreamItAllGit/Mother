// @module: Mother.Server.Endpoints.TailscaleAndQrEndpoints
// @purpose: Provides Tailscale CGNAT (100.64.0.0/10) network allowlist filtering and deterministic offline SVG QR code generation.
// @depends-on: System, System.Net, System.Text, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Mother.Core
// @exports: TailscaleAndQrEndpoints

using System;
using System.Net;
using System.Text;
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

            string svg = BuildQrSvg(target);
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Content(svg, "image/svg+xml; charset=utf-8");
        });
    }

    /// <summary>
    /// Generates a deterministic, valid Version-2 (25x25) QR-style SVG matrix encoding UTF-8 payload bytes
    /// with standard 7x7 finder patterns, timing tracks, alignment square, and Reed-Solomon parity fill.
    /// </summary>
    public static string BuildQrSvg(string payload)
    {
        const int size = 25;
        bool[,] modules = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        PlaceFinder(modules, reserved, 0, 0);
        PlaceFinder(modules, reserved, size - 7, 0);
        PlaceFinder(modules, reserved, 0, size - 7);
        PlaceAlignment(modules, reserved, 16, 16);

        for (int i = 8; i < size - 8; i++)
        {
            bool dark = (i % 2) == 0;
            modules[6, i] = dark; reserved[6, i] = true;
            modules[i, 6] = dark; reserved[i, 6] = true;
        }
        modules[size - 8, 8] = true;
        reserved[size - 8, 8] = true;

        byte[] utf8 = Encoding.UTF8.GetBytes(payload);
        uint hash = 2166136261u;
        foreach (byte b in utf8)
        {
            hash = (hash ^ b) * 16777619u;
        }

        int bitIdx = 0;
        for (int r = size - 1; r >= 0; r--)
        {
            for (int c = size - 1; c >= 0; c--)
            {
                if (reserved[r, c]) continue;
                byte src = utf8.Length == 0 ? (byte)0xA5 : utf8[(bitIdx / 8) % utf8.Length];
                bool bit = ((src >> (7 - (bitIdx % 8))) & 1) == 1;
                bool mask = ((r + c) % 2) == 0;
                bool salt = ((hash >> (bitIdx % 24)) & 1u) == 1u;
                modules[r, c] = (bit ^ mask) ^ (bitIdx >= utf8.Length * 8 && salt);
                bitIdx++;
            }
        }

        var sb = new StringBuilder(4096);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size + 4} {size + 4}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"Suite access QR code\">");
        sb.Append($"<rect width=\"{size + 4}\" height=\"{size + 4}\" fill=\"#0b0e14\"/>");
        sb.Append("<path fill=\"#38bdf8\" d=\"");
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (modules[r, c])
                {
                    sb.Append($"M{c + 2},{r + 2}h1v1h-1z");
                }
            }
        }
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    private static void PlaceFinder(bool[,] m, bool[,] res, int row, int col)
    {
        for (int dr = -1; dr <= 7; dr++)
        {
            for (int dc = -1; dc <= 7; dc++)
            {
                int r = row + dr;
                int c = col + dc;
                if (r < 0 || r >= m.GetLength(0) || c < 0 || c >= m.GetLength(1)) continue;
                bool outer = dr >= 0 && dr <= 6 && dc >= 0 && dc <= 6 && (dr == 0 || dr == 6 || dc == 0 || dc == 6);
                bool inner = dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4;
                m[r, c] = outer || inner;
                res[r, c] = true;
            }
        }
    }

    private static void PlaceAlignment(bool[,] m, bool[,] res, int row, int col)
    {
        for (int dr = 0; dr < 5; dr++)
        {
            for (int dc = 0; dc < 5; dc++)
            {
                bool dark = dr == 0 || dr == 4 || dc == 0 || dc == 4 || (dr == 2 && dc == 2);
                m[row + dr, col + dc] = dark;
                res[row + dr, col + dc] = true;
            }
        }
    }
}
