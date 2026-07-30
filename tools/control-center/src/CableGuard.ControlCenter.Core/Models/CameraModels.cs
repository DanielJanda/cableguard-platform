using System.Text.Json.Serialization;

namespace CableGuard.ControlCenter.Core.Models;

/// <summary>
/// Physical camera entry. Never contains a password — only credential_ref.
/// Logical MediaMTX paths live in streams.json, not here.
/// </summary>
public sealed class CameraEntry
{
    [JsonPropertyName("camera_id")] public string CameraId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("site_id")] public string SiteId { get; set; } = "";
    [JsonPropertyName("station_id")] public string StationId { get; set; } = "";
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("rtsp_port")] public int RtspPort { get; set; } = 554;
    [JsonPropertyName("profile")] public string Profile { get; set; } = "";
    [JsonPropertyName("transport")] public string Transport { get; set; } = "tcp";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("credential_ref")] public string CredentialRef { get; set; } = "";
    /// <summary>Optional dedicated MediaMTX path for this physical camera (e.g. comparison path).</summary>
    [JsonPropertyName("mediamtx_path")] public string MediaMtxPath { get; set; } = "";
    /// <summary>configured_not_ready | ready | fault — never claim LIVE without MediaMTX READY.</summary>
    [JsonPropertyName("runtime_state")] public string RuntimeState { get; set; } = "configured_not_ready";
    /// <summary>Safe operator-facing last apply message (no credentials).</summary>
    [JsonPropertyName("last_apply_message")] public string LastApplyMessage { get; set; } = "";

    /// <summary>test | production — drives UI badges and test-event labeling.</summary>
    [JsonPropertyName("environment")] public string Environment { get; set; } = "production";
    /// <summary>Preferred detector CLI profile (e.g. pyav_rtsp).</summary>
    [JsonPropertyName("detector_input_profile")] public string DetectorInputProfile { get; set; } = "pyav_rtsp";
    /// <summary>Preferred backend label for status bar (usually same as detector_input_profile).</summary>
    [JsonPropertyName("preferred_backend")] public string PreferredBackend { get; set; } = "pyav_rtsp";
    /// <summary>mediamtx | direct_camera</summary>
    [JsonPropertyName("source_mode")] public string SourceMode { get; set; } = "mediamtx";
    [JsonPropertyName("recording_allowed")] public bool RecordingAllowed { get; set; }
    [JsonPropertyName("event_generation_allowed")] public bool EventGenerationAllowed { get; set; } = true;
}

/// <summary>Legacy inline mapping kept for cameras.json v1 compatibility.</summary>
public sealed class StreamMapping
{
    [JsonPropertyName("logical_stream")] public string LogicalStream { get; set; } = "";
    [JsonPropertyName("primary_camera_id")] public string PrimaryCameraId { get; set; } = "";
}

public sealed class CameraRegistryDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("cameras")] public List<CameraEntry> Cameras { get; set; } = new();
    /// <summary>Deprecated — prefer streams.json. Still loaded for migration.</summary>
    [JsonPropertyName("stream_mappings")] public List<StreamMapping> StreamMappings { get; set; } = new();
}

/// <summary>Logical stream — stable MediaMTX path consumed by frontend/detector.</summary>
public sealed class LogicalStream
{
    [JsonPropertyName("stream_id")] public string StreamId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("mediamtx_path")] public string MediaMtxPath { get; set; } = "";
    [JsonPropertyName("camera_id")] public string CameraId { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("is_production")] public bool IsProduction { get; set; }
}

public sealed class StreamsDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("streams")] public List<LogicalStream> Streams { get; set; } = new();
}
