using System.Text.Json.Serialization;

namespace CableGuard.ControlCenter.Core.Models;

/// <summary>Logical Hikvision/ONVIF stream role on a physical camera.</summary>
public enum CameraStreamType
{
    Main,
    Sub,
    Third,
    Unknown,
}

public enum CameraAuditStatus
{
    Unknown,
    Compliant,
    Drifted,
    Unreachable,
    Unsupported,
}

/// <summary>
/// One discoverable camera stream profile (e.g. Hikvision channel 101/102/103).
/// Stored under cameras.json → stream_profiles (or sidecar runtime file).
/// Never contains credentials.
/// </summary>
public sealed class CameraStreamProfile
{
    [JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("camera_id")] public string CameraId { get; set; } = "";
    [JsonPropertyName("channel_id")] public int ChannelId { get; set; }
    [JsonPropertyName("stream_type")] public string StreamType { get; set; } = "unknown"; // main|sub|third
    [JsonPropertyName("rtsp_path")] public string RtspPath { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    [JsonPropertyName("current")] public StreamConfigSnapshot Current { get; set; } = new();
    [JsonPropertyName("capabilities")] public StreamCapabilities Capabilities { get; set; } = new();
    [JsonPropertyName("observed")] public StreamObservedSnapshot Observed { get; set; } = new();
    [JsonPropertyName("last_audited_at")] public string? LastAuditedAt { get; set; }
    [JsonPropertyName("audit_status")] public string AuditStatus { get; set; } = "unknown";
    [JsonPropertyName("audit_message")] public string AuditMessage { get; set; } = "";
    [JsonPropertyName("configuration_fingerprint")] public string ConfigurationFingerprint { get; set; } = "";
}

public sealed class StreamConfigSnapshot
{
    [JsonPropertyName("encoding")] public string? Encoding { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("fps")] public double? Fps { get; set; }
    [JsonPropertyName("bitrate_type")] public string? BitrateType { get; set; }
    [JsonPropertyName("bitrate_kbps")] public int? BitrateKbps { get; set; }
    [JsonPropertyName("h264_profile")] public string? H264Profile { get; set; }
    [JsonPropertyName("gov_length")] public int? GovLength { get; set; }
    [JsonPropertyName("smart_codec")] public bool? SmartCodec { get; set; }
    [JsonPropertyName("svc")] public bool? Svc { get; set; }
    [JsonPropertyName("audio")] public bool? Audio { get; set; }
}

public sealed class StreamCapabilities
{
    [JsonPropertyName("encodings")] public List<string> Encodings { get; set; } = new();
    [JsonPropertyName("h264_supported")] public bool H264Supported { get; set; }
    [JsonPropertyName("h265_supported")] public bool H265Supported { get; set; }
    [JsonPropertyName("mjpeg_supported")] public bool MjpegSupported { get; set; }
    [JsonPropertyName("resolutions")] public List<string> Resolutions { get; set; } = new();
    [JsonPropertyName("h264_profiles")] public List<string> H264Profiles { get; set; } = new();
    [JsonPropertyName("fps_options")] public List<string> FpsOptions { get; set; } = new();
    [JsonPropertyName("gov_min")] public int? GovMin { get; set; }
    [JsonPropertyName("gov_max")] public int? GovMax { get; set; }
    [JsonPropertyName("svc_supported")] public bool? SvcSupported { get; set; }
    [JsonPropertyName("audio_optional")] public bool? AudioOptional { get; set; }
    [JsonPropertyName("raw_note")] public string RawNote { get; set; } = "";
}

public sealed class StreamObservedSnapshot
{
    [JsonPropertyName("codec_name")] public string? CodecName { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("avg_frame_rate")] public string? AvgFrameRate { get; set; }
    [JsonPropertyName("r_frame_rate")] public string? RFrameRate { get; set; }
    [JsonPropertyName("fps")] public double? Fps { get; set; }
    [JsonPropertyName("bitrate")] public string? Bitrate { get; set; }
    [JsonPropertyName("profile")] public string? Profile { get; set; }
    [JsonPropertyName("has_b_frames")] public bool? HasBFrames { get; set; }
    [JsonPropertyName("pix_fmt")] public string? PixFmt { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>Engineering validated CableGuard profile — PROVISIONAL until Video Qualification passes.</summary>
public sealed class ValidatedCameraProfile
{
    [JsonPropertyName("validated_profile_id")] public string ValidatedProfileId { get; set; } = "";
    [JsonPropertyName("provisional")] public bool Provisional { get; set; } = true;
    [JsonPropertyName("validated_at")] public string? ValidatedAt { get; set; }
    [JsonPropertyName("camera_model")] public string CameraModel { get; set; } = "";
    [JsonPropertyName("firmware")] public string Firmware { get; set; } = "";
    [JsonPropertyName("channel_id")] public int ChannelId { get; set; }
    [JsonPropertyName("stream_type")] public string StreamType { get; set; } = "sub";
    [JsonPropertyName("encoding")] public string Encoding { get; set; } = "H.264";
    [JsonPropertyName("width")] public int Width { get; set; } = 1280;
    [JsonPropertyName("height")] public int Height { get; set; } = 720;
    [JsonPropertyName("fps")] public double Fps { get; set; } = 25;
    [JsonPropertyName("bitrate_type")] public string BitrateType { get; set; } = "CBR";
    [JsonPropertyName("bitrate_kbps")] public int BitrateKbps { get; set; } = 2048;
    [JsonPropertyName("gov_length")] public int GovLength { get; set; } = 25;
    [JsonPropertyName("h264_profile")] public string H264Profile { get; set; } = "Main";
    [JsonPropertyName("smart_codec")] public bool SmartCodec { get; set; }
    [JsonPropertyName("svc")] public bool Svc { get; set; }
    [JsonPropertyName("audio")] public bool Audio { get; set; }
    [JsonPropertyName("transport")] public string Transport { get; set; } = "tcp";
    [JsonPropertyName("configuration_fingerprint")] public string ConfigurationFingerprint { get; set; } = "";
    [JsonPropertyName("qualification_report_id")] public string? QualificationReportId { get; set; }
    [JsonPropertyName("note")] public string Note { get; set; } =
        "PROVISIONAL CableGuard baseline — must be confirmed by Video Qualification Lab.";
}

public sealed class DriftChange
{
    [JsonPropertyName("field")] public string Field { get; set; } = "";
    [JsonPropertyName("expected")] public string Expected { get; set; } = "";
    [JsonPropertyName("actual")] public string Actual { get; set; } = "";
}

public sealed class DriftReport
{
    [JsonPropertyName("status")] public string Status { get; set; } = "unknown";
    [JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("changes")] public List<DriftChange> Changes { get; set; } = new();
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}
