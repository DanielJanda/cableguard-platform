using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

namespace CableGuard.ControlCenter.Core.Services;

public sealed class WindowsProcessInspector : IProcessInspector
{
    public int? GetAlivePidFromFile(string pidFilePath)
    {
        try
        {
            if (!File.Exists(pidFilePath)) return null;
            var text = File.ReadAllText(pidFilePath).Trim();
            if (!int.TryParse(text, out var pid)) return null;
            using var proc = Process.GetProcessById(pid);
            return proc.HasExited ? null : pid;
        }
        catch (ArgumentException) { return null; }   // no process with that id
        catch (InvalidOperationException) { return null; }
        catch (IOException) { return null; }
    }

    public WindowsServiceInfo? GetWindowsService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT Name, State, StartMode, DelayedAutoStart FROM Win32_Service WHERE Name = '{serviceName.Replace("'", "''")}'");
            foreach (var obj in searcher.Get())
            {
                return new WindowsServiceInfo(
                    obj["Name"]?.ToString() ?? serviceName,
                    obj["State"]?.ToString() ?? "Unknown",
                    obj["StartMode"]?.ToString() ?? "Unknown",
                    obj["DelayedAutoStart"] is bool delayed && delayed);
            }
        }
        catch (ManagementException) { }
        return null;
    }

    public bool IsPortListening(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return listeners.Any(ep => ep.Port == port);
    }

    public int? FindProcessByCommandLineHint(string hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'python%'");
            foreach (var obj in searcher.Get())
            {
                var commandLine = obj["CommandLine"]?.ToString() ?? "";
                if (commandLine.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt32(obj["ProcessId"]);
            }
        }
        catch (ManagementException) { }
        return null;
    }

    public int? FindProcessByName(string processName)
    {
        var all = FindAllProcessIdsByName(processName);
        return all.Count == 0 ? null : all[0];
    }

    public IReadOnlyList<int> FindAllProcessIdsByName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return Array.Empty<int>();
        var want = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        try
        {
            return Process.GetProcessesByName(want)
                .Where(p =>
                {
                    try { return !p.HasExited; }
                    catch { return false; }
                })
                .Select(p => p.Id)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    public bool KillProcessTree(int pid, string expectedNameFragment, out string message)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var name = proc.ProcessName;
            if (!string.IsNullOrEmpty(expectedNameFragment) &&
                !name.Contains(expectedNameFragment, StringComparison.OrdinalIgnoreCase))
            {
                message = $"PID {pid} is '{name}', expected '{expectedNameFragment}' — refusing to stop a foreign process.";
                return false;
            }
        }
        catch (ArgumentException)
        {
            message = $"PID {pid} is not running.";
            return true; // already stopped
        }

        // taskkill /T terminates the whole tree (npm.cmd → node, uvicorn → workers).
        var psi = new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var killer = Process.Start(psi)!;
        killer.WaitForExit(10_000);
        message = killer.ExitCode == 0 ? $"Process tree {pid} stopped." : $"taskkill exited with code {killer.ExitCode}.";
        return killer.ExitCode == 0;
    }
}
