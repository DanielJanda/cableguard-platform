using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class VideoLabLogicTests
{
    private readonly EngineeringThresholds _t = new();

    [Fact]
    public void IceConnectedAlone_IsNotRealtime()
    {
        var state = VideoHealthEvaluator.Evaluate(
            pathReady: true, iceConnected: true,
            secondsSinceLastFrame: null, receivedFps: null, _t, out var detail);
        Assert.Equal(VideoHealthState.Unknown, state);
        Assert.Contains("not REALTIME", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IceConnectedButZeroFps_IsStale()
    {
        var state = VideoHealthEvaluator.Evaluate(true, true, 0.1, receivedFps: 0, _t, out _);
        Assert.Equal(VideoHealthState.Stale, state);
    }

    [Fact]
    public void ContinuingFrames_CanBeRealtime()
    {
        var state = VideoHealthEvaluator.Evaluate(true, true, 0.2, receivedFps: 20, _t, out _);
        Assert.Equal(VideoHealthState.Realtime, state);
    }

    [Fact]
    public void LongFrameGap_IsStale()
    {
        var state = VideoHealthEvaluator.Evaluate(true, false, 15, null, _t, out _);
        Assert.Equal(VideoHealthState.Stale, state);
    }

    [Fact]
    public void GlassToGlass_DefaultsToNotMeasured()
    {
        var m = new StreamLiveMetrics();
        Assert.Equal(MeasurementKind.NotMeasured, m.GlassToGlassLatencyMs.Kind);
        Assert.Equal("NOT MEASURED", m.GlassToGlassLatencyMs.ToString());
    }

    [Fact]
    public void Qualification_IncompleteWithoutG2G()
    {
        var report = QualificationEngine.Evaluate(
            new StreamLiveMetrics { Health = VideoHealthState.Realtime, ReceivedFps = MetricValue.Measured(20, "fps") },
            new EngineeringThresholds { RequireGlassToGlassForPass = true },
            glassToGlassMeasured: false, glassToGlassMs: null, fingerprint: "abc");
        Assert.Equal(QualificationVerdict.Incomplete, report.Verdict);
        Assert.Contains(report.Reasons, r => r.Contains("NOT MEASURED"));
    }

    [Fact]
    public void SoakPercentiles_AreStable()
    {
        var samples = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        var stats = SoakStatisticsCalculator.Compute("fps", samples);
        Assert.Equal(50.5, stats.Median);
        Assert.Equal(95.05, stats.P95!.Value, 2);
        Assert.Equal(99.01, stats.P99!.Value, 2);
        Assert.Equal(100, stats.Max);
    }

    [Fact]
    public void LatencyDrift_RequiresSamples()
    {
        Assert.Null(SoakStatisticsCalculator.LatencyDriftMsPerMinute(Array.Empty<(double, double)>()));
        var drift = SoakStatisticsCalculator.LatencyDriftMsPerMinute(new (double, double)[] { (0, 100), (1, 120), (2, 140) });
        Assert.NotNull(drift);
        Assert.InRange(drift!.Value, 19, 21);
    }

    [Fact]
    public void BacklogDetected_WhenQueueAgeRises()
    {
        Assert.False(StaleFrameDetector.IsBacklog(new[] { 10d, 12, 11 }));
        Assert.True(StaleFrameDetector.IsBacklog(new[] { 50d, 60, 70, 400, 500, 600, 700 }));
    }

    [Fact]
    public void Fingerprint_ExcludesSecrets_AndIsStable()
    {
        var payload = ConfigFingerprint.BuildQualificationPayload(
            "cam", "102", "H264", 1280, 720, 20, "path", "tcp", "fall", "sha", "v1", "roi");
        var a = ConfigFingerprint.Compute(payload);
        var b = ConfigFingerprint.Compute(payload);
        Assert.Equal(a, b);
        Assert.Throws<InvalidOperationException>(() =>
            ConfigFingerprint.Compute(new { password = "x" }));
    }

    [Fact]
    public void ReportWriter_RejectsSecrets()
    {
        Assert.True(VideoLabReportWriter.ForbiddenSecretPresent("""{"token":"abc"}"""));
        Assert.False(VideoLabReportWriter.ForbiddenSecretPresent("""{"stream":"x"}"""));
    }

    [Fact]
    public void MetricsParser_ReadsPathBytes()
    {
        var text = """
        # HELP
        paths_bytes_received{name="zahradky-horni-stanice",state="ready"} 1000
        paths_bytes_sent{name="zahradky-horni-stanice",state="ready"} 2000
        paths_readers{name="zahradky-horni-stanice",state="ready"} 2
        """;
        var map = MediaMtxMetricsParser.ParsePaths(text);
        Assert.Equal(1000, map["zahradky-horni-stanice"].BytesReceived);
        Assert.Equal(2000, map["zahradky-horni-stanice"].BytesSent);
        Assert.Equal(2, map["zahradky-horni-stanice"].Readers);
    }

    [Fact]
    public void DetectorFreshness_IsNotAvailable_NotFake()
    {
        var snap = DetectorFreshnessProvider.Get("fall-zahradky-upper");
        Assert.Equal(MeasurementKind.NotAvailable, snap.Availability);
        Assert.Contains("NOT AVAILABLE", snap.Detail);
        Assert.False(snap.BacklogDetected);
    }

    [Fact]
    public void DetectorFreshness_MonotonicDelta()
    {
        // Pure helper for when diagnostics arrive later.
        double Age(double received, double now) => now - received;
        Assert.Equal(150, Age(1000, 1150));
        Assert.True(Age(1000, 2000) > Age(1000, 1100));
    }
}
