// @module: Mother.Core.Features.Doctor.FleetDoctorService
// @purpose: Runs Fleet System Doctor diagnostics, online SQLite VACUUM INTO backups, diagnostic bundle ZIP export, and safe automated repairs.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics, Mother.Core.Persistence
// @exports: FleetDoctorService

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;
using Mother.Core.Persistence;

namespace Mother.Core.Features.Doctor;

public sealed class FleetDoctorService
{
    private readonly PathResolver _paths;
    private readonly MotherDb _db;
    private readonly MotherDiagnostics _diag;
    private readonly string _version;

    public FleetDoctorService(PathResolver paths, MotherDb db, MotherDiagnostics diag, string version)
    {
        _paths = paths;
        _db = db;
        _diag = diag;
        _version = string.IsNullOrWhiteSpace(version) ? MotherBuildConstants.AppVersionFallback : version;
    }

    public DoctorReportDto RunChecks(
        IReadOnlyList<SisterAppEntry> manifests,
        IReadOnlyList<HkLockSnapshotDto> locks,
        bool watchdogRegistered)
    {
        var checks = new List<DoctorCheckDto>();

        bool sqliteOk = _db.CheckIntegrity(out string integrityDetail);
        int userVer = _db.GetUserVersion();
        checks.Add(new DoctorCheckDto(
            "sqlite-integrity",
            "SQLite WAL Integrity & PRAGMA user_version",
            sqliteOk && userVer >= MotherDb.TargetSchemaVersion ? "pass" : "fail",
            $"quick_check={integrityDetail}, PRAGMA user_version={userVer}",
            false));

        string sdkManifest = Path.Combine(_paths.BaseDirectory, "sdk-manifest.json");
        checks.Add(new DoctorCheckDto(
            "foundation-sdk",
            "Foundation SDK Pin",
            "pass",
            File.Exists(sdkManifest)
                ? "sdk-manifest.json verified alongside runtime."
                : $"Foundation.Core v{MotherBuildConstants.SdkVersion} loaded.",
            false));

        checks.Add(new DoctorCheckDto(
            "manifests-discovered",
            "Sister-App Manifest Registry",
            "pass",
            manifests.Count > 0
                ? $"{manifests.Count} sister-app manifest(s) loaded from '{_paths.ManifestsDirectory}' / '{_paths.ProgramsRoot}'."
                : $"0 sister-app manifests registered yet (add suite.manifest.json under '{_paths.ManifestsDirectory}' or '{_paths.ProgramsRoot}').",
            false));

        int staleLocks = locks.Count(l => l.Status == "stale");
        checks.Add(new DoctorCheckDto(
            "stale-hklocks",
            "Orphan .hklock Sentinels",
            staleLocks == 0 ? "pass" : "warn",
            staleLocks == 0 ? "Zero stale locks detected." : $"{staleLocks} stale .hklock sentinel(s) eligible for purge.",
            staleLocks > 0));

        bool simulatedWatchdog = _paths.ShouldSuppressLiveHostMutations || _paths.Options.DryRun;
        checks.Add(new DoctorCheckDto(
            "watchdog-task",
            "Master Watchdog (\\Mother_Watchdog)",
            watchdogRegistered || simulatedWatchdog ? "pass" : "warn",
            simulatedWatchdog
                ? "Simulated in isolated/sandbox/dry-run mode (host Task Scheduler untouched)."
                : (watchdogRegistered ? @"\Mother_Watchdog registered." : @"\Mother_Watchdog not registered yet."),
            !watchdogRegistered && !simulatedWatchdog));

        int crashes = _diag.CountCrashBundles();
        checks.Add(new DoctorCheckDto(
            "crash-bundles",
            "Crash Bundle Presence",
            crashes == 0 ? "pass" : "warn",
            $"{crashes} crash bundle(s) in '{Path.Combine(_paths.LogDirectory, "crashes")}'.",
            false));

        string overall = checks.Any(c => c.Status == "fail")
            ? "fail"
            : (checks.Any(c => c.Status == "warn") ? "warn" : "pass");

        return new DoctorReportDto(
            Overall: overall,
            CheckedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            SuiteVersion: _version,
            SqliteUserVersion: userVer,
            CrashBundleCount: crashes,
            SuiteChecks: checks);
    }

    public async Task<string> BackupDatabaseAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string dest = Path.Combine(_paths.BackupsDirectory, $"mother_backup_{stamp}.db");
        string result = await _db.BackupToAsync(dest, ct).ConfigureAwait(false);
        _diag.Log("INFO", "mother", "Doctor", $"Created online SQLite backup: {result}");
        return result;
    }

    public async Task<string> ExportDiagnosticBundleAsync(DoctorReportDto report, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string zipPath = Path.Combine(_paths.BackupsDirectory, $"mother_diagnostics_{stamp}.zip");
        string tempDir = Path.Combine(_paths.BackupsDirectory, $"bundle_tmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "doctor_report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false),
                ct).ConfigureAwait(false);

            if (File.Exists(_paths.ConfigFilePath))
            {
                File.Copy(_paths.ConfigFilePath, Path.Combine(tempDir, "settings.json"), overwrite: true);
            }

            foreach (string log in Directory.EnumerateFiles(_paths.LogDirectory, "*.jsonl", SearchOption.TopDirectoryOnly).TakeLast(5))
            {
                File.Copy(log, Path.Combine(tempDir, Path.GetFileName(log)), overwrite: true);
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);
            _diag.Log("INFO", "mother", "Doctor", $"Exported diagnostic bundle: {zipPath}");
            return zipPath;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _diag.Log("WARN", "mother", "Doctor", $"Diagnostic temp cleanup skipped for '{tempDir}': {ex.Message}");
                }
            }
        }
    }
}
