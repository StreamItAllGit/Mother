// @module: Mother.Launcher.ArgsHelper
// @purpose: Extends RootLauncherHost CLI normalization and sync-wait detection with Mother-specific options (--programs-dir, --suite-tab, --window, --browser, --diagnose, --automation-*).
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Launcher
// @exports: MotherLauncherArgsHelper

using System;
using System.Collections.Generic;
using System.IO;
using Foundation.Launcher;

namespace Mother.Launcher;

/// <summary>
/// Wraps <see cref="RootLauncherHost"/> CLI helpers for Mother-specific path options and sync/async dispatch rules.
/// </summary>
public static class MotherLauncherArgsHelper
{
    private static readonly HashSet<string> MotherAsyncValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--suite-tab",
        "--window",
        "--browser",
        "--programs-dir"
    };

    private static readonly HashSet<string> MotherSyncFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--diagnose",
        "--automation-port",
        "--automation-token"
    };

    private static readonly HashSet<string> MotherNonPathValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--suite-tab",
        "--window",
        "--browser",
        "--automation-port",
        "--automation-token"
    };

    public static string[] NormalizeArgs(string[]? args, string callerCwd)
    {
        if (args is null || args.Length == 0)
        {
            return Array.Empty<string>();
        }

        string effectiveCwd = string.IsNullOrWhiteSpace(callerCwd)
            ? Environment.CurrentDirectory
            : callerCwd;

        var normalized = new List<string>(args.Length);
        var passthroughSegment = new List<string>();

        void FlushSegment()
        {
            if (passthroughSegment.Count == 0) return;
            normalized.AddRange(RootLauncherHost.NormalizeRelativePathArgs(passthroughSegment.ToArray(), effectiveCwd));
            passthroughSegment.Clear();
        }

        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i] ?? string.Empty;
            int eq = raw.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && raw.StartsWith("-", StringComparison.Ordinal))
            {
                string opt = raw[..eq];
                string val = raw[(eq + 1)..];
                if (opt.Equals("--programs-dir", StringComparison.OrdinalIgnoreCase))
                {
                    FlushSegment();
                    normalized.Add($"{opt}={RebasePathIfRelative(val, effectiveCwd)}");
                    continue;
                }
                if (MotherNonPathValueOptions.Contains(opt))
                {
                    FlushSegment();
                    normalized.Add(raw);
                    continue;
                }
            }
            else if (raw.Equals("--programs-dir", StringComparison.OrdinalIgnoreCase))
            {
                FlushSegment();
                normalized.Add(raw);
                if (i + 1 < args.Length && !IsFlagToken(args[i + 1]))
                {
                    i++;
                    normalized.Add(RebasePathIfRelative(args[i], effectiveCwd));
                }
                continue;
            }
            else if (MotherNonPathValueOptions.Contains(raw))
            {
                FlushSegment();
                normalized.Add(raw);
                if (i + 1 < args.Length && !IsFlagToken(args[i + 1]))
                {
                    i++;
                    normalized.Add(args[i]);
                }
                continue;
            }

            passthroughSegment.Add(raw);
        }

        FlushSegment();
        return normalized.ToArray();
    }

    public static bool ShouldWaitForChild(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        var remaining = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            string trimmed = (args[i] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            int eq = trimmed.IndexOf('=', StringComparison.Ordinal);
            string flagName = eq > 0 && trimmed.StartsWith("-", StringComparison.Ordinal)
                ? trimmed[..eq]
                : trimmed;

            if (MotherSyncFlags.Contains(flagName))
            {
                return true;
            }

            if (MotherAsyncValueOptions.Contains(flagName))
            {
                if (eq < 0 && i + 1 < args.Length && !IsFlagToken(args[i + 1]))
                {
                    i++;
                }
                continue;
            }

            remaining.Add(args[i]);
        }

        return RootLauncherHost.ShouldWaitForChild(remaining.ToArray());
    }

    private static bool IsFlagToken(string? token) =>
        !string.IsNullOrWhiteSpace(token) && token.TrimStart().StartsWith("-", StringComparison.Ordinal);

    private static string RebasePathIfRelative(string value, string callerCwd)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            return value;
        }
        if (Path.IsPathRooted(value))
        {
            return value;
        }
        return Path.GetFullPath(value, callerCwd);
    }
}
