using System.Text.Json.Serialization;

namespace CableGuard.ControlCenter.Core.Models;

/// <summary>Video transport health — NEVER equals glass-to-glass latency.</summary>
public enum VideoHealthState
{
    Unknown,
    Offline,
    Stale,
    Degraded,
    Realtime,
}

public enum MeasurementKind
{
    Measured,
    Configured,
    Unknown,
    NotAvailable,
    NotMeasured,
    Experimental,
}

public enum QualificationVerdict
{
    EngineeringPass,
    EngineeringFail,
    Incomplete,
}

public sealed class MetricValue
{
    [JsonPropertyName("kind")] public MeasurementKind Kind { get; set; } = MeasurementKind.Unknown;
    [JsonPropertyName("value")] public double? Value { get; set; }
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";

    public static MetricValue Unknown(string note = "") => new() { Kind = MeasurementKind.Unknown, Note = note };
    public static MetricValue NotAvailable(string note) => new() { Kind = MeasurementKind.NotAvailable, Note = note };
    public static MetricValue NotMeasured(string note) => new() { Kind = MeasurementKind.NotMeasured, Note = note };
    public static MetricValue Measured(double value, string unit, string note = "") =>
        new() { Kind = MeasurementKind.Measured, Value = value, Unit = unit, Note = note };
    public static MetricValue Configured(double value, string unit) =>
        new() { Kind = MeasurementKind.Configured, Value = value, Unit = unit };

    public override string ToString() => Kind switch
    {
        MeasurementKind.Measured => $"{Value:0.###} {Unit}".Trim(),
        MeasurementKind.Configured => $"{Value:0.###} {Unit} (configured)".Trim(),
        MeasurementKind.NotMeasured => "NOT MEASURED",
        MeasurementKind.NotAvailable => "NOT AVAILABLE",
        MeasurementKind.Experimental => $"EXPERIMENTAL {Value:0.###} {Unit}".Trim(),
        _ => "unknown",
    };
}

public sealed class StreamLiveMetrics
{
    public string StreamId { get; set; } = "";
    public string MediaMtxPath { get; set; } = "";
    public string SourceCameraId { get; set; } = "";
    public VideoHealthState Health { get; set; } = VideoHealthState.Unknown;
    public string HealthDetail { get; set; } = "";

    public MetricValue Codec { get; set; } = MetricValue.Unknown();
    public MetricValue ResolutionWidth { get; set; } = MetricValue.Unknown();
    public MetricValue ResolutionHeight { get; set; } = MetricValue.Unknown();
    public MetricValue ConfiguredFps { get; set; } = MetricValue.Unknown();
    public MetricValue ReceivedFps { get; set; } = MetricValue.NotAvailable("Requires browser WHEP probe");
    public MetricValue BitrateKbps { get; set; } = MetricValue.Unknown();
    public MetricValue FramesReceived { get; set; } = MetricValue.NotAvailable("Requires browser WHEP probe");
    public MetricValue FramesDecoded { get; set; } = MetricValue.NotAvailable("Requires browser WHEP probe");
    public MetricValue FramesDropped { get; set; } = MetricValue.NotAvailable("Requires browser WHEP probe");
    public MetricValue PacketLoss { get; set; } = MetricValue.NotAvailable("Requires browser WHEP probe or MediaMTX metrics");
    public MetricValue JitterMs { get; set; } = MetricValue.NotAvailable("Requires browser WHEP probe");
    public MetricValue RttMs { get; set; } = MetricValue.NotAvailable("Requires browser WHEP probe");
    public string IceState { get; set; } = "unknown";
    public MetricValue FreezeCount { get; set; } = MetricValue.Measured(0, "count");
    public MetricValue FreezeDurationSec { get; set; } = MetricValue.Measured(0, "s");
    public MetricValue ReconnectCount { get; set; } = MetricValue.Measured(0, "count");
    public MetricValue SecondsSinceLastFrame { get; set; } = MetricValue.Unknown();
    public bool PathReady { get; set; }
    public MetricValue MediaMtxBytesReceived { get; set; } = MetricValue.Unknown();
    public MetricValue MediaMtxBytesSent { get; set; } = MetricValue.Unknown();
    public MetricValue MediaMtxReaders { get; set; } = MetricValue.Unknown();

    /// <summary>Always NOT MEASURED unless a physical Latency Test sample was recorded.</summary>
    public MetricValue GlassToGlassLatencyMs { get; set; } =
        MetricValue.NotMeasured("No physical visual reference measurement recorded");
}

public sealed class GlassToGlassSample
{
    public DateTimeOffset MeasuredAtUtc { get; set; }
    public string StreamId { get; set; } = "";
    public string Method { get; set; } = "MANUAL"; // MANUAL | EXPERIMENTAL_AUTO
    public double LatencyMs { get; set; }
    public string OperatorNote { get; set; } = "";
    public bool IsAuthoritative { get; set; } // false for experimental auto
}

public sealed class DetectorFreshnessSnapshot
{
    public string DetectorId { get; set; } = "";
    public MeasurementKind Availability { get; set; } = MeasurementKind.NotAvailable;
    public string Detail { get; set; } =
        "NOT AVAILABLE — detector diagnostics contract (frame_received/inference_* monotonic) not on main";
    public MetricValue InputFps { get; set; } = MetricValue.NotAvailable("diagnostics contract missing");
    public MetricValue InferenceFps { get; set; } = MetricValue.NotAvailable("diagnostics contract missing");
    public MetricValue QueueAgeMs { get; set; } = MetricValue.NotAvailable("diagnostics contract missing");
    public MetricValue InferenceDurationMs { get; set; } = MetricValue.NotAvailable("diagnostics contract missing");
    public MetricValue FrameToDecisionMs { get; set; } = MetricValue.NotAvailable("diagnostics contract missing");
    public bool BacklogDetected { get; set; }
}

public sealed class EngineeringThresholds
{
    // ENGINEERING / PROVISIONAL — never safety-certified
    public double MinReceivedFps { get; set; } = 12;
    public double MaxSecondsSinceLastFrame { get; set; } = 2.0;
    public double MaxFreezeCountPerHour { get; set; } = 5;
    public double MaxReconnectsPerHour { get; set; } = 3;
    public bool RequireGlassToGlassForPass { get; set; } = true;
    public double MaxGlassToGlassMs { get; set; } = 1500;
    public string Label { get; set; } = "ENGINEERING / PROVISIONAL — not safety certified";
}

public sealed class QualificationReport
{
    public string TestId { get; set; } = "";
    public QualificationVerdict Verdict { get; set; } = QualificationVerdict.Incomplete;
    public string VerdictLabel { get; set; } = "INCOMPLETE";
    public List<string> Reasons { get; set; } = new();
    public string ConfigFingerprint { get; set; } = "";
    public StreamLiveMetrics? Metrics { get; set; }
    public bool GlassToGlassMeasured { get; set; }
}

public sealed class SoakStatistics
{
    public string MetricName { get; set; } = "";
    public double? Mean { get; set; }
    public double? Median { get; set; }
    public double? P95 { get; set; }
    public double? P99 { get; set; }
    public double? Max { get; set; }
    public double? Min { get; set; }
    public string Note { get; set; } = "";
}

public sealed class ResourceSnapshot
{
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public double? SystemCpuPercent { get; set; }
    public double? SystemRamUsedMb { get; set; }
    public double? SystemRamTotalMb { get; set; }
    public Dictionary<string, ProcessResource> Processes { get; set; } = new();
}

public sealed class ProcessResource
{
    public string Name { get; set; } = "";
    public int? Pid { get; set; }
    public double? CpuPercent { get; set; }
    public double? WorkingSetMb { get; set; }
}
