// @module: Mother.Core.Features.Discovery.ManifestModels
// @purpose: Canonical sister-app port defaults and dual-schema (v1 snake_case + v2 camelCase) manifest normalizer.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text.Json, Mother.Core.Contracts
// @exports: CanonicalSisterDefaults, ManifestParser

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mother.Core.Contracts;

namespace Mother.Core.Features.Discovery;

public static class CanonicalSisterDefaults
{
    public static readonly IReadOnlyDictionary<string, (string Name, int ProdPort, int SandboxPort, string Accent)> KnownApps =
        new Dictionary<string, (string, int, int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["housekeeping"] = ("Housekeeping", 5178, 15178, "#f59e0b"),
            ["shadowlibrary"] = ("ShadowLibrary", 5179, 15179, "#6366f1"),
            ["redroom"] = ("Redroom", 5180, 15180, "#ef4444"),
            ["greenroom"] = ("Greenroom", 8420, 15420, "#10b981"),
            ["streamengine"] = ("StreamEngine", 8430, 15430, "#8b5cf6"),
            ["legacy-sister-app"] = ("legacy-sister-app", 8460, 15460, "#06b6d4")
        };
}

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

        CanonicalSisterDefaults.KnownApps.TryGetValue(appId, out var known);
        int schemaVer = FirstInt(root, "schemaVersion", "schema_version") ?? (root.TryGetProperty("app_id", out _) ? 1 : 2);
        string name = FirstString(root, "name", "displayName", "display_name", "shortName") ?? known.Name ?? appId;
        string version = FirstString(root, "version") ?? "0.1.0";
        string minSuite = FirstString(root, "minSuiteVersion", "min_suite_version") ?? "0.1.0";
        string sdkVer = FirstString(root, "sdkVersion", "sdk_version") ?? "0.3.0";
        string accent = FirstString(root, "brandAccent", "brand_accent", "brand_color", "accentColor") ?? known.Accent ?? "#38bdf8";

        int internalPort = FirstInt(root, "internalPort", "internal_port", "port") ?? known.ProdPort;
        int sandboxPort = FirstInt(root, "sandboxPort", "sandbox_port") ?? known.SandboxPort;
        if (sandboxPort <= 0 && internalPort > 0)
        {
            sandboxPort = internalPort + 10000;
        }

        string installDir = FirstString(root, "installDir", "install_dir") ?? Path.GetDirectoryName(filePath) ?? string.Empty;
        string executable = FirstString(root, "executable", "launcherExecutable", "executablePath") ?? string.Empty;

        string residencyMode = "warm";
        int idleMins = 15;
        bool wakeApi = true;
        bool wakeUi = true;

        if (root.TryGetProperty("residency", out JsonElement resObj) && resObj.ValueKind == JsonValueKind.Object)
        {
            residencyMode = FirstString(resObj, "defaultMode", "mode") ?? residencyMode;
            idleMins = FirstInt(resObj, "idleTimeoutMinutes") ?? idleMins;
            wakeApi = FirstBool(resObj, "wakeOnApiRequest") ?? wakeApi;
            wakeUi = FirstBool(resObj, "wakeOnUiNavigation") ?? wakeUi;
        }
        else
        {
            residencyMode = FirstString(root, "residencyMode", "residency_mode") ?? residencyMode;
        }

        string healthEp = "/api/v1/health";
        if (root.TryGetProperty("endpoints", out JsonElement epObj) && epObj.ValueKind == JsonValueKind.Object)
        {
            healthEp = FirstString(epObj, "health") ?? healthEp;
        }
        else
        {
            healthEp = FirstString(root, "healthEndpoint", "health_endpoint") ?? healthEp;
        }

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
            SandboxPort = sandboxPort,
            ResidencyMode = residencyMode.ToLowerInvariant() is "warm" or "cold" or "disabled" ? residencyMode.ToLowerInvariant() : "warm",
            IdleTimeoutMinutes = Math.Clamp(idleMins, 1, 1440),
            WakeOnApiRequest = wakeApi,
            WakeOnUiNavigation = wakeUi,
            HealthEndpoint = healthEp,
            Capabilities = ReadStringArray(root, "capabilities"),
            SharedMounts = ReadStringArray(root, "sharedMounts", "shared_mounts"),
            MutuallyExclusiveWith = ReadStringArray(root, "mutuallyExclusiveWith", "mutually_exclusive_with"),
            TargetDrives = ReadStringArray(root, "targetDrives", "target_drives"),
            SecretVaultPath = FirstString(root, "secretVaultPath", "secret_vault_path"),
            ManifestSourcePath = filePath,
            DetectedSchemaFormat = schemaVer >= 2 ? "v2" : "v1"
        };
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
