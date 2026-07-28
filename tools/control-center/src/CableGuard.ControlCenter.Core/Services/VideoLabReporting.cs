using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public static class ConfigFingerprint
{
    private static readonly string[] Forbidden = { "password", "passwd", "pwd", "secret", "token", "credential", "rtsp_url", "api_key" };

    public static string Compute(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        foreach (var key in Forbidden)
        {
            if (json.Contains($"\"{key}\"", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Fingerprint payload must not contain '{key}'.");
        }
        // Strip absolute Windows paths that may embed usernames.
        json = Regex.Replace(json, @"[A-Za-z]:\\\\[^\""]+", "<path>");
        json = Regex.Replace(json, @"[A-Za-z]:/[^\""]+", "<path>");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static object BuildStreamLatencyPayload(
        string? cameraId,
        string streamId,
        string mediaMtxPath,
        int? width,
        int? height,
        double? fps,
        string? codec)
        => new
        {
            camera_id = cameraId ?? "",
            stream_id = streamId,
            mediamtx_path = mediaMtxPath,
            width,
            height,
            fps,
            codec = codec ?? "",
            method_scope = "glass_to_glass_manual",
        };

    public static object BuildQualificationPayload(
        string cameraId,
        string profile,
        string? codec,
        int? width,
        int? height,
        double? fpsConfig,
        string mediaMtxPath,
        string transport,
        string detectorProfile,
        string? modelSha,
        string? algorithmVersion,
        string? roiHash) => new
    {
        camera_id = cameraId,
        profile,
        codec,
        resolution = width is null || height is null ? null : $"{width}x{height}",
        fps_config = fpsConfig,
        mediamtx_path = mediaMtxPath,
        rtsp_transport = transport,
        detector_profile = detectorProfile,
        model_sha = modelSha,
        algorithm_version = algorithmVersion,
        roi_hash = roiHash,
    };
}

public static class QualificationEngine
{
    public static QualificationReport Evaluate(
        StreamLiveMetrics metrics,
        EngineeringThresholds thresholds,
        bool glassToGlassMeasured,
        double? glassToGlassMs,
        string fingerprint)
    {
        var report = new QualificationReport
        {
            TestId = $"qual-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            Metrics = metrics,
            ConfigFingerprint = fingerprint,
            GlassToGlassMeasured = glassToGlassMeasured,
        };

        if (thresholds.RequireGlassToGlassForPass && !glassToGlassMeasured)
        {
            report.Verdict = QualificationVerdict.Incomplete;
            report.VerdictLabel = "INCOMPLETE";
            report.Reasons.Add("GLASS-TO-GLASS LATENCY: NOT MEASURED — required for ENGINEERING PASS");
            return report;
        }

        var fail = new List<string>();
        if (metrics.ReceivedFps.Kind == MeasurementKind.Measured &&
            metrics.ReceivedFps.Value < thresholds.MinReceivedFps)
            fail.Add($"received FPS {metrics.ReceivedFps.Value:0.0} < min {thresholds.MinReceivedFps}");

        if (metrics.Health is VideoHealthState.Offline or VideoHealthState.Stale)
            fail.Add($"video health is {metrics.Health}");

        if (glassToGlassMeasured && glassToGlassMs is not null && glassToGlassMs > thresholds.MaxGlassToGlassMs)
            fail.Add($"G2G {glassToGlassMs:0} ms > max {thresholds.MaxGlassToGlassMs} ms (engineering)");

        if (fail.Count > 0)
        {
            report.Verdict = QualificationVerdict.EngineeringFail;
            report.VerdictLabel = "ENGINEERING FAIL";
            report.Reasons.AddRange(fail);
            report.Reasons.Add(thresholds.Label);
            return report;
        }

        report.Verdict = QualificationVerdict.EngineeringPass;
        report.VerdictLabel = "ENGINEERING PASS";
        report.Reasons.Add(thresholds.Label);
        return report;
    }
}

public static class VideoLabReportWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string CreateRunDirectory(string platformRoot, string testId)
    {
        var dir = Path.Combine(platformRoot, "runtime", "test-results", testId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void WriteJson(string path, object obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        if (ForbiddenSecretPresent(json))
            throw new InvalidOperationException("Refusing to write report containing secret-like fields.");
        File.WriteAllText(path, json);
    }

    public static void AppendCsv(string path, string header, string line)
    {
        if (!File.Exists(path)) File.WriteAllText(path, header + Environment.NewLine);
        File.AppendAllText(path, line + Environment.NewLine);
    }

    public static bool ForbiddenSecretPresent(string text) =>
        Regex.IsMatch(text, @"""(password|passwd|token|api_key|rtsp_url)""", RegexOptions.IgnoreCase);
}
