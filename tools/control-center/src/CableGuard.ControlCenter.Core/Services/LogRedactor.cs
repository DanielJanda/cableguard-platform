using System.Text.RegularExpressions;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Redacts secrets from any text shown in the GUI or written to the Control Center log:
/// RTSP credentials, API key headers, password-like assignments.
/// </summary>
public static partial class LogRedactor
{
    [GeneratedRegex(@"(rtsps?://)([^/@\s:]+):([^/@\s]+)@", RegexOptions.IgnoreCase)]
    private static partial Regex RtspCredentials();

    [GeneratedRegex(@"(X-API-Key|X-Kiosk-Key|Authorization)\s*[:=]\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyHeaders();

    [GeneratedRegex(@"((?:password|passwd|pwd|secret|api[_-]?key|token)\s*[=:]\s*)(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordAssignments();

    public static string Redact(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        var result = RtspCredentials().Replace(line, "$1***:***@");
        result = ApiKeyHeaders().Replace(result, "$1: ***");
        result = PasswordAssignments().Replace(result, "$1***");
        return result;
    }
}
