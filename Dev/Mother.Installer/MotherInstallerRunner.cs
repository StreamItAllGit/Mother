// @module: Mother.Installer.MotherInstallerRunner
// @purpose: Wires Foundation.Installer InstallerEngineCore, Studio/Suite theme options, dark Wizard UI, --uninstall --dry-run guard, and opt-in legacy migration.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Reflection, System.Runtime.InteropServices, System.Threading, System.Threading.Tasks, System.Windows.Forms, Microsoft.Win32, Foundation.Core, Foundation.Installer
// @exports: MotherInstallerRunner

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using Microsoft.Win32;

namespace Mother.Installer;

internal static class MotherInstallerRunner
{
    private const string EmbeddedPayloadName = "Mother.Installer.Embedded.payload.zip";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static int Run(string[] args, string version, Assembly payloadAssembly)
    {
        if (args.Length > 0 && OperatingSystem.IsWindows())
        {
            try
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    AttachConsole(-1);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Mother.Installer AttachConsole skipped: {ex.Message}");
            }
        }

        if (HasFlag(args, "--version") || HasFlag(args, "-v"))
        {
            Console.WriteLine(version);
            return 0;
        }

        if (HasFlag(args, "--help") || HasFlag(args, "-h") || HasFlag(args, "/?"))
        {
            Console.WriteLine($"Mother Setup v{version}");
            Console.WriteLine("Usage: Mother-Setup.exe [--install-dir <dir>] [--suite-dir <dir>] [--profile <lightweight|full>] [--migrate-legacy] [--silent] [--verify] [--uninstall] [--purge-data] [--autostart] [--dry-run] [--data-dir <dir>] [--log-dir <dir>]");
            return 0;
        }

        var appEnv = new AppEnvironment("mother", args, AppContext.BaseDirectory, "install-config.json");
        bool isIsolated = IsIsolatedContext(appEnv);
        string? cliInstallDir = ReadFlagValue(args, "--install-dir");
        string? cliSuiteDir = ReadFlagValue(args, "--suite-dir");
        string? cliProfile = ReadFlagValue(args, "--profile");
        string? cliPayload = ReadFlagValue(args, "--payload");
        bool isUninstall = HasFlag(args, "--uninstall");
        bool isVerify = HasFlag(args, "--verify");
        bool isSilent = HasFlag(args, "--silent");
        bool purgeData = HasFlag(args, "--purge-data");
        bool autoStart = HasFlag(args, "--autostart");
        bool migrateLegacy = HasFlag(args, "--migrate-legacy");

        InstallerThemeOptions theme = BuildOptions(appEnv, version, cliSuiteDir);
        var coreEngine = new InstallerEngineCore(theme, appEnv);

        if (isVerify)
        {
            bool ok = coreEngine.VerifyInstallation(cliInstallDir, out string msg);
            Console.WriteLine(msg);
            return ok ? 0 : 1;
        }

        if (isSilent)
        {
            if (isUninstall)
            {
                FoundationInstallResult ures = ExecuteUninstall(coreEngine, appEnv, isIsolated, cliInstallDir, purgeData);
                Console.WriteLine(ures.Message);
                return ures.Success ? 0 : 1;
            }

            FoundationInstallResult res = ExecuteInstallAsync(
                coreEngine,
                isIsolated,
                payloadAssembly,
                cliPayload,
                cliInstallDir,
                cliSuiteDir,
                cliProfile ?? theme.DefaultProfileId,
                migrateLegacy,
                createShortcuts: true,
                autoStart: autoStart,
                onProgress: null,
                CancellationToken.None).GetAwaiter().GetResult();

            Console.WriteLine(res.Message);
            return res.Success ? 0 : 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        InstallerWizardForm? wizard = null;
        wizard = new InstallerWizardForm(
            coreEngine,
            isUninstallMode: isUninstall,
            initialInstallDir: cliInstallDir,
            customInstallAction: (ct, progress) =>
            {
                if (isUninstall)
                {
                    return Task.FromResult(ExecuteUninstall(coreEngine, appEnv, isIsolated, wizard?.InstallDirTextBox.Text.Trim() ?? cliInstallDir, purgeData));
                }

                return ExecuteInstallAsync(
                    coreEngine,
                    isIsolated,
                    payloadAssembly,
                    cliPayload,
                    wizard?.InstallDirTextBox.Text.Trim() ?? cliInstallDir,
                    wizard?.SecondaryInstallDir is { Length: > 0 } sec ? sec : cliSuiteDir,
                    wizard?.SelectedProfileId ?? cliProfile ?? theme.DefaultProfileId,
                    wizard?.MigrateLegacyChecked ?? migrateLegacy,
                    createShortcuts: wizard?.ShortcutsCheckBox.Checked ?? true,
                    autoStart: wizard?.AutoStartCheckBox.Checked ?? autoStart,
                    onProgress: progress,
                    ct: ct);
            },
            initialProfileId: cliProfile,
            initialSecondaryDir: cliSuiteDir,
            initialMigrateLegacy: migrateLegacy);

        Application.Run(wizard);
        return wizard.ExitCode;
    }

    public static InstallerThemeOptions BuildOptions(AppEnvironment appEnv, string version, string? suiteDirOverride = null)
    {
        string defaultInstallDir = ResolveMotherDefaultInstallDir(appEnv);
        string defaultSuiteDir = !string.IsNullOrWhiteSpace(suiteDirOverride)
            ? Path.GetFullPath(suiteDirOverride.Trim())
            : (Path.GetDirectoryName(defaultInstallDir) ?? defaultInstallDir);

        return new InstallerThemeOptions
        {
            AppId = "mother",
            DisplayName = "Mother",
            Version = version,
            Subtitle = "Clean Release Root (Mother.exe + app\\, scripts\\, data\\)",
            BrandAccentHex = "#38BDF8",
            DefaultInstallDir = defaultInstallDir,
            ExecutableFileName = "Mother.exe",
            UninstallerFileName = "Mother-Uninstall.exe",
            BootstrapSharedSuiteDaemon = false,
            AllowAutoStart = true,
            StudioName = "StreamItAll",
            SuiteName = "Mother",
            UmbrellaSubpath = Path.Combine("StreamItAll", "Mother", "Mother"),
            AvailableProfiles = new[]
            {
                new InstallerProfileOption("lightweight", InstallerProfileKind.Lightweight, "Lightweight", "Core suite gateway & health supervisor only."),
                new InstallerProfileOption("full", InstallerProfileKind.Full, "Full Suite Host", "Complete Clean Release Root with bundled Web UI & scripts.", IsDefault: true)
            },
            DefaultProfileId = "full",
            SecondaryPathPicker = new SecondaryPathPickerOptions(
                FlagName: "--suite-dir",
                Label: "Shared Suite Umbrella Directory:",
                DefaultPath: defaultSuiteDir,
                Placeholder: defaultSuiteDir,
                Tooltip: "Root directory containing Mother and installed sister applications."),
            AllowLegacyMigration = true
        };
    }

    public static string ResolveMotherDefaultInstallDir(AppEnvironment appEnv)
    {
        if (IsIsolatedContext(appEnv))
        {
            return Path.Combine(appEnv.DataDirectory, "installed-app");
        }

        string? motherHome = Environment.GetEnvironmentVariable("MOTHER_HOME");
        if (!string.IsNullOrWhiteSpace(motherHome))
        {
            return Path.GetFullPath(motherHome.Trim());
        }

        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\StreamItAll\Mother");
            string? regDir = key?.GetValue("InstallDir") as string;
            if (!string.IsNullOrWhiteSpace(regDir))
            {
                return Path.GetFullPath(regDir.Trim());
            }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Programs", "StreamItAll", "Mother", "Mother");
    }

    private static async Task<FoundationInstallResult> ExecuteInstallAsync(
        InstallerEngineCore engine,
        bool isIsolated,
        Assembly payloadAssembly,
        string? externalPayloadPath,
        string? installDir,
        string? suiteDir,
        string profileId,
        bool migrateLegacy,
        bool createShortcuts,
        bool autoStart,
        Action<int, string>? onProgress,
        CancellationToken ct)
    {
        await using Stream payloadStream = OpenPayloadStream(payloadAssembly, externalPayloadPath);
        FoundationInstallResult result = await engine.InstallOrUpgradeFromZipAsync(
            payloadStream,
            installDir,
            onProgress,
            ct).ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        string normalizedSuiteDir = !string.IsNullOrWhiteSpace(suiteDir)
            ? Path.GetFullPath(suiteDir.Trim())
            : (Path.GetDirectoryName(result.InstallDirectory) ?? result.InstallDirectory);

        if (isIsolated && !string.IsNullOrWhiteSpace(suiteDir))
        {
            string root = ResolveIsolatedContainmentRoot(engine.RuntimeEnvironment.DataDirectory) ?? engine.RuntimeEnvironment.DataDirectory;
            AppEnvironment.AssertPathContained(normalizedSuiteDir, root);
        }

        if (!engine.RuntimeEnvironment.DryRun)
        {
            MotherInstallerIntegration.CopyUninstallerSelf(result.InstallDirectory);
            var plan = MotherInstallerIntegration.PlanLivePostInstallActions(
                isIsolated,
                engine.RuntimeEnvironment.DryRun,
                OperatingSystem.IsWindows(),
                createShortcuts,
                autoStart);
            if (plan.WriteShortcuts || plan.WriteAutoStart || plan.WriteRegistry)
            {
                MotherInstallerIntegration.ApplyLivePostInstallActions(engine, result.InstallDirectory, normalizedSuiteDir, profileId, plan);
            }
        }

        if (migrateLegacy)
        {
            string? legacyPath = engine.DetectLegacyFlatInstall();
            if (!string.IsNullOrWhiteSpace(legacyPath))
            {
                await engine.MigrateLegacyInstallAsync(
                    legacyPath,
                    result.InstallDirectory,
                    optInConfirmed: true,
                    dryRun: engine.RuntimeEnvironment.DryRun,
                    ct).ConfigureAwait(false);
            }
        }

        return result;
    }

    private static Stream OpenPayloadStream(Assembly payloadAssembly, string? externalPayloadPath)
    {
        if (!string.IsNullOrWhiteSpace(externalPayloadPath))
        {
            string full = Path.GetFullPath(externalPayloadPath.Trim());
            if (!File.Exists(full))
            {
                throw new FileNotFoundException($"External installer payload not found: '{full}'.", full);
            }
            return File.OpenRead(full);
        }

        return payloadAssembly.GetManifestResourceStream(EmbeddedPayloadName)
            ?? throw new InvalidOperationException($"Missing embedded installer resource '{EmbeddedPayloadName}'.");
    }

    internal static FoundationInstallResult ExecuteUninstall(
        InstallerEngineCore coreEngine,
        AppEnvironment appEnv,
        bool isIsolated,
        string? installDir,
        bool purgeData)
    {
        if (appEnv.DryRun)
        {
            string target = string.IsNullOrWhiteSpace(installDir)
                ? coreEngine.Theme.DefaultInstallDir
                : Path.GetFullPath(installDir.Trim());
            if (isIsolated)
            {
                string boundary = ResolveIsolatedContainmentRoot(appEnv.DataDirectory) ?? appEnv.DataDirectory;
                target = AppEnvironment.AssertPathContained(target, boundary);
            }
            return new FoundationInstallResult(
                true,
                target,
                string.Empty,
                0,
                $"Dry-run simulated uninstall for '{target}' (purgeData={purgeData}); no files removed.",
                false);
        }

        FoundationInstallResult ures = coreEngine.Uninstall(installDir, purgeData);
        MotherInstallerIntegration.RemoveShortcutsIfLive(appEnv, isIsolated);
        return ures;
    }

    internal static bool IsIsolatedContext(AppEnvironment appEnv) =>
        string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "1", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            Path.GetFullPath(appEnv.DataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(Path.Combine(appEnv.BaseDirectory, "data")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    internal static string? ResolveIsolatedContainmentRoot(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) return null;
        string full = Path.GetFullPath(dataDirectory.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(full), "data", StringComparison.OrdinalIgnoreCase))
            return null;
        string? parent = Directory.GetParent(full)?.FullName;
        if (string.IsNullOrWhiteSpace(parent)) return null;
        string normParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normParent, normTemp, StringComparison.OrdinalIgnoreCase) ? null : normParent;
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flagName) =>
        args.Any(a => string.Equals(a, flagName, StringComparison.OrdinalIgnoreCase));

    private static string? ReadFlagValue(IReadOnlyList<string> args, string flagName)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], flagName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return args[i + 1];
            if (args[i].StartsWith(flagName + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(flagName.Length + 1)..].Trim('"');
        }
        return null;
    }
}
