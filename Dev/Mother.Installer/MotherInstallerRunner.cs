// @module: Mother.Installer.MotherInstallerRunner
// @purpose: Declarative manifest-driven Mother installer host migrating to StandardInstallerHost, InstallerEngine, IPlatformRegistry, and Debug/TestAutomation loopback wiring.
// @depends-on: System, System.IO, System.Linq, System.Reflection, System.Threading, System.Windows.Forms, Foundation.Core, Foundation.Installer
// @exports: MotherInstallerRunner

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
#if DEBUG || MOTHER_TEST_AUTOMATION
using Foundation.Automation;
#endif
using Foundation.Core;
using Foundation.Installer;

namespace Mother.Installer;

internal static class MotherInstallerRunner
{
    private const string EmbeddedPayloadName = "Mother.Installer.Embedded.payload.zip";

    public static int Run(string[] args, string version, Assembly payloadAssembly)
    {
        args ??= Array.Empty<string>();
        if (!MotherInstallerIntegration.TryHandleAutomationArgs(args, out string[] hostArgs, out string[] automationArgs, out string? autoErr))
        {
            Console.Error.WriteLine(autoErr ?? "Invalid automation CLI option.");
            return 2;
        }
#if !(DEBUG || MOTHER_TEST_AUTOMATION)
        _ = automationArgs;
#endif

        InstallerAppDescriptor effectiveDescriptor = ResolveEffectiveDescriptor(version, payloadAssembly);
        InstallerCliOptions cli;
        try
        {
            cli = InstallerEngine.ParseCliArgs(hostArgs, effectiveDescriptor);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Console.Error.WriteLine($"Setup argument error: {ex.Message}");
            return 2;
        }

#if DEBUG || MOTHER_TEST_AUTOMATION
        if (!cli.ShowVersion &&
            !cli.ShowHelp &&
            !cli.Silent &&
            !cli.DryRun &&
            !cli.VerifyOnly &&
            (!IsIsolatedTestEnv() || automationArgs.Length > 0) &&
            OperatingSystem.IsWindows())
        {
            return RunInteractiveWizardWithAutomation(effectiveDescriptor, cli, automationArgs, payloadAssembly);
        }
#endif

        InstallerHostRunResult hostResult = StandardInstallerHost.RunWithResult(effectiveDescriptor, hostArgs, payloadAssembly);
        if (hostResult.ExitCode == 0 && !cli.ShowVersion && !cli.ShowHelp && !cli.VerifyOnly && !cli.DryRun)
        {
            ApplyPostHostIntegration(effectiveDescriptor, cli, hostResult);
        }

        return hostResult.ExitCode;
    }

    internal static InstallerAppDescriptor CreateBaseDescriptor(string version) => new(
        ProductName: "Mother",
        DisplayName: "Mother",
        Publisher: "StreamItAll",
        Version: string.IsNullOrWhiteSpace(version) ? "0.3.0" : version.Trim(),
        MainExecutableRelativePath: "Mother.exe",
        Subtitle: "Clean Release Root (Mother.exe + app\\, scripts\\, data\\)",
        EmbeddedPayloadResourceName: EmbeddedPayloadName,
        BrandAccentHex: "#38BDF8",
        AllowAutoStart: true,
        StudioName: "StreamItAll",
        SuiteName: "Mother",
        UmbrellaSubpath: Path.Combine("StreamItAll", "Mother", "Mother"),
        DefaultProfile: "full",
        AllowLegacyMigration: true);

    internal static InstallerAppDescriptor ResolveEffectiveDescriptor(string version, Assembly? payloadAssembly = null)
    {
        InstallerAppDescriptor baseDescriptor = CreateBaseDescriptor(version);
        Assembly resolvedAssembly = payloadAssembly ?? typeof(MotherInstallerRunner).Assembly;
        InstallerMarkupManifest? markup = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(
            AppContext.BaseDirectory,
            resolvedAssembly);
        return markup is not null
            ? InstallerManifestStore.ApplyToDescriptor(baseDescriptor, markup)
            : baseDescriptor;
    }

    public static InstallerThemeOptions BuildOptions(AppEnvironment appEnv, string version, string? suiteDirOverride = null)
    {
        ArgumentNullException.ThrowIfNull(appEnv);
        string defaultInstallDir = ResolveMotherDefaultInstallDir(appEnv);
        string defaultSuiteDir = !string.IsNullOrWhiteSpace(suiteDirOverride)
            ? Path.GetFullPath(suiteDirOverride.Trim())
            : (Path.GetDirectoryName(defaultInstallDir) ?? defaultInstallDir);

        InstallerMarkupManifest? markup = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(
            AppContext.BaseDirectory,
            typeof(MotherInstallerRunner).Assembly);
        if (markup is not null)
        {
            InstallerThemeOptions fromManifest = InstallerManifestStore.ToThemeOptions(markup, defaultInstallDir);
            return fromManifest with
            {
                Version = string.IsNullOrWhiteSpace(version) ? fromManifest.Version : version.Trim(),
                BootstrapSharedSuiteDaemon = false,
                UninstallerFileName = "Mother-Uninstall.exe",
                UmbrellaSubpath = Path.Combine("StreamItAll", "Mother", "Mother"),
                SecondaryPathPicker = new SecondaryPathPickerOptions(
                    FlagName: "--suite-dir",
                    Label: "Shared Suite Umbrella Directory:",
                    DefaultPath: defaultSuiteDir,
                    Placeholder: defaultSuiteDir,
                    Tooltip: "Root directory containing Mother and installed sister applications.")
            };
        }

        return new InstallerThemeOptions
        {
            AppId = "mother",
            DisplayName = "Mother",
            Version = string.IsNullOrWhiteSpace(version) ? "0.3.0" : version.Trim(),
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
        ArgumentNullException.ThrowIfNull(appEnv);
        if (IsIsolatedContext(appEnv))
        {
            return Path.Combine(appEnv.DataDirectory, "installed-app");
        }

        SandboxResolution sandbox = appEnv.Sandbox.IsSandboxActive ? appEnv.Sandbox : SandboxResolution.Inactive;
        IPlatformRegistry registry = PlatformRegistryFactory.CreateDefault(sandbox, suppressMutations: false);
        return MotherInstallerIntegration.ResolveMotherDefaultInstallDirCore(sandbox, registry, dataDir: null, isStage1Isolated: false, engine: null);
    }

    internal static string ResolveMotherDefaultInstallDir(InstallerRuntimeContext ctx, InstallerEngine? engine = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        SandboxResolution sandbox = ctx.IsSandboxActive ? ctx.Sandbox : SandboxResolution.Inactive;
        return MotherInstallerIntegration.ResolveMotherDefaultInstallDirCore(sandbox, ctx.Registry, ctx.DataDirectory, ctx.IsStage1Isolated, engine);
    }

#if DEBUG || MOTHER_TEST_AUTOMATION
    private static int RunInteractiveWizardWithAutomation(
        InstallerAppDescriptor descriptor,
        InstallerCliOptions cli,
        string[] automationArgs,
        Assembly payloadAssembly)
    {
        InstallerRuntimeContext? ctx = null;
        try
        {
            ctx = new InstallerRuntimeContext(
                cliDataDir: cli.DataDir,
                cliConfigPath: cli.ConfigPath,
                cliLogDir: cli.LogDir,
                dryRun: cli.DryRun,
                envPrefix: descriptor.EnvPrefix);

            var guiEngine = new InstallerEngine(descriptor, ctx, payloadAssembly);
            string resolvedInitialDir = !string.IsNullOrWhiteSpace(cli.InstallDir)
                ? Path.GetFullPath(cli.InstallDir.Trim())
                : ResolveMotherDefaultInstallDir(ctx, guiEngine);
            string defaultSuiteDir = Path.GetDirectoryName(resolvedInitialDir) ?? resolvedInitialDir;

            string? pickerDefault = !string.IsNullOrWhiteSpace(cli.SecondaryDir)
                ? cli.SecondaryDir
                : (!string.IsNullOrWhiteSpace(ctx.ConfigOverride?.SuiteInstallDir)
                    ? ctx.ConfigOverride.SuiteInstallDir
                    : defaultSuiteDir);

            InstallerAppDescriptor wizardDescriptor = descriptor.SecondaryPickers is { Count: > 0 } pickers
                ? descriptor with
                {
                    SecondaryPickers = pickers
                        .Select((p, idx) => idx == 0 ? p with { DefaultPath = pickerDefault } : p)
                        .ToList()
                }
                : descriptor;
            var configuredEngine = new InstallerEngine(wizardDescriptor, ctx, payloadAssembly);

            InitializeWinFormsHost();

            bool effectiveAutoStart = wizardDescriptor.AllowAutoStart && cli.AutoStart;
            bool explicitMigrate = cli.MigrateLegacy || (ctx.ConfigOverride?.MigrateLegacy ?? false);
            bool showLegacy = wizardDescriptor.AllowLegacyMigration || explicitMigrate;
            bool passExplicitSecondary = !string.IsNullOrWhiteSpace(cli.SecondaryDir) &&
                                         string.Equals(cli.SecondaryFlagName, "--reengine-dir", StringComparison.OrdinalIgnoreCase);

            using var wizard = new InstallerWizardForm(
                engine: configuredEngine,
                ctx: ctx,
                initialInstallDir: resolvedInitialDir,
                externalPayloadPath: cli.PayloadPath,
                isUninstallMode: cli.Uninstall,
                initialCreateShortcuts: !cli.NoShortcuts,
                initialAutoStart: effectiveAutoStart,
                initialLaunchAfter: !cli.NoLaunch,
                initialPurgeData: cli.PurgeData,
                initialProfileId: cli.ProfileId,
                initialSecondaryDir: passExplicitSecondary ? cli.SecondaryDir : null,
                secondaryFlagName: passExplicitSecondary ? cli.SecondaryFlagName : null,
                initialMigrateLegacy: explicitMigrate,
                showLegacyMigration: showLegacy,
                payloadAssembly: payloadAssembly);

            using AutomationLoopbackServer? automationServer = AutomationCliHelper.TryCreateFromArgs(
                "mother-installer",
                automationArgs,
                wizard);
            automationServer?.Start();

            Application.Run(wizard);

            if (wizard.ExitCode == 0)
            {
                if (cli.Uninstall)
                {
                    MotherInstallerIntegration.RemoveMotherSuiteMetadata(ctx);
                }
                else
                {
                    string chosenInstallDir = wizard.InstallDirTextBox.Text.Trim();
                    string chosenSuiteDir = wizard.SecondaryInstallDir;
                    MotherInstallerIntegration.RegisterMotherSuiteMetadata(
                        ctx,
                        chosenInstallDir,
                        chosenSuiteDir,
                        wizard.SelectedProfileId,
                        wizardDescriptor.Version);

                    if (wizard.MigrateLegacyChecked)
                    {
                        MotherInstallerIntegration.TryMigrateLegacyMotherInstallAsync(
                            ctx,
                            wizardDescriptor,
                            chosenInstallDir,
                            CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
            }

            return wizard.ExitCode;
        }
        catch (Exception ex)
        {
            int exitCode = SandboxEnvironmentSeam.IsContainmentViolation(ex)
                ? SandboxEnvironmentSeam.ContainmentExitCode
                : 1;
            ctx?.WriteLog("ERROR", $"Installer automation wizard failed (ExitCode={exitCode}): {ex.Message}");
            Console.Error.WriteLine($"Setup error: {ex.Message}");
            return exitCode;
        }
    }

    private static void InitializeWinFormsHost()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
        catch (InvalidOperationException ex)
        {
            _ = ex; // WinForms host initialization may already be active inside test runners.
        }
    }

    private static bool IsIsolatedTestEnv()
    {
        string? raw = Environment.GetEnvironmentVariable(SandboxEnvironmentSeam.EnvIsolatedTest);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string v = raw.Trim();
        return v.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
#endif

    private static void ApplyPostHostIntegration(
        InstallerAppDescriptor descriptor,
        InstallerCliOptions cli,
        InstallerHostRunResult hostResult)
    {
        var ctx = new InstallerRuntimeContext(
            cliDataDir: cli.DataDir,
            cliConfigPath: cli.ConfigPath,
            cliLogDir: cli.LogDir,
            dryRun: cli.DryRun,
            envPrefix: descriptor.EnvPrefix);

        if (cli.Uninstall)
        {
            MotherInstallerIntegration.RemoveMotherSuiteMetadata(ctx);
            return;
        }

        string? installedDir = hostResult.OperationResult?.InstallDirectory;
        if (string.IsNullOrWhiteSpace(installedDir))
        {
            installedDir = ctx.Registry
                .GetValue(PlatformRegistryHive.CurrentUser, descriptor.WindowsUninstallRegistrySubKey, "InstallLocation")
                ?.AsString();
        }
        if (string.IsNullOrWhiteSpace(installedDir) || !Directory.Exists(installedDir))
        {
            return;
        }

        InstallManifest? manifest = MotherInstallerIntegration.TryReadInstallManifest(installedDir);
        string? suiteDir = !string.IsNullOrWhiteSpace(cli.SuiteDir) ? cli.SuiteDir : manifest?.SuiteInstallDir;
        string profileId = !string.IsNullOrWhiteSpace(cli.ProfileId) ? cli.ProfileId : (manifest?.InstallProfile ?? descriptor.DefaultProfile);
        bool migrateLegacy = cli.MigrateLegacy || (manifest?.MigrateLegacy ?? false);

        MotherInstallerIntegration.RegisterMotherSuiteMetadata(ctx, installedDir, suiteDir, profileId, descriptor.Version);
        if (migrateLegacy)
        {
            MotherInstallerIntegration.TryMigrateLegacyMotherInstallAsync(
                ctx,
                descriptor,
                installedDir,
                CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    internal static bool IsIsolatedContext(AppEnvironment appEnv) =>
        string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "1", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            Path.GetFullPath(appEnv.DataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(Path.Combine(appEnv.BaseDirectory, "data")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
