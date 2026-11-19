// @module: Mother.Core.Configuration.SettingsMigrationService
// @purpose: Migrates legacy data\settings.json and v1 config payloads into data\config\settings.json (schemaVersion 2) with Standard 18 keys.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Globalization, System.IO, System.Text, System.Text.Json, System.Text.Json.Nodes, Foundation.Core
// @exports: SettingsMigrationResult, SettingsMigrationService

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Core;

namespace Mother.Core.Configuration;

public sealed record SettingsMigrationResult(
    bool MigratedFromLegacyPath,
    bool UpgradedInPlace,
    string? DiagnosticWarning
);

public static class SettingsMigrationService
{
    private enum LegacyValueKind
    {
        Boolean,
        Int32
    }

    private readonly record struct LegacyKeyMapping(
        string LegacyKey,
        string CanonicalKey,
        LegacyValueKind Kind,
        Action<MotherSettings, JsonElement> ApplyFallback);

    private static readonly LegacyKeyMapping[] LegacyMappings =
    [
        new("allowTailscaleBind", "shouldAllowTailscaleBind", LegacyValueKind.Boolean,
            (s, el) => { if (TryReadBool(el, out bool v)) s.ShouldAllowTailscaleBind = v; }),
        new("coldWakeIdleMinutes", "idleTimeoutMinutes", LegacyValueKind.Int32,
            (s, el) => { if (TryReadInt(el, out int v)) s.IdleTimeoutMinutes = v; }),
        new("sleepInhibitDuringActiveLock", "shouldInhibitSleepDuringActiveLock", LegacyValueKind.Boolean,
            (s, el) => { if (TryReadBool(el, out bool v)) s.ShouldInhibitSleepDuringActiveLock = v; }),
        new("autoAlignWithSisterApps", "shouldAutoAlignSisterApps", LegacyValueKind.Boolean,
            (s, el) => { if (TryReadBool(el, out bool v)) s.ShouldAutoAlignSisterApps = v; }),
        new("desktopToastNotifications", "shouldShowDesktopToasts", LegacyValueKind.Boolean,
            (s, el) => { if (TryReadBool(el, out bool v)) s.ShouldShowDesktopToasts = v; }),
        new("discordWebhookNotifications", "shouldSendDiscordWebhooks", LegacyValueKind.Boolean,
            (s, el) => { if (TryReadBool(el, out bool v)) s.ShouldSendDiscordWebhooks = v; })
    ];

    public static SettingsMigrationResult MigrateBeforeLoad(
        PathResolver paths,
        GranularSettingsManager manager,
        JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        string targetPath = Path.GetFullPath(paths.ConfigFilePath);
        if (paths.IsSandboxActive)
        {
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(targetPath, paths.Sandbox);
        }

        string defaultConfigPath = Path.GetFullPath(Path.Combine(paths.DataDirectory, "config", "settings.json"));
        string legacyPath = paths.AssertContainedIfIsolatedOrSandboxed(Path.Combine(paths.DataDirectory, "settings.json"));
        string legacyMigratedBakPath = paths.AssertContainedIfIsolatedOrSandboxed(Path.Combine(paths.DataDirectory, "settings.json.migrated.bak"));
        bool isUsingDefaultTarget = string.Equals(targetPath, defaultConfigPath, StringComparison.OrdinalIgnoreCase);

        if (isUsingDefaultTarget && !File.Exists(targetPath) && File.Exists(legacyPath))
        {
            return MigrateLegacyFileToDefaultConfig(legacyPath, legacyMigratedBakPath, targetPath, manager, jsonOptions);
        }

        if (File.Exists(targetPath) &&
            TryReadAndNormalizeFile(targetPath, jsonOptions, out MotherSettings? upgraded, out bool wasUpgraded) &&
            wasUpgraded &&
            upgraded is not null)
        {
            manager.SaveAtomically(targetPath, upgraded, jsonOptions, recordSnapshot: true);
            return new SettingsMigrationResult(false, true, null);
        }

        return new SettingsMigrationResult(false, false, null);
    }

    public static MotherSettings NormalizeAfterLoad(
        string settingsFilePath,
        GranularSettingsManager manager,
        JsonSerializerOptions jsonOptions,
        MotherSettings loaded,
        bool healedFromBackup)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (!healedFromBackup && !NeedsNormalization(loaded))
        {
            return loaded.Normalized();
        }

        if (File.Exists(settingsFilePath))
        {
            try
            {
                string raw = File.ReadAllText(settingsFilePath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    MotherSettings normalizedFromDisk = ParseAndNormalize(raw, jsonOptions, out bool upgraded);
                    if (upgraded || healedFromBackup)
                    {
                        manager.SaveAtomically(settingsFilePath, normalizedFromDisk, jsonOptions, recordSnapshot: false);
                    }
                    return normalizedFromDisk;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                Trace.TraceWarning($"Post-load settings migration fallback for '{settingsFilePath}': {ex.Message}");
            }
        }

        MotherSettings normalized = NormalizeLoadedObject(loaded, out bool objectChanged);
        if (objectChanged)
        {
            manager.SaveAtomically(settingsFilePath, normalized, jsonOptions, recordSnapshot: false);
        }
        return normalized;
    }

    public static bool NeedsNormalization(MotherSettings settings)
    {
        if (settings.SchemaVersion < 2) return true;
        if (settings.ExtensionData is null || settings.ExtensionData.Count == 0) return false;
        foreach (string key in settings.ExtensionData.Keys)
        {
            if (IsLegacyKey(key)) return true;
        }
        return false;
    }

    public static MotherSettings ParseAndNormalize(JsonElement rawPayload, JsonSerializerOptions jsonOptions, out bool wasUpgraded)
    {
        if (rawPayload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Settings payload must be a JSON object.");

        JsonObject obj = JsonObject.Create(rawPayload)
            ?? throw new InvalidDataException("Settings payload could not be parsed as a JSON object.");
        return NormalizeJsonObjectAndDeserialize(obj, jsonOptions, out wasUpgraded);
    }

    public static MotherSettings ParseAndNormalize(string rawJson, JsonSerializerOptions jsonOptions, out bool wasUpgraded)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new InvalidDataException("Settings payload is empty.");

        if (JsonNode.Parse(rawJson) is not JsonObject obj)
            throw new InvalidDataException("Settings payload must be a JSON object.");

        return NormalizeJsonObjectAndDeserialize(obj, jsonOptions, out wasUpgraded);
    }

    public static MotherSettings StripLegacyExtensionKeys(MotherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ExtensionData is { Count: > 0 })
        {
            var toRemove = new List<string>();
            foreach (string key in settings.ExtensionData.Keys)
            {
                if (IsLegacyKey(key)) toRemove.Add(key);
            }
            foreach (string key in toRemove)
            {
                settings.ExtensionData.Remove(key);
            }
            if (settings.ExtensionData.Count == 0)
            {
                settings.ExtensionData = null;
            }
        }
        return settings.Normalized();
    }

    private static bool IsLegacyKey(string key)
    {
        foreach (LegacyKeyMapping map in LegacyMappings)
        {
            if (string.Equals(key, map.LegacyKey, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static SettingsMigrationResult MigrateLegacyFileToDefaultConfig(
        string legacyPath,
        string legacyMigratedBakPath,
        string targetPath,
        GranularSettingsManager manager,
        JsonSerializerOptions jsonOptions)
    {
        if ((TryReadAndNormalizeFile(legacyPath, jsonOptions, out MotherSettings? migrated, out _) ||
             TryReadAndNormalizeFile(legacyPath + ".bak", jsonOptions, out migrated, out _)) &&
            migrated is not null)
        {
            manager.SaveAtomically(targetPath, migrated, jsonOptions, recordSnapshot: true);
            File.Move(legacyPath, legacyMigratedBakPath, overwrite: true);
            return new SettingsMigrationResult(true, false, null);
        }

        string corruptBakPath = legacyPath + ".corrupt.bak";
        try
        {
            File.Move(legacyPath, corruptBakPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not move corrupt legacy settings file '{legacyPath}': {ex.Message}");
        }

        return new SettingsMigrationResult(
            false,
            false,
            $"Legacy settings file '{Path.GetFileName(legacyPath)}' was unreadable; initialized clean v2 defaults.");
    }

    private static bool TryReadAndNormalizeFile(
        string path,
        JsonSerializerOptions jsonOptions,
        out MotherSettings? settings,
        out bool wasUpgraded)
    {
        settings = null;
        wasUpgraded = false;
        if (!File.Exists(path)) return false;

        try
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            settings = ParseAndNormalize(raw, jsonOptions, out wasUpgraded);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Trace.TraceWarning($"Settings migration skipped unreadable file '{path}': {ex.Message}");
            return false;
        }
    }

    private static MotherSettings NormalizeJsonObjectAndDeserialize(
        JsonObject obj,
        JsonSerializerOptions jsonOptions,
        out bool wasUpgraded)
    {
        wasUpgraded = false;

        List<string> schemaKeys = FindMatchingKeys(obj, "schemaVersion");
        bool hasValidV2Schema = false;
        foreach (string sk in schemaKeys)
        {
            if (TryReadNodeAsElement(obj[sk], out JsonElement verEl) && TryReadInt(verEl, out int ver) && ver >= 2)
            {
                hasValidV2Schema = true;
            }
        }
        if (!hasValidV2Schema)
        {
            foreach (string sk in schemaKeys) obj.Remove(sk);
            obj["schemaVersion"] = 2;
            wasUpgraded = true;
        }

        foreach (LegacyKeyMapping map in LegacyMappings)
        {
            List<string> legacyKeys = FindMatchingKeys(obj, map.LegacyKey);
            if (legacyKeys.Count == 0) continue;

            wasUpgraded = true;
            List<string> canonicalKeys = FindMatchingKeys(obj, map.CanonicalKey);
            bool hasCanonicalValue = false;
            foreach (string ck in canonicalKeys)
            {
                if (obj[ck] is not null) { hasCanonicalValue = true; break; }
            }

            if (!hasCanonicalValue && TryReadNodeAsElement(obj[legacyKeys[0]], out JsonElement legacyEl))
            {
                JsonNode? coerced = CoerceLegacyValue(legacyEl, map.Kind);
                if (coerced is not null)
                {
                    foreach (string ck in canonicalKeys) obj.Remove(ck);
                    obj[map.CanonicalKey] = coerced;
                }
            }

            foreach (string lk in legacyKeys) obj.Remove(lk);
        }

        MotherSettings deserialized = JsonSerializer.Deserialize<MotherSettings>(obj.ToJsonString(), jsonOptions)
            ?? throw new InvalidDataException("Settings payload could not be parsed.");
        return StripLegacyExtensionKeys(deserialized);
    }

    private static MotherSettings NormalizeLoadedObject(MotherSettings loaded, out bool changed)
    {
        changed = loaded.SchemaVersion < 2;
        if (loaded.ExtensionData is { Count: > 0 })
        {
            foreach (LegacyKeyMapping map in LegacyMappings)
            {
                foreach (var kv in loaded.ExtensionData)
                {
                    if (!string.Equals(kv.Key, map.LegacyKey, StringComparison.OrdinalIgnoreCase)) continue;
                    changed = true;
                    map.ApplyFallback(loaded, kv.Value);
                    break;
                }
            }
        }
        return StripLegacyExtensionKeys(loaded);
    }

    private static List<string> FindMatchingKeys(JsonObject obj, string targetName)
    {
        var matches = new List<string>();
        foreach (KeyValuePair<string, JsonNode?> kv in obj)
        {
            if (string.Equals(kv.Key, targetName, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(kv.Key);
            }
        }
        return matches;
    }

    private static bool TryReadNodeAsElement(JsonNode? node, out JsonElement element)
    {
        if (node is null) { element = default; return false; }
        element = node.Deserialize<JsonElement>();
        return true;
    }

    private static JsonNode? CoerceLegacyValue(JsonElement el, LegacyValueKind kind) => kind switch
    {
        LegacyValueKind.Boolean when TryReadBool(el, out bool b) => JsonValue.Create(b),
        LegacyValueKind.Int32 when TryReadInt(el, out int i) => JsonValue.Create(i),
        _ => null
    };

    private static bool TryReadBool(JsonElement el, out bool result)
    {
        if (el.ValueKind == JsonValueKind.True) { result = true; return true; }
        if (el.ValueKind == JsonValueKind.False) { result = false; return true; }
        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString()?.Trim(), out bool parsed))
        {
            result = parsed;
            return true;
        }
        result = false;
        return false;
    }

    private static bool TryReadInt(JsonElement el, out int result)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int i))
        {
            result = i;
            return true;
        }
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
        {
            result = (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue);
            return true;
        }
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            result = parsed;
            return true;
        }
        result = 0;
        return false;
    }
}
