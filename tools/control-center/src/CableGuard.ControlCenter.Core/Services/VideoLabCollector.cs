using System.Diagnostics;
using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public sealed class VideoLabCollector
{
    private readonly ControlCenterConfig _config;
    private readonly IMediaMtxApi _api;
    private readonly MediaMtxMetricsClient _metrics;
    private readonly Dictionary<string, long> _lastBytes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastBytesAt = new(StringComparer.Ordinal);
    private readonly List<GlassToGlassSample> _g2g = new();

    public VideoLabCollector(ControlCenterConfig config, IMediaMtxApi api, HttpClient http)
    {
        _config = config;
        _api = api;
        _metrics = new MediaMtxMetricsClient(http);
    }

    public EngineeringThresholds Thresholds { get; set; } = new();
    public IReadOnlyList<GlassToGlassSample> GlassToGlassSamples => _g2g;

    public void RecordManualLatency(
        string streamId,
        string cameraId,
        double latencyMs,
        string configurationFingerprint,
        string note)
    {
        foreach (var old in _g2g.Where(s => s.StreamId == streamId))
            old.IsAuthoritative = false;

        _g2g.Add(new GlassToGlassSample
        {
            MeasuredAtUtc = DateTimeOffset.UtcNow,
            StreamId = streamId,
            CameraId = cameraId ?? "",
            Method = "manual",
            LatencyMs = latencyMs,
            OperatorNote = note,
            ConfigurationFingerprint = configurationFingerprint ?? "",
            IsAuthoritative = true,
            IsOutdated = false,
        });
    }

    public static void MarkOutdatedIfFingerprintChanged(
        IEnumerable<GlassToGlassSample> samples,
        string streamId,
        string currentFingerprint)
    {
        foreach (var s in samples.Where(x => x.StreamId == streamId && x.IsAuthoritative))
        {
            if (string.IsNullOrWhiteSpace(s.ConfigurationFingerprint)) continue;
            if (!string.Equals(s.ConfigurationFingerprint, currentFingerprint, StringComparison.Ordinal))
                s.IsOutdated = true;
        }
    }

    public async Task<StreamLiveMetrics> CollectAsync(LogicalStream stream, string? cameraId, CancellationToken ct = default)
    {
        var m = new StreamLiveMetrics
        {
            StreamId = stream.StreamId,
            MediaMtxPath = stream.MediaMtxPath,
            SourceCameraId = cameraId ?? stream.CameraId,
        };

        var ready = await _api.IsPathReadyAsync(stream.MediaMtxPath, ct);
        m.PathReady = ready == true;

        // Control API path details (no credentials)
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var apiBase = _config.MediaMtxApiBase.TrimEnd('/');
            using var resp = await http.GetAsync($"{apiBase}/v3/paths/get/{Uri.EscapeDataString(stream.MediaMtxPath)}", ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                if (root.TryGetProperty("bytesReceived", out var br))
                    m.MediaMtxBytesReceived = MetricValue.Measured(br.GetInt64(), "B");
                if (root.TryGetProperty("bytesSent", out var bs))
                    m.MediaMtxBytesSent = MetricValue.Measured(bs.GetInt64(), "B");
                if (root.TryGetProperty("readers", out var readers) && readers.ValueKind == JsonValueKind.Array)
                    m.MediaMtxReaders = MetricValue.Measured(readers.GetArrayLength(), "sessions");
                if (root.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array && tracks.GetArrayLength() > 0)
                    m.Codec = MetricValue.Measured(0, tracks[0].GetString() ?? "", note: tracks[0].GetString() ?? "");
                // Derive rough bitrate from bytesReceived delta
                if (root.TryGetProperty("bytesReceived", out var br2))
                {
                    var bytes = br2.GetInt64();
                    var key = stream.MediaMtxPath;
                    var now = DateTime.UtcNow;
                    if (_lastBytes.TryGetValue(key, out var prev) && _lastBytesAt.TryGetValue(key, out var prevAt))
                    {
                        var dt = (now - prevAt).TotalSeconds;
                        if (dt > 0.5)
                        {
                            var kbps = (bytes - prev) * 8.0 / 1000.0 / dt;
                            if (kbps >= 0) m.BitrateKbps = MetricValue.Measured(kbps, "kbps", "from MediaMTX bytesReceived delta");
                        }
                    }
                    _lastBytes[key] = bytes;
                    _lastBytesAt[key] = now;
                }
            }
        }
        catch { /* leave unknown */ }

        var (metricsOk, body, metricsDetail) = await _metrics.TryGetMetricsAsync(ct);
        if (metricsOk && body is not null)
        {
            var paths = MediaMtxMetricsParser.ParsePaths(body);
            if (paths.TryGetValue(stream.MediaMtxPath, out var pm))
            {
                if (pm.BytesReceived is not null)
                    m.MediaMtxBytesReceived = MetricValue.Measured(pm.BytesReceived.Value, "B", "prometheus");
                if (pm.BytesSent is not null)
                    m.MediaMtxBytesSent = MetricValue.Measured(pm.BytesSent.Value, "B", "prometheus");
                if (pm.Readers is not null)
                    m.MediaMtxReaders = MetricValue.Measured(pm.Readers.Value, "readers", "prometheus");
            }
        }
        else
        {
            // Keep Control API values; annotate metrics status elsewhere.
            _ = metricsDetail;
        }

        // G2G: only if manual sample exists for this stream and fingerprint still matches.
        var sample = _g2g.LastOrDefault(s => s.StreamId == stream.StreamId && s.IsAuthoritative);
        if (sample is not null)
        {
            var camId = cameraId ?? stream.CameraId;
            var fp = ConfigFingerprint.Compute(ConfigFingerprint.BuildStreamLatencyPayload(
                camId, stream.StreamId, stream.MediaMtxPath,
                m.ResolutionWidth.Kind == MeasurementKind.Measured ? (int?)m.ResolutionWidth.Value : null,
                m.ResolutionHeight.Kind == MeasurementKind.Measured ? (int?)m.ResolutionHeight.Value : null,
                m.ReceivedFps.Kind == MeasurementKind.Measured ? m.ReceivedFps.Value : null,
                m.Codec.Note));
            MarkOutdatedIfFingerprintChanged(_g2g, stream.StreamId, fp);
            sample = _g2g.LastOrDefault(s => s.StreamId == stream.StreamId && s.IsAuthoritative);
        }

        m.GlassToGlassLatencyMs = sample is null
            ? MetricValue.NotMeasured("No physical visual reference measurement recorded")
            : sample.IsOutdated
                ? MetricValue.NotMeasured($"OUTDATED – konfigurace streamu se změnila (bylo {sample.LatencyMs:0} ms MANUAL)")
                : MetricValue.Measured(sample.LatencyMs, "ms", $"manual @ {sample.MeasuredAtUtc:u}");

        var iceConnected = string.Equals(m.IceState, "connected", StringComparison.OrdinalIgnoreCase);
        m.Health = VideoHealthEvaluator.Evaluate(
            m.PathReady,
            iceConnected,
            m.SecondsSinceLastFrame.Value,
            m.ReceivedFps.Kind == MeasurementKind.Measured ? m.ReceivedFps.Value : null,
            Thresholds,
            out var detail);
        m.HealthDetail = detail;
        return m;
    }

    public void ApplyBrowserProbe(StreamLiveMetrics target, BrowserProbeStats stats)
    {
        target.IceState = stats.IceState ?? "unknown";
        if (stats.Fps is not null) target.ReceivedFps = MetricValue.Measured(stats.Fps.Value, "fps", "browser WHEP probe");
        if (stats.FramesReceived is not null) target.FramesReceived = MetricValue.Measured(stats.FramesReceived.Value, "frames");
        if (stats.FramesDecoded is not null) target.FramesDecoded = MetricValue.Measured(stats.FramesDecoded.Value, "frames");
        if (stats.FramesDropped is not null) target.FramesDropped = MetricValue.Measured(stats.FramesDropped.Value, "frames");
        if (stats.PacketLoss is not null) target.PacketLoss = MetricValue.Measured(stats.PacketLoss.Value, "%");
        if (stats.JitterMs is not null) target.JitterMs = MetricValue.Measured(stats.JitterMs.Value, "ms");
        if (stats.RttMs is not null) target.RttMs = MetricValue.Measured(stats.RttMs.Value, "ms");
        if (stats.BitrateKbps is not null) target.BitrateKbps = MetricValue.Measured(stats.BitrateKbps.Value, "kbps", "browser probe");
        if (stats.Width is not null) target.ResolutionWidth = MetricValue.Measured(stats.Width.Value, "px");
        if (stats.Height is not null) target.ResolutionHeight = MetricValue.Measured(stats.Height.Value, "px");
        if (stats.SecondsSinceLastFrame is not null)
            target.SecondsSinceLastFrame = MetricValue.Measured(stats.SecondsSinceLastFrame.Value, "s");
        if (stats.FreezeCount is not null) target.FreezeCount = MetricValue.Measured(stats.FreezeCount.Value, "count");
        if (stats.ReconnectCount is not null) target.ReconnectCount = MetricValue.Measured(stats.ReconnectCount.Value, "count");

        target.Health = VideoHealthEvaluator.Evaluate(
            target.PathReady,
            string.Equals(target.IceState, "connected", StringComparison.OrdinalIgnoreCase),
            target.SecondsSinceLastFrame.Value,
            target.ReceivedFps.Kind == MeasurementKind.Measured ? target.ReceivedFps.Value : null,
            Thresholds,
            out var detail);
        target.HealthDetail = detail;
    }
}

public sealed class BrowserProbeStats
{
    public string? StreamId { get; set; }
    public string? IceState { get; set; }
    public double? Fps { get; set; }
    public double? FramesReceived { get; set; }
    public double? FramesDecoded { get; set; }
    public double? FramesDropped { get; set; }
    public double? PacketLoss { get; set; }
    public double? JitterMs { get; set; }
    public double? RttMs { get; set; }
    public double? BitrateKbps { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? SecondsSinceLastFrame { get; set; }
    public double? FreezeCount { get; set; }
    public double? ReconnectCount { get; set; }
}

public static class ResourceMonitor
{
    public static ResourceSnapshot Capture(IReadOnlyDictionary<string, int?> namedPids)
    {
        var snap = new ResourceSnapshot();
        // CPU % requires System.Diagnostics.PerformanceCounter (optional). Leave unknown honestly.
        snap.SystemCpuPercent = null;
        snap.SystemRamUsedMb = null;
        snap.SystemRamTotalMb = null;

        foreach (var (name, pid) in namedPids)
        {
            var pr = new ProcessResource { Name = name, Pid = pid };
            if (pid is not null)
            {
                try
                {
                    using var p = Process.GetProcessById(pid.Value);
                    pr.WorkingSetMb = p.WorkingSet64 / (1024.0 * 1024.0);
                }
                catch { }
            }
            snap.Processes[name] = pr;
        }
        return snap;
    }
}

public static class CameraProfileInspector
{
    public static async Task<string> InspectAsync(string host, int port, CancellationToken ct = default)
    {
        // Safe TCP reachability only unless ffprobe is on PATH — never include credentials.
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync(host, port);
            var ok = await Task.WhenAny(connect, Task.Delay(3000, ct)) == connect && client.Connected;
            if (!ok) return $"TCP {host}:{port} — timeout/unreachable";
        }
        catch (Exception ex)
        {
            return $"TCP {host}:{port} — {ex.GetType().Name}";
        }

        var ffprobe = FindOnPath("ffprobe.exe") ?? FindOnPath("ffprobe");
        if (ffprobe is null)
            return $"TCP OK. ffprobe NOT AVAILABLE — codec/GOP/B-frames unknown (install ffprobe to deepen profile audit). Do not infer PTS as UTC capture time.";

        return $"TCP OK. ffprobe found at {ffprobe} — RTSP URL with credentials must be supplied via Credential Manager at call site; inspector will not print URLs.";
    }

    private static string? FindOnPath(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in paths)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

public static class DetectorFreshnessProvider
{
    public static DetectorFreshnessSnapshot Get(string detectorId) => new()
    {
        DetectorId = detectorId,
        Availability = MeasurementKind.NotAvailable,
        Detail = "NOT AVAILABLE — frame_received_monotonic / inference_* diagnostics contract not present on detector main. " +
                 "This is NOT glass-to-glass latency. Do not invent queue age.",
    };
}
