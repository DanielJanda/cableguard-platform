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
                var pid = _processes.GetAlivePidFromFile(pidFile);
                var whepStatus = pid is null ? null : await _prober.OptionsStatusCodeAsync(whepUrl, ct);
                var pathReady = pid is null ? null : await _mediaMtxApi.IsPathReadyAsync(_config.ProductionStream, ct);
                var probes = new ProbeResults(
                    ProcessAlive: pid is not null,
                    WhepReachable: whepStatus is >= 200 and < 300,
                    PathReady: pathReady);
                var status = StatusEvaluators.EvaluateMediaMtx(probes);
                var detail = status switch
                {
                    ComponentStatus.Running => $"WHEP ready, path '{_config.ProductionStream}' READY",
                    ComponentStatus.Degraded => $"WHEP up, path '{_config.ProductionStream}' NOT ready (camera source?)",
                    ComponentStatus.Fault => "Process up but WHEP :8889 not responding",
                    _ => "Not running",
                };
                return new ComponentSnapshot(ComponentId.MediaMtx, status, detail, pid);
            },
            startFunc: ct => RunScriptAsStep(ComponentId.MediaMtx, startScript, ct),
            stopFunc: ct => RunScriptAsStep(ComponentId.MediaMtx, stopScript, ct));
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
            ComponentId.Detector, "Fall Detector", configured, logFile,
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
            DisplayName = "Fall Detector – Zahrádky Upper",
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
        var pid = _processes.GetAlivePidFromFile(pidFile);
        if (pid is null)
            return Task.FromResult(new StartStepResult(id, true, "Not running (no managed PID)."));
        var ok = _processes.KillProcessTree(pid.Value, expectedName, out var msg);
        if (ok)
        {
            try { File.Delete(pidFile); } catch (IOException) { }
        }
        return Task.FromResult(new StartStepResult(id, ok, msg));
    }
}
