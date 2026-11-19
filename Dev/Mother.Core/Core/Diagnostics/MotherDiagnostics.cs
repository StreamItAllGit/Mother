// @module: Mother.Core.Diagnostics.MotherDiagnostics
// @purpose: Structured daily JSONL rolling logger, <=15s HeartbeatLogger factory, crash bundle hooks, and 250-line terminal ring buffer.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json, Foundation.Core
// @exports: MotherDiagnostics

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Foundation.Core;
using Mother.Core.Configuration;
using Mother.Core.Persistence;

namespace Mother.Core.Diagnostics;

public sealed class MotherDiagnostics
{
    private const int RingBufferCapacity = 250;
    private readonly object _sync = new();
    private readonly Queue<TerminalLogEntryDto> _ring = new(RingBufferCapacity);
    private readonly string _logDir;
    private readonly string _version;
    private long _seq;

    public string LogDirectory => _logDir;
    public event Action<TerminalLogEntryDto>? LogLineEmitted;

    public MotherDiagnostics(PathResolver paths, string version)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _logDir = paths.LogDirectory;
        _version = version;
        Directory.CreateDirectory(_logDir);
    }

    public void RegisterGlobalCrashHandlers(Func<object?>? activeJobsProvider = null)
    {
        CrashBundleService.RegisterGlobalHandlers(
            _logDir,
            "mother",
            _version,
            () => GetRecentLines(50).Select(l => $"[{l.TimestampUtc}] [{l.Level}] [{l.AppId}/{l.Slice}] {l.Message}"),
            activeJobsProvider);
    }

    public string? WriteCrashBundle(Exception ex, object? activeJobs = null) =>
        CrashBundleService.TryWriteCrashBundle(
            _logDir,
            "mother",
            _version,
            ex,
            GetRecentLines(50).Select(l => $"[{l.TimestampUtc}] [{l.Level}] [{l.AppId}/{l.Slice}] {l.Message}"),
            activeJobs);

    public HeartbeatLogger CreateHeartbeat(string operationName, string slice = "Supervisor") =>
        new(
            operationName,
            logSink: line => Log("HEARTBEAT", "mother", slice, StripLeadingHeartbeatTag(line)),
            terminalWriter: Console.Out,
            heartbeatInterval: TimeSpan.FromSeconds(15));

    public static string StripLeadingHeartbeatTag(string? line)
    {
        if (string.IsNullOrEmpty(line)) return string.Empty;
        ReadOnlySpan<char> span = line.AsSpan().TrimStart();
        const string prefix = "[HEARTBEAT]";
        while (span.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            span = span[prefix.Length..].TrimStart();
        }
        return span.ToString();
    }

    public TerminalLogEntryDto Log(string level, string appId, string slice, string message)
    {
        string normLevel = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
        string normApp = string.IsNullOrWhiteSpace(appId) ? "mother" : appId.Trim();
        string normSlice = string.IsNullOrWhiteSpace(slice) ? "Core" : slice.Trim();
        string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        long seq = Interlocked.Increment(ref _seq);

        var entry = new TerminalLogEntryDto(seq, nowUtc, normApp, normLevel, normSlice, message ?? string.Empty);

        lock (_sync)
        {
            if (_ring.Count >= RingBufferCapacity)
            {
                _ring.Dequeue();
            }
            _ring.Enqueue(entry);
            AppendJsonlLineUnsynchronized(entry);
        }

        LogLineEmitted?.Invoke(entry);
        return entry;
    }

    public IReadOnlyList<TerminalLogEntryDto> GetRecentLines(int limit = 250)
    {
        lock (_sync)
        {
            return _ring.TakeLast(Math.Clamp(limit, 1, RingBufferCapacity)).ToList();
        }
    }

    public void ClearBuffer()
    {
        lock (_sync)
        {
            _ring.Clear();
        }
    }

    public int CountCrashBundles()
    {
        string crashesDir = Path.Combine(_logDir, "crashes");
        if (!Directory.Exists(crashesDir))
        {
            return 0;
        }
        return Directory.GetFiles(crashesDir, "crash_*.json", SearchOption.TopDirectoryOnly).Length;
    }

    private void AppendJsonlLineUnsynchronized(TerminalLogEntryDto entry)
    {
        try
        {
            string file = Path.Combine(_logDir, $"mother-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            string line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            File.AppendAllText(file, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging failure must never crash runtime; surface to debug trace only.
            System.Diagnostics.Trace.TraceWarning($"MotherDiagnostics JSONL append skipped: {ex.Message}");
        }
    }
}
