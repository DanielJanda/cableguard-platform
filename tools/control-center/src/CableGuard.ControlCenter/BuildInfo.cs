using System.Diagnostics;
using System.IO;
using System.Reflection;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter;

/// <summary>Exact running build identity for the status panel.</summary>
public static class BuildInfo
{
    public static string Branch { get; private set; } = "unknown";
    public static string CommitSha { get; private set; } = "unknown";
    public static string BuildTimestamp { get; private set; } = "unknown";
    public static string Summary => $"branch={Branch}  sha={CommitSha}  built={BuildTimestamp}";

    public static void Initialize(ControlCenterConfig config)
    {
        BuildTimestamp = File.GetLastWriteTimeUtc(Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");

        var root = config.PlatformRoot;
        Branch = RunGit(root, "rev-parse --abbrev-ref HEAD") ?? Environment.GetEnvironmentVariable("CABLEGUARD_GIT_BRANCH") ?? "unknown";
        var sha = RunGit(root, "rev-parse HEAD") ?? Environment.GetEnvironmentVariable("CABLEGUARD_GIT_SHA");
        CommitSha = string.IsNullOrWhiteSpace(sha) ? "unknown" : (sha.Length > 12 ? sha[..12] : sha);
    }

    private static string? RunGit(string root, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout) ? stdout : null;
        }
        catch
        {
            return null;
        }
    }
}
