// @module: Mother.Core.Features.Supervision.WorkerLaunchCommandBuilder
// @purpose: Resolves supervised child executables, WorkerArgs/commands.startDaemon arguments, isolated-root WorkingDirectory, and Mother gateway env vars.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Text, Mother.Core.Configuration, Mother.Core.Contracts
// @exports: WorkerLaunchCommandBuilder

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Mother.Core.Configuration;
using Mother.Core.Contracts;

namespace Mother.Core.Features.Supervision;

public static class WorkerLaunchCommandBuilder
{
    public static ProcessStartInfo? BuildStartInfo(
        SisterAppEntry manifest,
        PathResolver paths,
        int effectivePort,
        int motherPort,
        out string resolvedExecutable,
        out string unavailableReason)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(paths);

        var argList = new List<string>();
        resolvedExecutable = ResolveExecutableAndArgs(manifest, paths, argList);
        if (string.IsNullOrWhiteSpace(resolvedExecutable))
        {
            unavailableReason = "Executable not staged on disk (external or mock probe mode).";
            return null;
        }

        string? rawWorkDir = !string.IsNullOrWhiteSpace(manifest.InstallDir)
            ? manifest.InstallDir
            : Path.GetDirectoryName(resolvedExecutable);

        string workingDir = !string.IsNullOrWhiteSpace(rawWorkDir)
            ? paths.AssertContainedIfIsolated(rawWorkDir)
            : paths.DataDirectory;

        if (!Directory.Exists(workingDir))
        {
            workingDir = Path.GetDirectoryName(resolvedExecutable) ?? paths.DataDirectory;
            if (paths.IsIsolatedOverride)
            {
                workingDir = paths.AssertContainedIfIsolated(workingDir);
            }
        }

        string fileName = resolvedExecutable;
        var finalArgs = new List<string>();
        if (resolvedExecutable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
            resolvedExecutable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "cmd.exe";
            finalArgs.Add("/c");
            finalArgs.Add(resolvedExecutable);
        }

        finalArgs.AddRange(argList);

        AppendFlagIfMissing(finalArgs, "--managed-by-mother");
        AppendFlagWithValueIfMissing(finalArgs, "--bind", "127.0.0.1");
        AppendFlagWithValueIfMissing(finalArgs, "--port", effectivePort.ToString());

        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string a in finalArgs)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["MANAGED_BY_SUITE"] = "1";
        psi.Environment["MOTHER_SUPERVISED"] = "1";
        psi.Environment["MOTHER_GATEWAY_URL"] = $"http://127.0.0.1:{motherPort}";

        unavailableReason = string.Empty;
        return psi;
    }

    private static string ResolveExecutableAndArgs(
        SisterAppEntry manifest,
        PathResolver paths,
        List<string> args)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Executable))
        {
            string fullExe = CombineAndAssertIfRootedOrRelative(manifest.Executable, manifest.InstallDir, paths);
            if (!File.Exists(fullExe))
            {
                return string.Empty;
            }

            if (manifest.WorkerArgs.Count > 0)
            {
                args.AddRange(manifest.WorkerArgs);
            }
            else
            {
                string? cmd = manifest.Commands?.StartDaemon ?? manifest.Commands?.Start;
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    IReadOnlyList<string> tokens = SplitCommandLineTokens(cmd);
                    if (tokens.Count > 0 && tokens[0].StartsWith("-", StringComparison.Ordinal))
                    {
                        args.AddRange(tokens);
                    }
                    else if (tokens.Count > 1)
                    {
                        args.AddRange(tokens.Skip(1));
                    }
                }
            }
            return fullExe;
        }

        string? startCmd = manifest.Commands?.StartDaemon ?? manifest.Commands?.Start;
        if (string.IsNullOrWhiteSpace(startCmd))
        {
            return string.Empty;
        }

        IReadOnlyList<string> cmdTokens = SplitCommandLineTokens(startCmd);
        if (cmdTokens.Count == 0)
        {
            return string.Empty;
        }

        string head = cmdTokens[0];
        string candidate = CombineAndAssertIfRootedOrRelative(head, manifest.InstallDir, paths);
        string resolvedHead = File.Exists(candidate)
            ? candidate
            : (IsBareCommandToken(head) ? head : string.Empty);

        if (string.IsNullOrWhiteSpace(resolvedHead))
        {
            return string.Empty;
        }

        args.AddRange(cmdTokens.Skip(1));
        if (manifest.WorkerArgs.Count > 0)
        {
            args.AddRange(manifest.WorkerArgs);
        }
        return resolvedHead;
    }

    private static string CombineAndAssertIfRootedOrRelative(string target, string installDir, PathResolver paths)
    {
        string trimmed = target.Trim().Trim('"');
        string combined = Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(installDir)
            ? trimmed
            : Path.Combine(installDir, trimmed);
        return paths.IsIsolatedOverride ? paths.AssertContainedIfIsolated(combined) : Path.GetFullPath(combined);
    }

    private static bool IsBareCommandToken(string token) =>
        !token.Contains('\\') && !token.Contains('/') && !Path.IsPathRooted(token);

    public static IReadOnlyList<string> SplitCommandLineTokens(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (char ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }

    private static void AppendFlagIfMissing(List<string> args, string flag)
    {
        if (!args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase)))
        {
            args.Add(flag);
        }
    }

    private static void AppendFlagWithValueIfMissing(List<string> args, string flag, string value)
    {
        bool exists = args.Any(a =>
            a.Equals(flag, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            args.Add(flag);
            args.Add(value);
        }
    }
}
