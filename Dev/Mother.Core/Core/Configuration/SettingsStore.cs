// @module: Mother.Core.Configuration.SettingsStore
// @purpose: Atomic settings store wrapping GranularSettingsManager with v1->v2 migration, 20-snapshot history, rollback, and JSON export (SIS-03).
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Security, System.Text, System.Text.Json, Foundation.Core
// @exports: SettingsStore

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Core;

namespace Mother.Core.Configuration;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly GranularSettingsManager _manager;
    private readonly string _settingsFileTarget;
    private MotherSettings _current;

    public string SettingsFilePath => _settingsFileTarget;
    public string? LastDiagnosticWarning { get; private set; }
    public event Action<MotherSettings>? SettingsChanged;

    public SettingsStore(PathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _manager = new GranularSettingsManager(paths.DataDirectory, GranularSettingsManager.DefaultMaxHistorySnapshots);
        _settingsFileTarget = paths.ConfigFilePath;
        SettingsMigrationResult migration = SettingsMigrationService.MigrateBeforeLoad(paths, _manager, JsonOptions);
        SettingsLoadOutcome<MotherSettings> outcome = _manager.LoadOrSelfHeal(
            _settingsFileTarget,
            () => new MotherSettings().Normalized(),
            JsonOptions);
        _current = SettingsMigrationService.NormalizeAfterLoad(
            _settingsFileTarget,
            _manager,
            JsonOptions,
            outcome.Value,
            outcome.HealedFromBackup);
        LastDiagnosticWarning = CombineWarnings(migration.DiagnosticWarning, outcome.DiagnosticWarning);
    }

    public MotherSettings GetCurrent()
    {
        lock (_sync)
        {
            string json = JsonSerializer.Serialize(_current, JsonOptions);
            return JsonSerializer.Deserialize<MotherSettings>(json, JsonOptions)?.Normalized() ?? new MotherSettings();
        }
    }

    public MotherSettings Save(MotherSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        MotherSettings normalized = SettingsMigrationService.StripLegacyExtensionKeys(updated);
        lock (_sync)
        {
            _manager.SaveAtomically(_settingsFileTarget, normalized, JsonOptions, recordSnapshot: true);
            _current = normalized;
        }
        SettingsChanged?.Invoke(GetCurrent());
        return GetCurrent();
    }

    public MotherSettings ApplyAndSaveJson(JsonElement rawPayload)
    {
        MotherSettings updated = SettingsMigrationService.ParseAndNormalize(rawPayload, JsonOptions, out _);
        return Save(updated);
    }

    public IReadOnlyList<SettingsSnapshotSummary> ListSnapshots()
    {
        string targetName = Path.GetFileName(_settingsFileTarget);
        IReadOnlyList<string> paths = _manager.GetHistorySnapshots(targetName);
        var results = new List<SettingsSnapshotSummary>(paths.Count);
        foreach (string path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                results.Add(new SettingsSnapshotSummary(
                    SnapshotId: info.Name,
                    FileName: info.Name,
                    CreatedAtUtc: info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    FileSizeBytes: info.Length));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Transient snapshot metadata read issues should not block listing remaining snapshots.
                Trace.TraceWarning($"Skipped unreadable settings snapshot '{path}': {ex.Message}");
            }
        }
        return results;
    }

    public MotherSettings Rollback(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        string safeFileName = Path.GetFileName(snapshotId.Trim());
        string fullPath = AppEnvironment.AssertPathContained(
            Path.Combine(_manager.ConfigHistoryDirectory, safeFileName),
            _manager.ConfigHistoryDirectory);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Settings snapshot '{safeFileName}' was not found.", fullPath);
        }

        string raw = File.ReadAllText(fullPath, Encoding.UTF8);
        MotherSettings restored = SettingsMigrationService.ParseAndNormalize(raw, JsonOptions, out _);
        return Save(restored);
    }

    public SettingsExportPayload Export(string version) =>
        new(
            ExportedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            AppId: "mother",
            Version: version,
            Settings: GetCurrent());

    private static string? CombineWarnings(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second;
        if (string.IsNullOrWhiteSpace(second)) return first;
        return $"{first} {second}";
    }
}
