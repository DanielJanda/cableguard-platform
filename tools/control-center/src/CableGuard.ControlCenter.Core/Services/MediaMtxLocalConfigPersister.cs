using System.Text.RegularExpressions;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Persists path blocks into gitignored deploy/mediamtx/mediamtx.local.yml.
/// Never logs file content (contains RTSP credentials).
/// </summary>
public sealed partial class MediaMtxLocalConfigPersister : IMediaMtxConfigPersister
{
    private readonly string _ymlPath;

    public MediaMtxLocalConfigPersister(string ymlPath) => _ymlPath = ymlPath;

    public bool PersistPathSource(string pathName, string newSource, out string message)
    {
        if (!File.Exists(_ymlPath))
        {
            message = "mediamtx.local.yml not found.";
            return false;
        }

        var lines = File.ReadAllLines(_ymlPath).ToList();
        var headerIndex = FindPathHeader(lines, pathName);
        if (headerIndex < 0)
        {
            message = $"Path '{pathName}' not found in local config.";
            return false;
        }

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            if (PathHeaderRegex().IsMatch(lines[i])) break;
            var match = SourceLineRegex().Match(lines[i]);
            if (match.Success)
            {
                lines[i] = $"{match.Groups[1].Value}source: {newSource}";
                WriteWithBackup(lines);
                message = $"Source of '{pathName}' persisted to local config.";
                return true;
            }
        }

        message = $"No 'source:' line found under path '{pathName}'.";
        return false;
    }

    public bool UpsertPath(string pathName, string source, string? rtspTransport, out string message)
    {
        if (string.IsNullOrWhiteSpace(pathName))
        {
            message = "Empty path name.";
            return false;
        }

        if (!File.Exists(_ymlPath))
        {
            message = "mediamtx.local.yml not found.";
            return false;
        }

        var transport = string.Equals(rtspTransport, "udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
        var lines = File.ReadAllLines(_ymlPath).ToList();
        var headerIndex = FindPathHeader(lines, pathName);

        if (headerIndex >= 0)
        {
            // Replace existing source + ensure transport line.
            var replacedSource = false;
            var replacedTransport = false;
            var end = FindPathBlockEnd(lines, headerIndex);
            for (var i = headerIndex + 1; i < end; i++)
            {
                var srcMatch = SourceLineRegex().Match(lines[i]);
                if (srcMatch.Success)
                {
                    lines[i] = $"{srcMatch.Groups[1].Value}source: {source}";
                    replacedSource = true;
                    continue;
                }
                if (TransportLineRegex().IsMatch(lines[i]))
                {
                    lines[i] = "    rtspTransport: " + transport;
                    replacedTransport = true;
                }
            }
            if (!replacedSource)
                lines.Insert(headerIndex + 1, $"    source: {source}");
            if (!replacedTransport)
                lines.Insert(headerIndex + (replacedSource ? 2 : 2), $"    rtspTransport: {transport}");
            WriteWithBackup(lines);
            message = $"Path '{pathName}' updated in local config.";
            return true;
        }

        // Insert new block under paths:
        var pathsIndex = lines.FindIndex(l => PathsRootRegex().IsMatch(l));
        if (pathsIndex < 0)
        {
            message = "No 'paths:' root in mediamtx.local.yml.";
            return false;
        }

        var insertAt = pathsIndex + 1;
        while (insertAt < lines.Count && string.IsNullOrWhiteSpace(lines[insertAt]))
            insertAt++;

        var block = new[]
        {
            $"  {pathName}:",
            $"    source: {source}",
            $"    rtspTransport: {transport}",
            "    sourceOnDemand: no",
        };
        lines.InsertRange(insertAt, block);
        WriteWithBackup(lines);
        message = $"Path '{pathName}' inserted into local config.";
        return true;
    }

    public bool RemovePath(string pathName, out string message)
    {
        if (!File.Exists(_ymlPath))
        {
            message = "mediamtx.local.yml not found.";
            return false;
        }

        var lines = File.ReadAllLines(_ymlPath).ToList();
        var headerIndex = FindPathHeader(lines, pathName);
        if (headerIndex < 0)
        {
            message = $"Path '{pathName}' already absent from local config.";
            return true;
        }

        var end = FindPathBlockEnd(lines, headerIndex);
        lines.RemoveRange(headerIndex, end - headerIndex);
        WriteWithBackup(lines);
        message = $"Path '{pathName}' removed from local config.";
        return true;
    }

    private static int FindPathHeader(List<string> lines, string pathName)
    {
        var pathHeader = new Regex(@"^\s{2}" + Regex.Escape(pathName) + @"\s*:\s*$");
        return lines.FindIndex(l => pathHeader.IsMatch(l));
    }

    private static int FindPathBlockEnd(List<string> lines, int headerIndex)
    {
        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            if (PathHeaderRegex().IsMatch(lines[i])) return i;
            // Top-level key (not indented under paths) ends the paths section.
            if (TopLevelKeyRegex().IsMatch(lines[i])) return i;
        }
        return lines.Count;
    }

    private void WriteWithBackup(List<string> lines)
    {
        var backup = _ymlPath + ".bak";
        File.Copy(_ymlPath, backup, overwrite: true);
        File.WriteAllLines(_ymlPath, lines);
    }

    [GeneratedRegex(@"^\s{2}[A-Za-z0-9_\-]+\s*:\s*$")]
    private static partial Regex PathHeaderRegex();

    [GeneratedRegex(@"^(\s{4,})source:\s*\S+")]
    private static partial Regex SourceLineRegex();

    [GeneratedRegex(@"^\s{4,}rtspTransport:\s*\S+")]
    private static partial Regex TransportLineRegex();

    [GeneratedRegex(@"^paths\s*:\s*$")]
    private static partial Regex PathsRootRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]+\s*:\s*$")]
    private static partial Regex TopLevelKeyRegex();
}
