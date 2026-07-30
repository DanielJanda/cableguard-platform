using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public interface IHttpProber
{
    /// <summary>Returns the HTTP status code, or null when the endpoint is unreachable.</summary>
    Task<int?> GetStatusCodeAsync(string url, CancellationToken ct = default);
    Task<int?> OptionsStatusCodeAsync(string url, CancellationToken ct = default);
    Task<string?> GetBodyAsync(string url, CancellationToken ct = default);
}

/// <summary>Windows service facts needed to tell "service running" apart from "component healthy".</summary>
public sealed record WindowsServiceInfo(string Name, string State, string StartMode, bool DelayedAutoStart)
{
    public bool IsRunning => State.Equals("Running", StringComparison.OrdinalIgnoreCase);

    public string StartupDescription => DelayedAutoStart ? "Automatic (Delayed Start)" : StartMode;
}

public interface IProcessInspector
{
    /// <summary>Reads a PID file and returns the PID when that process is still alive.</summary>
    int? GetAlivePidFromFile(string pidFilePath);

    /// <summary>Service facts, or null when the service is not installed on this machine.</summary>
    WindowsServiceInfo? GetWindowsService(string serviceName);
    bool IsPortListening(int port);
    /// <summary>Finds a process whose command line contains the hint (used for the detector).</summary>
    int? FindProcessByCommandLineHint(string hint);
    /// <summary>Finds first alive process by ProcessName (e.g. "mediamtx"), ignoring extension.</summary>
    int? FindProcessByName(string processName);
    /// <summary>All alive PIDs matching ProcessName (used to refuse multiple MediaMTX instances).</summary>
    IReadOnlyList<int> FindAllProcessIdsByName(string processName);
    /// <summary>Kills a process tree; verifies the process name matches the expectation first.</summary>
    bool KillProcessTree(int pid, string expectedNameFragment, out string message);
}

public interface IScriptRunner
{
    /// <summary>Runs a PowerShell script, streams redacted output to the Control Center log, returns exit code + tail of output.</summary>
    Task<(int ExitCode, string Output)> RunAsync(string scriptPath, CancellationToken ct = default);
}

public interface IComponentController
{
    ComponentId Id { get; }
    string DisplayName { get; }
    bool IsConfigured { get; }
    Task<ComponentSnapshot> GetStatusAsync(CancellationToken ct = default);
    Task<StartStepResult> StartAsync(CancellationToken ct = default);
    Task<StartStepResult> StopAsync(CancellationToken ct = default);
    /// <summary>Path of the primary log file for the Logs tab (may not exist yet).</summary>
    string LogFilePath { get; }
}

public interface IMediaMtxApi
{
    /// <summary>GET /v3/paths/list — true when Control API on 127.0.0.1:9997 responds.</summary>
    Task<bool?> IsControlApiReadyAsync(CancellationToken ct = default);
    Task<bool?> IsPathReadyAsync(string pathName, CancellationToken ct = default);
    Task<string?> GetConfiguredSourceAsync(string pathName, CancellationToken ct = default);
    Task<bool> PatchPathSourceAsync(string pathName, string source, CancellationToken ct = default);
    /// <summary>GET /v3/config/paths/get/{name} — true when path conf exists.</summary>
    Task<bool?> ConfigPathExistsAsync(string pathName, CancellationToken ct = default);
    /// <summary>GET /v3/config/paths/get/{name} → record flag (null if API unreachable).</summary>
    Task<bool?> IsPathRecordingEnabledAsync(string pathName, CancellationToken ct = default);
    /// <summary>POST /v3/config/paths/add/{name} (MediaMTX v1.11+).</summary>
    Task<bool> AddPathAsync(string pathName, string source, string? rtspTransport = "tcp", CancellationToken ct = default);
    /// <summary>DELETE /v3/config/paths/delete/{name}.</summary>
    Task<bool> DeletePathAsync(string pathName, CancellationToken ct = default);
}

public interface IMediaMtxConfigPersister
{
    /// <summary>Persists a new source for a path in the gitignored local yml so restarts keep the switch.</summary>
    bool PersistPathSource(string pathName, string newSource, out string message);
    /// <summary>Create or replace a full path block under paths:.</summary>
    bool UpsertPath(string pathName, string source, string? rtspTransport, out string message);
    /// <summary>Remove a path block from local yml.</summary>
    bool RemovePath(string pathName, out string message);
}

public interface ICredentialStore
{
    bool TryRead(string credentialRef, out string username, out string password);
    bool Write(string credentialRef, string username, string password);
    bool Delete(string credentialRef);
}
