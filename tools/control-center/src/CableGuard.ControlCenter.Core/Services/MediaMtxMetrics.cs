using System.Globalization;
using System.Text.RegularExpressions;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Parses MediaMTX Prometheus text. Metric names differ across versions —
/// we accept both v1.11-era (paths_bytes_received) and newer aliases.
/// </summary>
public static class MediaMtxMetricsParser
{
    private static readonly Regex Line = new(
        @"^(?<name>[a-zA-Z_:][a-zA-Z0-9_:]*)\{(?<labels>[^}]*)\}\s+(?<value>[-+]?[0-9]*\.?[0-9]+([eE][-+]?\d+)?)",
        RegexOptions.Compiled);

    public sealed class PathMetrics
    {
        public string Name { get; init; } = "";
        public string State { get; init; } = "";
        public double? BytesReceived { get; set; }
        public double? BytesSent { get; set; }
        public double? Readers { get; set; }
    }

    public static Dictionary<string, PathMetrics> ParsePaths(string prometheusText)
    {
        var map = new Dictionary<string, PathMetrics>(StringComparer.Ordinal);
        foreach (var raw in prometheusText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var m = Line.Match(line);
            if (!m.Success) continue;
            var name = m.Groups["name"].Value;
            var labels = ParseLabels(m.Groups["labels"].Value);
            if (!labels.TryGetValue("name", out var pathName)) continue;
            if (!map.TryGetValue(pathName, out var pm))
            {
                pm = new PathMetrics { Name = pathName, State = labels.GetValueOrDefault("state", "") };
                map[pathName] = pm;
            }
            if (!double.TryParse(m.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                continue;

            switch (name)
            {
                case "paths_bytes_received":
                case "paths_inbound_bytes":
                    pm.BytesReceived = val; break;
                case "paths_bytes_sent":
                case "paths_outbound_bytes":
                    pm.BytesSent = val; break;
                case "paths_readers":
                    pm.Readers = (pm.Readers ?? 0) + val; break;
            }
        }
        return map;
    }

    public static IReadOnlyList<string> ListMetricNames(string prometheusText) =>
        prometheusText.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l =>
            {
                var i = l.IndexOf('{');
                var j = l.IndexOf(' ');
                if (i > 0) return l[..i];
                if (j > 0) return l[..j];
                return l;
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x)
            .ToList();

    private static Dictionary<string, string> ParseLabels(string labels)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(labels, @"(?<k>[a-zA-Z_][a-zA-Z0-9_]*)=""(?<v>[^""]*)"""))
            dict[m.Groups["k"].Value] = m.Groups["v"].Value;
        return dict;
    }
}

public sealed class MediaMtxMetricsClient
{
    private readonly HttpClient _http;
    private readonly string _base;

    public MediaMtxMetricsClient(HttpClient http, string metricsBase = "http://127.0.0.1:9998")
    {
        var uri = new Uri(metricsBase);
        if (uri.Host is not ("127.0.0.1" or "localhost"))
            throw new ArgumentException("MediaMTX metrics must stay 127.0.0.1 only.");
        _http = http;
        _base = metricsBase.TrimEnd('/');
    }

    public async Task<(bool Available, string? Body, string Detail)> TryGetMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{_base}/metrics", ct);
            if (!resp.IsSuccessStatusCode)
                return (false, null, $"HTTP {(int)resp.StatusCode} from {_base}/metrics");
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (true, body, "OK");
        }
        catch (HttpRequestException ex)
        {
            return (false, null,
                $"NOT AVAILABLE — metrics endpoint unreachable ({ex.Message}). " +
                "Enable `metrics: yes` and `metricsAddress: 127.0.0.1:9998` in mediamtx.local.yml, then restart MediaMTX.");
        }
        catch (TaskCanceledException)
        {
            return (false, null, "metrics request timed out");
        }
    }
}

/// <summary>Patches gitignored local yml to enable localhost metrics without exposing to LAN.</summary>
public static class MediaMtxMetricsEnabler
{
    public static bool EnsureLocalhostMetrics(string ymlPath, out string message)
    {
        if (!File.Exists(ymlPath))
        {
            message = "mediamtx.local.yml not found";
            return false;
        }
        var text = File.ReadAllText(ymlPath);
        var original = text;
        text = Regex.Replace(text, @"^metrics:\s*no\s*$", "metrics: yes", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^metrics:\s*false\s*$", "metrics: yes", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!Regex.IsMatch(text, @"^metricsAddress:\s*", RegexOptions.Multiline))
            text = "metrics: yes\nmetricsAddress: 127.0.0.1:9998\n" + text;
        else
            text = Regex.Replace(text, @"^metricsAddress:\s*.*$", "metricsAddress: 127.0.0.1:9998", RegexOptions.Multiline);

        if (text == original)
        {
            message = "metrics already configured for localhost (or no change needed)";
            return true;
        }
        File.Copy(ymlPath, ymlPath + ".bak-metrics", overwrite: true);
        File.WriteAllText(ymlPath, text);
        message = "Enabled metrics: yes + metricsAddress: 127.0.0.1:9998 (restart MediaMTX required)";
        return true;
    }
}
