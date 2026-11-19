// @module: Mother.Installer.MotherInstallerIntegration
// @purpose: Routes Mother HKCU/sandbox suite registry metadata, automation flag parsing, legacy migration, and sandbox-safe cleanup strictly through IPlatformRegistry and KnownFolderPaths.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Globalization, System.IO, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Installer
// @exports: MotherInstallerIntegration

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;

namespace Mother.Installer;

internal static class MotherInstallerIntegration
{
    internal const string MotherRegistrySubKey = @"Software\StreamItAll\Mother";

    private static readonly JsonSerializerOptions ManifestJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static (bool WriteShortcuts, bool WriteAutoStart, bool WriteRegistry) PlanLivePostInstallActions(
        bool suppressHostMutations,
        bool dryRun,
        bool isWindows,
        bool createShortcuts,
        bool autoStart)
    {
        if (suppressHostMutations || dryRun || !isWindows)
        {
            return (false, false, false);
        }
        return (createShortcuts, autoStart, true);
    }

    internal static bool TryHandleAutomationArgs(
        string[] args,
        out string[] hostArgs,
        out string[] automationArgs,
        out string? errorMessage)
    {
        var host = new List<string>(args.Length);
        var auto = new List<string>();
        bool sawAutomationFlag = false;
        errorMessage = null;

        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i] ?? string.Empty;
            string trimmed = raw.Trim();

            if (TryMatchKeyValueFlag(args, ref i, trimmed, "--automation-port", out string? portVal, out bool missingPortVal))
            {
                sawAutomationFlag = true;
                if (missingPortVal || !int.TryParse(portVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int portNum) || portNum < 0 || portNum > 65535)
                {
                    errorMessage = "Option '--automation-port' requires an integer port between 0 and 65535.";
                    hostArgs = Array.Empty<string>();
                    automationArgs = Array.Empty<string>();
                    return false;
                }
                auto.Add("--automation-port");
                auto.Add(portNum.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            if (TryMatchKeyValueFlag(args, ref i, trimmed, "--automation-token", out string? tokenVal, out bool missingTokenVal))
            {
                sawAutomationFlag = true;
                if (missingTokenVal || string.IsNullOrWhiteSpace(tokenVal))
                {
                    errorMessage = "Option '--automation-token' requires a non-empty bearer token.";
                    hostArgs = Array.Empty<string>();
                    automationArgs = Array.Empty<string>();
                    return false;
                }
                auto.Add("--automation-token");
                auto.Add(tokenVal.Trim());
                continue;
            }

            host.Add(raw);
        }

#if !(DEBUG || MOTHER_TEST_AUTOMATION)
        if (sawAutomationFlag)
        {
            errorMessage = "Automation flags (--automation-port / --automation-token) are disabled in Release builds.";
            hostArgs = Array.Empty<string>();
            automationArgs = Array.Empty<string>();
            return false;
        }
#else
        _ = sawAutomationFlag;
#endif

        hostArgs = host.ToArray();
        automationArgs = auto.ToArray();
        return true;
    }

    private static bool TryMatchKeyValueFlag(
        string[] args,
        ref int index,
        string trimmed,
        string flagName,
        out string? value,
        out bool missingValue)
    {
        value = null;
        missingValue = false;

        if (trimmed.Equals(flagName, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                missingValue = true;
                return true;
            }
            index++;
            value = args[index].Trim();
            return true;
        }

        if (trimmed.StartsWith(flagName + "=", StringComparison.OrdinalIgnoreCase))
        {
            string rawVal = trimmed[(flagName.Length + 1)..].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rawVal))
            {
                missingValue = true;
                return true;
            }
            value = rawVal;
            return true;
        }

        return false;
    }

    internal static string ResolveMotherDefaultInstallDirCore(
        SandboxResolution sandbox,
        IPlatformRegistry registry,
        string? dataDir,
        bool isStage1Isolated,
        InstallerEngine? engine)
    {
        if (isStage1Isolated && !string.IsNullOrWhiteSpace(dataDir))
        {
            return Path.Combine(dataDir, "installed-app");
        }

        string? motherHome = Environment.GetEnvironmentVariable("MOTHER_HOME");
        if (!string.IsNullOrWhiteSpace(motherHome))
        {
            string fullHome = Path.GetFullPath(motherHome.Trim());
            return sandbox.IsSandboxActive
                ? SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullHome, sandbox)
                : fullHome;
        }

        if (OperatingSystem.IsWindows() || sandbox.IsSandboxActive)
        {
            string? regDir = registry
                .GetValue(PlatformRegistryHive.CurrentUser, MotherRegistrySubKey, "InstallDir")
                ?.AsString();
            if (!string.IsNullOrWhiteSpace(regDir))
            {
                string candidate = Path.GetFullPath(regDir.Trim());
                if ((!sandbox.IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(candidate, sandbox)) &&
                    Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (engine is not null)
        {
            return engine.ResolveDefaultInstallDirectory();
        }

        string localAppData = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox);
        return Path.Combine(localAppData, "Programs", "StreamItAll", "Mother", "Mother");
    }

    internal static void RegisterMotherSuiteMetadata(
        InstallerRuntimeContext ctx,
        string installDir,
        string? suiteDir,
        string? profileId,
        string version)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (string.IsNullOrWhiteSpace(installDir))
            return;

        if (ctx.DryRun || ctx.ShouldSuppressHostMutations || ctx.Registry is NoOpPlatformRegistry)
            return;

        if (!OperatingSystem.IsWindows() && !ctx.IsSandboxActive)
            return;

        string fullInstallDir = Path.GetFullPath(installDir.Trim());
        string effectiveSuiteDir = !string.IsNullOrWhiteSpace(suiteDir)
            ? Path.GetFullPath(suiteDir.Trim())
            : (Path.GetDirectoryName(fullInstallDir) ?? fullInstallDir);

        if (ctx.IsSandboxActive)
        {
            fullInstallDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullInstallDir, ctx.Sandbox);
            effectiveSuiteDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(effectiveSuiteDir, ctx.Sandbox);
        }

        string effectiveProfile = string.IsNullOrWhiteSpace(profileId) ? "full" : profileId.Trim();
        string effectiveVersion = string.IsNullOrWhiteSpace(version) ? "0.3.0" : version.Trim();

        ctx.Registry.SetValues(
            PlatformRegistryHive.CurrentUser,
            MotherRegistrySubKey,
            new Dictionary<string, PlatformRegistryValue>
            {
                ["InstallDir"] = new(PlatformRegistryValueKind.String, fullInstallDir),
                ["SuiteInstallDir"] = new(PlatformRegistryValueKind.String, effectiveSuiteDir),
                ["InstallProfile"] = new(PlatformRegistryValueKind.String, effectiveProfile),
                ["Version"] = new(PlatformRegistryValueKind.String, effectiveVersion)
            });
    }

    internal static void RemoveMotherSuiteMetadata(InstallerRuntimeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.DryRun || ctx.ShouldSuppressHostMutations || ctx.Registry is NoOpPlatformRegistry)
            return;

        if (!OperatingSystem.IsWindows() && !ctx.IsSandboxActive)
            return;

        try
        {
            ctx.Registry.DeleteSubKeyTree(PlatformRegistryHive.CurrentUser, MotherRegistrySubKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            ctx.WriteLog("WARN", $"Mother registry metadata cleanup warning: {ex.Message}");
        }
    }

    internal static InstallManifest? TryReadInstallManifest(string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir))
            return null;

        string[] candidates =
        {
            Path.Combine(installDir, "app", InstallerEngine.InstallManifestFileName),
            Path.Combine(installDir, InstallerEngine.InstallManifestFileName)
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<InstallManifest>(raw, ManifestJsonOpts);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"Mother install manifest read warning ({path}): {ex.Message}");
            }
        }

        return null;
    }

    internal static async Task TryMigrateLegacyMotherInstallAsync(
        InstallerRuntimeContext ctx,
        InstallerAppDescriptor descriptor,
        string installedDir,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (ctx.DryRun || ctx.ShouldSuppressHostMutations || string.IsNullOrWhiteSpace(installedDir))
            return;

        string[] appEnvArgs = !string.IsNullOrWhiteSpace(ctx.DataDirectory)
            ? new[] { "--data-dir", ctx.DataDirectory }
            : Array.Empty<string>();
        SandboxResolution sandboxOverride = ctx.IsSandboxActive ? ctx.Sandbox : SandboxResolution.Inactive;
        var legacyEnv = new AppEnvironment("mother", appEnvArgs, AppContext.BaseDirectory, "install-config.json", sandboxOverride);
        var legacyTheme = new InstallerThemeOptions
        {
            AppId = "mother",
            DisplayName = descriptor.DisplayName,
            Version = descriptor.Version,
            DefaultInstallDir = installedDir,
            StudioName = descriptor.StudioName,
            SuiteName = descriptor.SuiteName,
            UmbrellaSubpath = descriptor.UmbrellaSubpath
        };

        var legacyEngine = new InstallerEngineCore(legacyTheme, legacyEnv, ctx.Registry);
        string? legacyPath = legacyEngine.DetectLegacyFlatInstall();
        if (string.IsNullOrWhiteSpace(legacyPath))
            return;

        FoundationInstallResult migRes = await legacyEngine.MigrateLegacyInstallAsync(
            legacyPath,
            installedDir,
            optInConfirmed: true,
            dryRun: ctx.DryRun,
            cancellationToken: ct).ConfigureAwait(false);

        ctx.WriteLog(migRes.Success ? "INFO" : "WARN", migRes.Message);
    }

    internal static void RemoveShortcutsIfLive(AppEnvironment appEnv, bool isIsolated)
    {
        if (isIsolated || appEnv.DryRun || !OperatingSystem.IsWindows())
            return;

        SandboxResolution sandbox = appEnv.Sandbox;
        string[] paths =
        {
            Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, sandbox), "Mother.lnk"),
            Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.Programs, sandbox), "Mother", "Mother.lnk"),
            Path.Combine(KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.Startup, sandbox), "Mother.lnk")
        };

        foreach (string p in paths)
        {
            if (sandbox.IsSandboxActive && !SandboxEnvironmentSeam.IsPathWithinSandbox(p, sandbox))
                continue;

            if (File.Exists(p))
            {
                try { File.Delete(p); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.WriteLine($"Shortcut removal warning ({p}): {ex.Message}");
                }
            }
        }
    }
}
