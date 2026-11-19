// @module: Mother.Core.Features.Discovery.ManifestModels
// @purpose: Dual-schema (v1 snake_case + v2 camelCase) sister-app manifest normalizer.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text.Json, Mother.Core.Configuration, Mother.Core.Contracts
// @exports: ManifestParser

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mother.Core.Configuration;
using Mother.Core.Contracts;

namespace Mother.Core.Features.Discovery;

public static class ManifestParser
{
    public static SisterAppEntry? Parse(string filePath, string jsonText)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonText);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? appId = FirstString(root, "appId", "app_id", "id");
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }
        appId = appId.Trim().ToLowerInvariant();
        if (appId == "mother")
        {
            return null;
        }

        int schemaVer = FirstInt(root, "schemaVersion", "schema_version") ?? (root.TryGetProperty("app_id", out _) ? 1 : 2);
        string name = FirstString(root, "name", "displayName", "display_name", "shortName") ?? appId;
        string version = FirstString(root, "version") ?? "0.1.0";
        string minSuite = FirstString(root, "minSuiteVersion", "min_suite_version") ?? "0.1.0";
        string sdkVer = FirstString(root, "sdkVersion", "sdk_version") ?? MotherBuildConstants.SdkVersion;
        string accent = FirstString(root, "brandAccent", "brand_accent", "brand_color", "accentColor") ?? "#38bdf8";

        int rawPort = FirstInt(root, "internalPort", "internal_port", "port") ?? 0;
        int internalPort = rawPort is > 0 and <= 65535 ? rawPort : 0;

        string installDir = FirstString(root, "installDir", "install_dir") ?? Path.GetDirectoryName(filePath) ?? string.Empty;
        string executable = FirstString(root, "executable", "launcherExecutable", "executablePath") ?? string.Empty;

        string residencyMode = "warm";
        int idleMins = 15;
        bool wakeApi = true;
        bool wakeUi = true;

        if (root.TryGetProperty("residency", out JsonElement resObj) && resObj.ValueKind == JsonValueKind.Object)
        {
            residencyMode = FirstString(resObj, "defaultMode", "mode") ?? residencyMode;
            idleMins = FirstInt(resObj, "idleTimeoutMinutes", "idle_timeout_minutes") ?? idleMins;
            wakeApi = FirstBool(resObj, "wakeOnApiRequest", "wake_on_api_request") ?? wakeApi;
            wakeUi = FirstBool(resObj, "wakeOnUiNavigation", "wake_on_ui_navigation") ?? wakeUi;
        }
        else
        {
            residencyMode = FirstString(root, "residencyMode", "residency_mode") ?? residencyMode;
        }

        idleMins = FirstInt(root, "idleTimeoutMinutes", "idle_timeout_minutes") ?? idleMins;
        wakeApi = FirstBool(root, "wakeOnApiRequest", "wake_on_api_request") ?? wakeApi;
        wakeUi = FirstBool(root, "wakeOnUiNavigation", "wake_on_ui_navigation") ?? wakeUi;

        string healthEp = "/api/v1/health";
        if (root.TryGetProperty("endpoints", out JsonElement epObj) && epObj.ValueKind == JsonValueKind.Object)
        {
            healthEp = FirstString(epObj, "health") ?? healthEp;
        }
        else
        {
            healthEp = FirstString(root, "healthEndpoint", "health_endpoint") ?? healthEp;
        }

        string normResidency = residencyMode.Trim().ToLowerInvariant() switch
        {
            "warm" or "always-on" => "warm",
            "cold" or "on-demand" => "cold",
            "disabled" => "disabled",
            _ => "warm"
        };

        return new SisterAppEntry
        {
            SchemaVersion = schemaVer,
            AppId = appId,
            Name = name,
            Version = version,
            MinSuiteVersion = minSuite,
            SdkVersion = sdkVer,
            BrandAccent = accent,
            InstallDir = installDir,
            Executable = executable,
            WorkerArgs = ReadStringArray(root, "workerArgs", "worker_args"),
            InternalPort = internalPort,
            ResidencyMode = normResidency,
            IdleTimeoutMinutes = Math.Clamp(idleMins, 1, 1440),
            WakeOnApiRequest = wakeApi,
            WakeOnUiNavigation = wakeUi,
            HealthEndpoint = healthEp,
            Capabilities = ReadStringArray(root, "capabilities"),
            SharedMounts = ReadStringArray(root, "sharedMounts", "shared_mounts"),
            MutuallyExclusiveWith = ReadStringArray(root, "mutuallyExclusiveWith", "mutually_exclusive_with"),
            TargetDrives = ReadStringArray(root, "targetDrives", "target_drives"),
            Schedules = ReadSchedules(root),
            Commands = ReadCommands(root),
            SecretVaultPath = FirstString(root, "secretVaultPath", "secret_vault_path"),
            ManifestSourcePath = filePath,
            DetectedSchemaFormat = schemaVer >= 2 ? "v2" : "v1"
        };
    }

    private static IReadOnlyList<SisterScheduleEntry> ReadSchedules(JsonElement root)
    {
        if (!root.TryGetProperty("schedules", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<SisterScheduleEntry>();

        var list = new List<SisterScheduleEntry>();
        int idx = 0;
        foreach (JsonElement item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            idx++;
            string schedId = FirstString(item, "scheduleId", "schedule_id", "id") ?? $"schedule-{idx}";
            string name = FirstString(item, "name", "label") ?? schedId;
            string cadence = FirstString(item, "cadence") ?? "daily";
            string timeUtc = FirstString(item, "timeUtc", "time_utc") ?? "03:00";
            bool runIfMissed = FirstBool(item, "runIfMissed", "run_if_missed") ?? true;
            IReadOnlyList<string> drives = ReadStringArray(item, "targetDrives", "target_drives");
            string triggerEp = FirstString(item, "triggerEndpoint", "trigger_endpoint") ?? "/api/v1/health";
            list.Add(new SisterScheduleEntry(schedId, name, cadence, timeUtc, runIfMissed, drives, triggerEp));
        }
        return list;
    }

    private static SisterCommandsEntry? ReadCommands(JsonElement root)
    {
        if (!root.TryGetProperty("commands", out JsonElement obj) || obj.ValueKind != JsonValueKind.Object)
            return null;

        return new SisterCommandsEntry(
            StartDaemon: FirstString(obj, "startDaemon", "start_daemon"),
            Start: FirstString(obj, "start"),
            StopDaemon: FirstString(obj, "stopDaemon", "stop_daemon"),
            StatusCheck: FirstString(obj, "statusCheck", "status_check"),
            BackupNow: FirstString(obj, "backupNow", "backup_now"));
    }

    private static string? FirstString(JsonElement obj, params string[] keys)
    {
        foreach (string k in keys)
        {
            if (obj.TryGetProperty(k, out JsonElement val) && val.ValueKind == JsonValueKind.String)
            {
                string? s = val.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        }
        return null;
    }

    private static int? FirstInt(JsonElement obj, params string[] keys)
    {
        foreach (string k in keys)
        {
            if (obj.TryGetProperty(k, out JsonElement val) && val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int num))
            {
                return num;
            }
        }
        return null;
    }

    private static bool? FirstBool(JsonElement obj, params string[] keys)
    {
        foreach (string k in keys)
        {
            if (obj.TryGetProperty(k, out JsonElement val) && (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False))
            {
                return val.GetBoolean();
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement obj, params string[] keys)
    {
        foreach (string k in keys)
        {
            if (obj.TryGetProperty(k, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
            {
                return arr.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                    .Select(x => x.GetString()!.Trim())
                    .ToArray();
            }
        }
        return Array.Empty<string>();
    }
}
