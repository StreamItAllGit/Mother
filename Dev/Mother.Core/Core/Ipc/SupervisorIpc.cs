// @module: Mother.Core.Ipc.SupervisorIpc
// @purpose: Single-instance + Supervisor active mutexes and named-pipe command bridge for focusing or switching tabs.
// @depends-on: System, System.Diagnostics, System.IO, System.IO.Pipes, System.Text, System.Threading, System.Threading.Tasks
// @exports: SupervisorIpc

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mother.Core.Configuration;

namespace Mother.Core.Ipc;

public sealed class SupervisorIpc : IDisposable
{
    private readonly Mutex _singleInstanceMutex;
    private readonly Mutex? _supervisorActiveMutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;
    private bool _disposed;

    public bool IsPrimaryInstance { get; }
    public string SingleInstanceMutexName { get; }
    public string SupervisorActiveMutexName { get; }

    public event Action<string>? CommandReceived;

    public SupervisorIpc(PathResolver paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.ShouldSuppressLiveHostMutations)
        {
            string scope = paths.IsSandboxActive ? "Sandbox" : "Isolated";
            string hash = paths.ComputeIsolatedMutexHash();
            SingleInstanceMutexName = $@"Local\Mother.{scope}.{hash}";
            SupervisorActiveMutexName = $@"Local\Mother.{scope}.Supervisor.{hash}";
            _pipeName = $"Mother.{scope}.Pipe.{hash}";
        }
        else
        {
            SingleInstanceMutexName = @"Global\Mother_SingleInstance_Mutex";
            SupervisorActiveMutexName = @"Global\Mother_Supervisor_Active";
            _pipeName = "Mother.Suite.Daemon.Pipe";
        }

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool created);
        IsPrimaryInstance = created;
        if (IsPrimaryInstance)
        {
            _supervisorActiveMutex = new Mutex(initiallyOwned: true, SupervisorActiveMutexName, out _);
        }
    }

    public void StartCommandListener()
    {
        if (!IsPrimaryInstance || _listenerTask is not null)
        {
            return;
        }
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
    }

    public bool TrySendCommandToPrimary(string command, int timeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            byte[] bytes = Encoding.UTF8.GetBytes((command ?? string.Empty).Trim() + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Primary instance pipe may have closed or timed out before accepting command.
            Trace.TraceWarning($"IPC command send to '{_pipeName}' failed: {ex.Message}");
            return false;
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    CommandReceived?.Invoke(line.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Transient broken pipe or client disconnect; back off briefly before recreating listener.
                Trace.TraceWarning($"IPC listener error on '{_pipeName}': {ex.Message}");
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _cts.Cancel();
        try
        {
            _listenerTask?.Wait(500);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation when shutting down IPC pipe listener.
        }
        catch (AggregateException ex)
        {
            Trace.TraceWarning($"IPC listener shutdown warning: {ex.GetBaseException().Message}");
        }
        _cts.Dispose();

        if (IsPrimaryInstance)
        {
            try
            {
                _supervisorActiveMutex?.ReleaseMutex();
            }
            catch (Exception ex) when (ex is ApplicationException or ObjectDisposedException)
            {
                // Mutex may have been released on a different thread or already disposed during teardown.
                Trace.TraceWarning($"Supervisor mutex release note: {ex.Message}");
            }
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (Exception ex) when (ex is ApplicationException or ObjectDisposedException)
            {
                // Mutex may have been released on a different thread or already disposed during teardown.
                Trace.TraceWarning($"Single-instance mutex release note: {ex.Message}");
            }
        }
        _supervisorActiveMutex?.Dispose();
        _singleInstanceMutex.Dispose();
    }
}
