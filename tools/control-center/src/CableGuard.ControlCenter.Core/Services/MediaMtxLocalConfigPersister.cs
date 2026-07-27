using System.Text.RegularExpressions;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Persists a path's source into the gitignored deploy/mediamtx/mediamtx.local.yml by replacing
/// only the "source:" line under that path block. Never logs file content (contains RTSP credentials).
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
        var pathHeader = new Regex(@"^\s{2}" + Regex.Escape(pathName) + @"\s*:\s*$");
        var anyPathHeader = PathHeaderRegex();
        var sourceLine = SourceLineRegex();

        var headerIndex = lines.FindIndex(l => pathHeader.IsMatch(l));
        if (headerIndex < 0)
        {
            message = $"Path '{pathName}' not found in local config.";
            return false;
        }

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            if (anyPathHeader.IsMatch(lines[i])) break; // next path block — no source line found
            var match = sourceLine.Match(lines[i]);
            if (match.Success)
            {
                lines[i] = $"{match.Groups[1].Value}source: {newSource}";
                var backup = _ymlPath + ".bak";
                File.Copy(_ymlPath, backup, overwrite: true);
                File.WriteAllLines(_ymlPath, lines);
                message = $"Source of '{pathName}' persisted to local config (backup: mediamtx.local.yml.bak).";
                return true;
            }
        }

        message = $"No 'source:' line found under path '{pathName}'.";
        return false;
    }

    [GeneratedRegex(@"^\s{2}[A-Za-z0-9_\-]+\s*:\s*$")]
    private static partial Regex PathHeaderRegex();

    [GeneratedRegex(@"^(\s{4,})source:\s*\S+")]
    private static partial Regex SourceLineRegex();
}
