// @module: Mother.Core.Features.Coexistence.SecretVaultInspector
// @purpose: Read-only DPAPI vault key metadata inspector (exposes key names, file sizes, timestamps only; never decrypts secret plaintext).
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core, Mother.Core.Configuration, Mother.Core.Contracts
// @exports: SecretVaultInspector

using System;
using System.Collections.Generic;
using System.IO;
using Foundation.Core;
using Mother.Core.Configuration;
using Mother.Core.Contracts;

namespace Mother.Core.Features.Coexistence;

public sealed class SecretVaultInspector
{
    private readonly PathResolver _paths;

    public SecretVaultInspector(PathResolver paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<SecretKeyMetadataDto> InspectAll(IEnumerable<SisterAppEntry> apps)
    {
        var results = new List<SecretKeyMetadataDto>();
        InspectVaultDirectory("mother", _paths.DataDirectory, results);

        foreach (SisterAppEntry app in apps)
        {
            if (string.IsNullOrWhiteSpace(app.SecretVaultPath)) continue;
            try
            {
                string fullVault = _paths.AssertContainedIfSandbox(app.SecretVaultPath);
                if (File.Exists(fullVault))
                {
                    string? dir = Path.GetDirectoryName(fullVault);
                    string file = Path.GetFileName(fullVault);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        InspectVaultDirectory(app.AppId, dir, results, file);
                    }
                }
            }
            catch
            {
                /* skip out-of-sandbox paths in test-mode */
            }
        }

        return results;
    }

    private static void InspectVaultDirectory(
        string appId,
        string dir,
        List<SecretKeyMetadataDto> results,
        string fileName = "secrets.dpapi")
    {
        string vaultPath = Path.Combine(dir, fileName);
        if (!File.Exists(vaultPath)) return;

        try
        {
            var info = new FileInfo(vaultPath);
            var store = new DpapiSecretStore(dir, fileName);
            IReadOnlyCollection<string> keys = store.GetSecretKeys();
            string modified = info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            if (keys.Count == 0)
            {
                results.Add(new SecretKeyMetadataDto(vaultPath, appId, "(empty-vault)", info.Length, modified));
                return;
            }
            foreach (string key in keys)
            {
                results.Add(new SecretKeyMetadataDto(vaultPath, appId, key, info.Length, modified));
            }
        }
        catch
        {
            /* fail closed without exposing raw cipher/errors */
        }
    }
}
