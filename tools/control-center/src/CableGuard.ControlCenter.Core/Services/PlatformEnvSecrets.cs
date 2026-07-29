namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Resolves Event Core / ingest secrets from process env or platform <c>.env</c> (gitignored).
/// Values must never be logged or shown in the UI.
/// </summary>
public static class PlatformEnvSecrets
{
    public const string IngestApiKey = "CABLEGUARD_INGEST_API_KEY";
    public const string EventCoreUrl = "CABLEGUARD_EVENT_CORE_URL";

    /// <summary>Process env first, then platform <c>.env</c>. Returns null when missing/blank.</summary>
    public static string? TryGet(string name, string? platformRoot)
    {
        var fromEnv = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        if (string.IsNullOrWhiteSpace(platformRoot))
            return null;

        return TryReadDotEnv(Path.Combine(platformRoot, ".env"), name);
    }

    public static string? TryReadDotEnv(string path, string name)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                    line = line[7..].Trim();

                var idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;

                var key = line[..idx].Trim();
                if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var val = line[(idx + 1)..].Trim();
                if (val.Length >= 2 &&
                    ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
                    val = val[1..^1];

                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
