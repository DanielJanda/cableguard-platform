using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CableGuard.Usb4761Probe;

/// <summary>
/// Read-only Advantech USB-4761 probe — same CLI bridge as Admin Studio.
/// Never writes relays. Exit 0 = CONNECTED.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var platformRoot = FindPlatformRoot();
        var cli = Path.Combine(platformRoot, "tools", "control-center", "scripts", "usb4761_guarded_cli.py");
        var python = FindPython();

        var report = new Dictionary<string, object?>
        {
            ["tool"] = "usb4761-probe",
            ["os"] = RuntimeInformation.OSDescription,
            ["process_arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["os_arch"] = RuntimeInformation.OSArchitecture.ToString(),
            ["cli"] = File.Exists(cli) ? "present" : "missing",
            ["python"] = python is null ? "missing" : "present",
            ["daqnavi"] = FindDaqNavi() ?? "missing",
        };

        if (python is null || !File.Exists(cli))
        {
            report["status"] = python is null ? "SDK LOAD ERROR" : "SDK NOT FOUND";
            report["error"] = python is null ? "Python not on PATH" : "usb4761_guarded_cli.py missing";
            Console.WriteLine(JsonSerializer.Serialize(report, Pretty));
            return 2;
        }

        var psi = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(cli);
        psi.ArgumentList.Add("probe");

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            report["status"] = "OPEN FAILED";
            report["error"] = "failed to start probe process";
            Console.WriteLine(JsonSerializer.Serialize(report, Pretty));
            return 3;
        }

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);

        if (string.IsNullOrWhiteSpace(stdout))
        {
            report["status"] = "OPEN FAILED";
            report["error"] = string.IsNullOrWhiteSpace(stderr) ? $"exit={proc.ExitCode}" : stderr.Trim();
            Console.WriteLine(JsonSerializer.Serialize(report, Pretty));
            return 3;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                report[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.TryGetInt64(out var n) ? n : p.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Array => p.Value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.True).ToArray(),
                    _ => p.Value.ToString(),
                };
            }
        }
        catch (Exception ex)
        {
            report["status"] = "OPEN FAILED";
            report["error"] = $"non-JSON probe output: {ex.Message}";
            report["raw"] = stdout.Length > 500 ? stdout[..500] : stdout;
            Console.WriteLine(JsonSerializer.Serialize(report, Pretty));
            return 3;
        }

        Console.WriteLine(JsonSerializer.Serialize(report, Pretty));
        var status = report.TryGetValue("status", out var s) ? s?.ToString() : null;
        return status == "CONNECTED" ? 0 : 1;
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private static string FindPlatformRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tools", "control-center", "scripts", "usb4761_guarded_cli.py")))
                return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "control-center")))
                return dir.FullName;
            dir = dir.Parent;
        }
        // tools/usb4761-probe → ../../
        var fromCwd = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
        return fromCwd;
    }

    private static string? FindDaqNavi()
    {
        foreach (var p in new[]
                 {
                     Environment.GetEnvironmentVariable("ADVANTECH_DAQNAVI_PATH"),
                     @"C:\Advantech\DAQNavi",
                     @"C:\Program Files\Advantech\DAQNavi",
                     @"C:\Program Files (x86)\Advantech\DAQNavi",
                 })
        {
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) return p;
        }
        return null;
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python.exe", "python3.exe", "py.exe" })
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                var full = Path.Combine(dir.Trim(), name);
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }
}
