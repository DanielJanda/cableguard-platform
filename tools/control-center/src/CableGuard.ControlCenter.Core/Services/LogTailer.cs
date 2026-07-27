namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Live tail of a log file for the Logs tab: reads only appended bytes on each poll,
/// redacts every line, supports view-clear without touching the file.
/// </summary>
public sealed class LogTailer
{
    private long _position;
    private string? _currentFile;

    /// <summary>Reads new (redacted) lines since the last call. Resets when the file is switched or truncated.</summary>
    public IReadOnlyList<string> ReadNewLines(string filePath)
    {
        var lines = new List<string>();
        if (!File.Exists(filePath)) return lines;

        if (_currentFile != filePath)
        {
            _currentFile = filePath;
            // Start with the last ~64 KB so opening a huge log is instant.
            var length = new FileInfo(filePath).Length;
            _position = Math.Max(0, length - 64 * 1024);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < _position) _position = 0; // truncated/rotated
        if (stream.Length == _position) return lines;

        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            lines.Add(LogRedactor.Redact(line));
        _position = stream.Length;
        return lines;
    }

    public void Reset()
    {
        _currentFile = null;
        _position = 0;
    }
}
