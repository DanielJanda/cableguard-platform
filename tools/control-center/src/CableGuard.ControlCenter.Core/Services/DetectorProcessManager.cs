using System.Diagnostics;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>Starts/stops detector instances via DetectorLaunchBuilder + Process.</summary>
public sealed class DetectorProcessManager
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly IProcessInspector _processes;
    private readonly Dictionary<string, int> _pids = new(StringComparer.OrdinalIgnoreCase);

    public DetectorProcessManager(ControlCenterConfig config, ControlCenterLogger logger, IProcessInspector processes)
    {
        _config = config;
        _logger = logger;
        _processes = processes;
    }

    public int? FindPid(DetectorInstance instance)
    {
        if (_pids.TryGetValue(instance.Id, out var pid))
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited) return pid;
            }
            catch (ArgumentException) { }
            _pids.Remove(instance.Id);
        }
        return _processes.FindProcessByCommandLineHint(
            string.IsNullOrWhiteSpace(instance.ProcessHint) ? instance.ScriptRelative : instance.ProcessHint);
    }

    public async Task<(bool Ok, string Message)> StartAsync(
        DetectorInstance instance,
        NotificationsDocument? notifications = null,
        bool forceDebug = false,
        CancellationToken ct = default)
    {
        if (FindPid(instance) is not null)
            return (true, "Already running.");

        var script = Path.Combine(_config.DetectorRoot,
            instance.ScriptRelative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(script) && !File.Exists(Path.Combine(_config.DetectorRoot, instance.ScriptRelative)))
            return (false, $"Script not found: {instance.ScriptRelative} (detector branch may not include this entrypoint).");

        var spec = DetectorLaunchBuilder.Build(instance, _config, notifications, forceDebug);
        _logger.Info($"[DETECTOR] Starting {instance.Id}: {DetectorLaunchBuilder.FormatCommandLine(spec)}");

        var psi = new ProcessStartInfo
        {
            FileName = spec.Executable,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = !(forceDebug || instance.DebugOverlay),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a);
        foreach (var (k, v) in spec.Environment)
            psi.Environment[k] = v;

        var logDir = Path.Combine(_config.LogsDir, "detectors");
        Directory.CreateDirectory(logDir);
        var outLog = Path.Combine(logDir, $"{instance.Id}.out.log");
        var errLog = Path.Combine(logDir, $"{instance.Id}.err.log");

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to start: {ex.Message}");
        }

        _pids[instance.Id] = proc.Id;
        _ = Task.Run(() => Drain(proc.StandardOutput, outLog), CancellationToken.None);
        _ = Task.Run(() => Drain(proc.StandardError, errLog), CancellationToken.None);
        // CUDA/model import can take several seconds before ConfigError or healthy loop.
        await Task.Delay(2500, ct);
        if (proc.HasExited)
            return (false, $"Exited immediately (code {proc.ExitCode}). See {errLog}");
        return (true, $"Started PID {proc.Id}.");
    }

    public (bool Ok, string Message) Stop(DetectorInstance instance)
    {
        var pid = FindPid(instance);
        if (pid is null) return (true, "Not running.");
        var ok = _processes.KillProcessTree(pid.Value, "python", out var msg);
        _pids.Remove(instance.Id);
        _logger.Info($"[DETECTOR] Stop {instance.Id}: {msg}");
        return (ok, msg);
    }

    private void Drain(StreamReader reader, string path)
    {
        try
        {
            while (reader.ReadLine() is { } line)
            {
                var redacted = LogRedactor.Redact(line);
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {redacted}{Environment.NewLine}");
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }
}
