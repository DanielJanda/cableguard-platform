using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Generic component controller: status from pluggable probes, start via existing
/// PowerShell scripts, stop via managed PID file (with process-name safety check).
/// </summary>
public sealed class ServiceComponent : IComponentController
{
    private readonly Func<CancellationToken, Task<ComponentSnapshot>> _statusFunc;
    private readonly Func<CancellationToken, Task<StartStepResult>> _startFunc;
    private readonly Func<CancellationToken, Task<StartStepResult>> _stopFunc;

    public ServiceComponent(
        ComponentId id,
        string displayName,
        bool isConfigured,
        string logFilePath,
        Func<CancellationToken, Task<ComponentSnapshot>> statusFunc,
        Func<CancellationToken, Task<StartStepResult>> startFunc,
        Func<CancellationToken, Task<StartStepResult>> stopFunc)
    {
        Id = id;
        DisplayName = displayName;
        IsConfigured = isConfigured;
        LogFilePath = logFilePath;
        _statusFunc = statusFunc;
        _startFunc = startFunc;
        _stopFunc = stopFunc;
    }

    public ComponentId Id { get; }
    public string DisplayName { get; }
    public bool IsConfigured { get; }
    public string LogFilePath { get; }

    public Task<ComponentSnapshot> GetStatusAsync(CancellationToken ct = default) => _statusFunc(ct);
    public Task<StartStepResult> StartAsync(CancellationToken ct = default) => _startFunc(ct);
    public Task<StartStepResult> StopAsync(CancellationToken ct = default) => _stopFunc(ct);
}

/// <summary>Builds the four CableGuard components wired to the real scripts, PID files and health endpoints.</summary>
public sealed class ComponentFactory
{
    private readonly ControlCenterConfig _config;
    private readonly IProcessInspector _processes;
    private readonly IHttpProber _prober;
    private readonly IScriptRunner _scripts;
    private readonly IMediaMtxApi _mediaMtxApi;
    private readonly DetectorProcessManager? _detectors;

    public ComponentFactory(
        ControlCenterConfig config,
        IProcessInspector processes,
        IHttpProber prober,
        IScriptRunner scripts,
        IMediaMtxApi mediaMtxApi,
        DetectorProcessManager? detectors = null)
    {
        _config = config;
        _processes = processes;
        _prober = prober;
        _scripts = scripts;
        _mediaMtxApi = mediaMtxApi;
        _detectors = detectors;
    }

    public IReadOnlyList<IComponentController> CreateAllInStartOrder() => new[]
    {
        CreateMediaMtx(),
        CreateEventCore(),
        CreateMonitor(),
        CreateDetector(),
    };

    /// <summary>Windows service that owns MediaMTX in the permanent local deployment.</summary>
    public const string MediaMtxServiceName = "CableGuardMediaMTX";

    public IComponentController CreateMediaMtx()
    {
        var pidFile = Path.Combine(_config.PlatformRoot, "runtime", "mediamtx", "mediamtx.pid");
        var logFile = Path.Combine(_config.PlatformRoot, "runtime", "mediamtx", "mediamtx.out.log");
        var startScript = Path.Combine(_config.ScriptsDir, "start_mediamtx.ps1");
        var stopScript = Path.Combine(_config.ScriptsDir, "stop_mediamtx.ps1");
        var whepUrl = $"{_config.WhepBaseLocal}/{_config.ProductionStream}/whep";

        return new ServiceComponent(
            ComponentId.MediaMtx, "MediaMTX", isConfigured: true, logFile,
            statusFunc: async ct =>
            {
                var service = _processes.GetWindowsService(MediaMtxServiceName);
                var serviceText = service is null
                    ? "service=NOT INSTALLED"
                    : $"service={service.State.ToUpperInvariant()} ({service.StartupDescription})";

                var live = _processes.FindAllProcessIdsByName("mediamtx");
                if (live.Count > 1)
                {
                    return new ComponentSnapshot(
                        ComponentId.MediaMtx,
                        ComponentStatus.Fault,
                        $"{serviceText} · process=FAULT: {live.Count} instances (PIDs {string.Join(", ", live)}). Stop extras before continuing.",
                        live[0]);
                }

                var pidFilePid = _processes.GetAlivePidFromFile(pidFile);
                var decision = MediaMtxLifecycleLogic.Decide(
                    pidFilePid,
                    pidFilePid is not null,
                    live,
                    portOwnerPid: null,
                    portOwnerIsMediaMtx: false);

                int? pid = decision.PidToAdopt ?? pidFilePid ?? (live.Count == 1 ? live[0] : null);
                if (pid is not null)
                    TryHealPidFile(pidFile, pid.Value);

                var portUp = _processes.IsPortListening(8889) || _processes.IsPortListening(8554);
                var processAlive = pid is not null || portUp;
                var processText = processAlive
                    ? $"process=RUNNING (PID {pid?.ToString() ?? "?"})"
                    : "process=STOPPED";

                var controlApiReady = processAlive && await _mediaMtxApi.IsControlApiReadyAsync(ct) == true;
                var controlApiText = controlApiReady ? "control-api=READY" : "control-api=OFFLINE";

                var whepStatus = processAlive ? await _prober.OptionsStatusCodeAsync(whepUrl, ct) : null;
                var whepReady = whepStatus is >= 200 and < 300;

                var expected = ExpectedMediaMtxPaths();
                var notReady = new List<string>();
                foreach (var path in expected)
                {
                    var ready = processAlive && await _mediaMtxApi.IsPathReadyAsync(path, ct) == true;
                    if (!ready) notReady.Add(path);
                }
                var pathsReady = expected.Count > 0 && notReady.Count == 0;
                var pathsText = expected.Count == 0
                    ? "paths=NONE CONFIGURED"
                    : pathsReady
                        ? $"paths=READY ({expected.Count}/{expected.Count})"
                        : $"paths=NOT READY ({expected.Count - notReady.Count}/{expected.Count}: {string.Join(", ", notReady)})";

                var serviceRunning = service?.IsRunning == true;
                var probes = new ProbeResults(
                    ProcessAlive: processAlive,
                    WhepReachable: whepReady,
                    PathReady: pathsReady,
                    ServiceRunning: service is null ? false : serviceRunning,
                    ControlApiReady: controlApiReady);
                var status = StatusEvaluators.EvaluateMediaMtx(probes);

                var detail = string.Join(
                    " · ",
                    serviceText,
                    processText,
                    controlApiText,
                    pathsText);
                return new ComponentSnapshot(ComponentId.MediaMtx, status, detail, pid);
            },
            startFunc: ct => RunScriptAsStep(ComponentId.MediaMtx, startScript, ct),
            stopFunc: ct => RunScriptAsStep(ComponentId.MediaMtx, stopScript, ct));
    }

    /// <summary>MediaMTX paths that must be READY: every enabled logical stream.</summary>
    private IReadOnlyList<string> ExpectedMediaMtxPaths()
    {
        try
        {
            var streams = StreamsService.Load(_config.StreamsJsonPath).Streams
                .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.MediaMtxPath))
                .Select(s => s.MediaMtxPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return streams.Count > 0 ? streams : new List<string> { _config.ProductionStream };
        }
        catch (JsonException)
        {
            return new List<string> { _config.ProductionStream };
        }
    }

    public IComponentController CreateEventCore()
    {
        var pidFile = Path.Combine(_config.PlatformRoot, "runtime", "event-core", "event-core.pid");
        var logFile = Path.Combine(_config.PlatformRoot, "runtime", "event-core", "event-core.err.log");
        var startScript = Path.Combine(_config.ScriptsDir, "start_internal_event_core.ps1");
        var healthUrl = $"{_config.EventCoreBaseLocal}/api/v1/health";

        return new ServiceComponent(
            ComponentId.EventCore, "Event Core", isConfigured: true, logFile,
            statusFunc: async ct =>
            {
                var pid = _processes.GetAlivePidFromFile(pidFile);
                var processAlive = pid is not null || _processes.IsPortListening(8000);
                var healthBody = await _prober.GetBodyAsync(healthUrl, ct);
                var healthy = healthBody?.Contains("\"ok\"") == true && healthBody.Contains("true");
                var status = StatusEvaluators.EvaluateEventCore(new ProbeResults(processAlive, HttpHealthy: healthy));
                var detail = status switch
                {
                    ComponentStatus.Running => "/api/v1/health OK",
                    ComponentStatus.Fault => "Process/port up but /api/v1/health failing",
                    _ => "Not running",
                };
                return new ComponentSnapshot(ComponentId.EventCore, status, detail, pid);
            },
            startFunc: ct => RunScriptAsStep(ComponentId.EventCore, startScript, ct),
            stopFunc: ct => StopByPidFile(ComponentId.EventCore, pidFile, "python", ct));
    }

    public IComponentController CreateMonitor()
    {
        var pidFile = Path.Combine(_config.MonitorRoot, "runtime", "monitor.pid");
        var logFile = Path.Combine(_config.MonitorRoot, "runtime", "monitor.out.log");
        var startScript = Path.Combine(_config.MonitorRoot, "scripts", "start_internal_monitor.ps1");
        var httpUrl = $"{_config.MonitorBaseLocal}/";

        return new ServiceComponent(
            ComponentId.Monitor, "Monitor", isConfigured: true, logFile,
            statusFunc: async ct =>
            {
                var pid = _processes.GetAlivePidFromFile(pidFile);
                var processAlive = pid is not null || _processes.IsPortListening(8080);
                var httpStatus = await _prober.GetStatusCodeAsync(httpUrl, ct);
                var status = StatusEvaluators.EvaluateMonitor(
                    new ProbeResults(processAlive, HttpHealthy: httpStatus is >= 200 and < 500));
                var detail = status switch
                {
                    ComponentStatus.Running => $"HTTP :8080 responding ({httpStatus})",
                    ComponentStatus.Fault => "Process up but HTTP :8080 not responding",
                    _ => "Not running",
                };
                return new ComponentSnapshot(ComponentId.Monitor, status, detail, pid);
            },
            startFunc: ct => RunScriptAsStep(ComponentId.Monitor, startScript, ct),
            stopFunc: ct => StopByPidFile(ComponentId.Monitor, pidFile, "", ct)); // npm.cmd tree: cmd→node
    }

    public IComponentController CreateDetector()
    {
        var primary = ResolvePrimaryFallInstance();
        var scriptPath = primary is null
            ? ""
            : Path.Combine(_config.DetectorRoot, primary.ScriptRelative.Replace('/', Path.DirectorySeparatorChar));
        var configured = primary is not null && File.Exists(scriptPath);
        var logFile = Path.Combine(_config.LogsDir, "detectors",
            (primary?.Id ?? "fall-zahradky-upper") + ".err.log");

        return new ServiceComponent(
            ComponentId.Detector, "Detektor pádu", configured, logFile,
            statusFunc: ct =>
            {
                if (!configured || primary is null)
                    return Task.FromResult(ComponentSnapshot.NotConfigured(
                        ComponentId.Detector,
                        "NOT CONFIGURED — apps/zahradky_horni_pad.py not found (need detector feature/mediamtx-input-profile or later)"));

                var pid = _detectors?.FindPid(primary)
                          ?? _processes.FindProcessByCommandLineHint(primary.ProcessHint);
                var status = StatusEvaluators.EvaluateDetector(
                    new ProbeResults(pid is not null, DeepHealthAvailable: false));
                var detail = pid is not null
                    ? $"RUNNING PID {pid} · stream={primary.InputStream} · deep health NOT AVAILABLE"
                    : $"Stopped · instance={primary.Id} · stream={primary.InputStream}";
                return Task.FromResult(new ComponentSnapshot(ComponentId.Detector, status, detail, pid));
            },
            startFunc: async ct =>
            {
                if (!configured || primary is null || _detectors is null)
                    return new StartStepResult(ComponentId.Detector, false, "Detector not configured.");
                var (ok, msg) = await _detectors.StartAsync(primary, ct: ct);
                return new StartStepResult(ComponentId.Detector, ok, msg);
            },
            stopFunc: ct =>
            {
                if (primary is null || _detectors is null)
                    return Task.FromResult(new StartStepResult(ComponentId.Detector, true, "Not configured."));
                var (ok, msg) = _detectors.Stop(primary);
                return Task.FromResult(new StartStepResult(ComponentId.Detector, ok, msg));
            });
    }

    private DetectorInstance? ResolvePrimaryFallInstance()
    {
        var doc = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var fall = doc.Instances.FirstOrDefault(i =>
            i.DetectorType.Equals("fall", StringComparison.OrdinalIgnoreCase) && i.Enabled);
        if (fall is not null) return fall;

        // Defaults when detectors.json not seeded yet.
        var script = Path.Combine(_config.DetectorRoot, "apps", "zahradky_horni_pad.py");
        if (!File.Exists(script)) return null;
        return new DetectorInstance
        {
            Id = "fall-zahradky-upper",
            DisplayName = "Detektor pádu – Zahrádky horní",
            DetectorType = "fall",
            InputStream = _config.ProductionStream,
            ScriptRelative = "apps/zahradky_horni_pad.py",
            ProcessHint = "zahradky_horni_pad",
            Enabled = true,
        };
    }

    private async Task<StartStepResult> RunScriptAsStep(ComponentId id, string scriptPath, CancellationToken ct)
    {
        var (exitCode, output) = await _scripts.RunAsync(scriptPath, ct);
        var tail = output.Length > 400 ? output[^400..] : output;
        return new StartStepResult(id, exitCode == 0, exitCode == 0 ? "Script OK." : $"Exit {exitCode}: {tail.Trim()}");
    }

    private Task<StartStepResult> StopByPidFile(ComponentId id, string pidFile, string expectedName, CancellationToken ct)
    {
        var pid = _processes.GetAlivePidFromFile(pidFile)
                  ?? (expectedName.Contains("mediamtx", StringComparison.OrdinalIgnoreCase)
                      ? _processes.FindProcessByName("mediamtx")
                      : null);
        if (pid is null)
            return Task.FromResult(new StartStepResult(id, true, "Not running (no managed PID)."));
        var ok = _processes.KillProcessTree(pid.Value, expectedName, out var msg);
        if (ok)
        {
            try { File.Delete(pidFile); } catch (IOException) { }
        }
        return Task.FromResult(new StartStepResult(id, ok, msg));
    }

    private static void TryHealPidFile(string pidFile, int pid)
    {
        try
        {
            var dir = Path.GetDirectoryName(pidFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var current = File.Exists(pidFile) ? File.ReadAllText(pidFile).Trim() : "";
            if (current == pid.ToString()) return;
            File.WriteAllText(pidFile, pid.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
