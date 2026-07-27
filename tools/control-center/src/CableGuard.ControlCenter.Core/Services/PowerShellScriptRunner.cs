using System.Diagnostics;
using System.Text;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Runs the existing PowerShell runtime scripts. All output is redacted before it reaches
/// the Control Center log or the GUI.
/// </summary>
public sealed class PowerShellScriptRunner : IScriptRunner
{
    private readonly ControlCenterLogger _logger;

    public PowerShellScriptRunner(ControlCenterLogger logger) => _logger = logger;

    public async Task<(int ExitCode, string Output)> RunAsync(string scriptPath, CancellationToken ct = default)
    {
        if (!File.Exists(scriptPath))
        {
            var msg = $"Script not found: {scriptPath}";
            _logger.Warn(msg);
            return (-1, msg);
        }

        _logger.Info($"Running script: {Path.GetFileName(scriptPath)}");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = new Process { StartInfo = psi };
        var output = new StringBuilder();

        proc.OutputDataReceived += (_, e) => Append(e.Data);
        proc.ErrorDataReceived += (_, e) => Append(e.Data);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Do NOT use WaitForExitAsync here: it also waits for stdout/stderr EOF, and detached
        // children spawned by the script (mediamtx, uvicorn, node) inherit the pipe handles,
        // so EOF never arrives while they keep running. Poll for process exit instead.
        while (!proc.HasExited)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct);
        }
        // Give already-buffered output a moment to flush, then stop reading.
        await Task.Delay(300, CancellationToken.None);
        try { proc.CancelOutputRead(); proc.CancelErrorRead(); } catch (InvalidOperationException) { }

        _logger.Info($"Script {Path.GetFileName(scriptPath)} exited with code {proc.ExitCode}.");
        return (proc.ExitCode, output.ToString());

        void Append(string? data)
        {
            if (data is null) return;
            var redacted = LogRedactor.Redact(data);
            lock (output) output.AppendLine(redacted);
            _logger.Info($"  | {redacted}");
        }
    }
}

/// <summary>Simple thread-safe file logger for runtime/logs/control-center.log (gitignored).</summary>
public sealed class ControlCenterLogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public event Action<string>? LineWritten;

    public ControlCenterLogger(string logsDir)
    {
        Directory.CreateDirectory(logsDir);
        _logFilePath = Path.Combine(logsDir, "control-center.log");
    }

    public string LogFilePath => _logFilePath;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {LogRedactor.Redact(message)}";
        lock (_lock)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
        LineWritten?.Invoke(line);
    }
}
