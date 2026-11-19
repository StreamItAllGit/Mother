// @module: Mother.Core.Features.Supervision.HealthPollerService
// @purpose: Steady-state background health/status poller for registered sister apps with SQLite fleet_health_events logging.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Net.Http, System.Text.Json, System.Threading, System.Threading.Tasks, Mother.Core.Configuration, Mother.Core.Contracts, Mother.Core.Diagnostics, Mother.Core.Persistence
// @exports: HealthPollerService

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Configuration;
using Mother.Core.Contracts;
using Mother.Core.Diagnostics;
using Mother.Core.Persistence;

namespace Mother.Core.Features.Supervision;

public sealed class HealthPollerService
{
    private readonly SettingsStore _settings;
    private readonly ProcessSupervisor _supervisor;
    private readonly MotherDb _db;
    private readonly MotherDiagnostics _diag;
    private readonly HttpClient _http;
    private readonly Dictionary<string, bool> _lastOnline = new(StringComparer.OrdinalIgnoreCase);

    public HealthPollerService(
        SettingsStore settings,
        ProcessSupervisor supervisor,
        MotherDb db,
        MotherDiagnostics diag,
        HttpClient? httpClient = null)
    {
        _settings = settings;
        _supervisor = supervisor;
        _db = db;
        _diag = diag;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };
    }

    public async Task PollOnceAsync(IEnumerable<SisterAppEntry> manifests, CancellationToken ct = default)
    {
        foreach (SisterAppEntry entry in manifests)
        {
            if (entry.ResidencyMode == "disabled") continue;
            int port = entry.InternalPort;
            if (port <= 0) continue;

            bool online = false;
            int activeJobs = 0;
            int queuedJobs = 0;
            string? err = null;

            try
            {
                using HttpResponseMessage resp = await _http.GetAsync($"http://127.0.0.1:{port}{entry.HealthEndpoint}", ct).ConfigureAwait(false);
                online = resp.IsSuccessStatusCode;
                if (online)
                {
                    string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    TryExtractJobCounts(body, out activeJobs, out queuedJobs);
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }

            _supervisor.UpdateProbeResult(entry.AppId, online, activeJobs, queuedJobs, err);

            bool hadPrev = _lastOnline.TryGetValue(entry.AppId, out bool prevOnline);
            if (!hadPrev || prevOnline != online)
            {
                _lastOnline[entry.AppId] = online;
                await _db.InsertHealthEventAsync(new FleetHealthEventRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SuiteId = "mother",
                    AppId = entry.AppId,
                    Status = online ? "online" : "offline",
                    ResidencyMode = entry.ResidencyMode,
                    IsOnline = online ? 1 : 0,
                    ErrorMessage = online ? null : err,
                    MetadataJson = JsonSerializer.Serialize(new { eventKind = "probe", port }),
                    CreatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                }, ct).ConfigureAwait(false);
            }
        }
    }

    public int GetPollIntervalMs() => _settings.GetCurrent().HealthPollIntervalMs;

    private static void TryExtractJobCounts(string json, out int activeJobs, out int queuedJobs)
    {
        activeJobs = 0;
        queuedJobs = 0;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("activeJobs", out JsonElement a) && a.TryGetInt32(out int av)) activeJobs = av;
            if (doc.RootElement.TryGetProperty("queuedJobs", out JsonElement q) && q.TryGetInt32(out int qv)) queuedJobs = qv;
        }
        catch (JsonException ex)
        {
            // Non-JSON plain-text health endpoints are valid; default job counters to zero.
            Trace.TraceInformation($"Non-JSON health payload observed ({ex.Message}); defaulting job counts to 0.");
        }
    }
}
