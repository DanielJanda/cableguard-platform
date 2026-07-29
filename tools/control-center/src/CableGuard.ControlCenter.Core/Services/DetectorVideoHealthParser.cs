using System.Text.Json;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Parses Event Core /api/v1/status for detector video_input health (no credentials).
/// </summary>
public sealed record DetectorVideoHealth(
    bool Available,
    string ServiceId,
    string Backend,
    string SourceMode,
    string ConnectionState,
    double? DecodedFps,
    double? LatestFrameAgeMs,
    int? ReconnectCount,
    string? LastErrorRedacted);

public static class DetectorVideoHealthParser
{
    public static DetectorVideoHealth? TryParse(string? statusJson, string? preferredServiceId, string? inputStream)
    {
        if (string.IsNullOrWhiteSpace(statusJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(statusJson);
            if (!doc.RootElement.TryGetProperty("services", out var services) ||
                services.ValueKind != JsonValueKind.Array)
                return null;

            JsonElement? best = null;
            foreach (var svc in services.EnumerateArray())
            {
                var id = svc.TryGetProperty("service_id", out var sid) ? sid.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(preferredServiceId) &&
                    id.Equals(preferredServiceId, StringComparison.OrdinalIgnoreCase))
                {
                    best = svc;
                    break;
                }
            }

            if (best is null)
            {
                foreach (var svc in services.EnumerateArray())
                {
                    if (!TryGetDetails(svc, out var details)) continue;
                    var stream = details.TryGetProperty("input_stream", out var isEl) ? isEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(inputStream) &&
                        string.Equals(stream, inputStream, StringComparison.OrdinalIgnoreCase))
                    {
                        best = svc;
                        break;
                    }
                    if (details.TryGetProperty("video_input", out _))
                        best ??= svc;
                }
            }

            if (best is null) return null;
            var service = best.Value;
            var serviceId = service.TryGetProperty("service_id", out var sid2) ? sid2.GetString() ?? "" : "";
            if (!TryGetDetails(service, out var det))
                return new DetectorVideoHealth(false, serviceId, "", "", "", null, null, null, Redact(service));

            string backend = "", sourceMode = "", conn = "";
            double? decoded = null, age = null;
            int? reconnects = null;
            string? lastErr = service.TryGetProperty("last_error", out var le) ? le.GetString() : null;
            if (det.TryGetProperty("video_input", out var vi) && vi.ValueKind == JsonValueKind.Object)
            {
                backend = vi.TryGetProperty("backend", out var b) ? b.GetString() ?? "" : "";
                sourceMode = vi.TryGetProperty("source_mode", out var sm) ? sm.GetString() ?? "" : "";
                conn = vi.TryGetProperty("connection_state", out var cs) ? cs.GetString() ?? "" : "";
                if (vi.TryGetProperty("decoded_fps", out var df) && df.TryGetDouble(out var dfs)) decoded = dfs;
                if (vi.TryGetProperty("latest_frame_age_ms", out var ag) && ag.TryGetDouble(out var ags)) age = ags;
                if (vi.TryGetProperty("reconnect_count", out var rc) && rc.TryGetInt32(out var rci)) reconnects = rci;
                if (vi.TryGetProperty("last_error", out var ve) && ve.ValueKind == JsonValueKind.String)
                    lastErr ??= ve.GetString();
            }

            var available = !string.IsNullOrWhiteSpace(backend) || decoded is not null;
            return new DetectorVideoHealth(
                available,
                serviceId,
                backend,
                sourceMode,
                conn,
                decoded,
                age,
                reconnects,
                RedactText(lastErr));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetDetails(JsonElement svc, out JsonElement details)
    {
        details = default;
        if (!svc.TryGetProperty("details_json", out var d)) return false;
        if (d.ValueKind == JsonValueKind.Object)
        {
            details = d;
            return true;
        }
        if (d.ValueKind == JsonValueKind.String)
        {
            var raw = d.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            try
            {
                using var inner = JsonDocument.Parse(raw);
                details = inner.RootElement.Clone();
                return details.ValueKind == JsonValueKind.Object;
            }
            catch { return false; }
        }
        return false;
    }

    private static string? Redact(JsonElement service)
    {
        if (service.TryGetProperty("last_error", out var le))
            return RedactText(le.GetString());
        return null;
    }

    public static string? RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Never show credentialed RTSP URLs in Control Center detail.
        return System.Text.RegularExpressions.Regex.Replace(
            text, @"rtsp://[^\s""']+", "rtsp://***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
