using System.Text.Json.Serialization;

namespace CableGuard.ControlCenter.Core.Models;

/// <summary>
/// Physical camera entry in runtime/config/cameras.json.
/// Never contains a password: only a credential_ref into Windows Credential Manager.
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
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("credential_ref")] public string CredentialRef { get; set; } = "";
    [JsonPropertyName("mediamtx_path")] public string MediaMtxPath { get; set; } = "";
}

/// <summary>Maps a stable logical stream name to the physical camera currently feeding it.</summary>
public sealed class StreamMapping
{
    [JsonPropertyName("logical_stream")] public string LogicalStream { get; set; } = "";
    [JsonPropertyName("primary_camera_id")] public string PrimaryCameraId { get; set; } = "";
}

public sealed class CameraRegistryDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("cameras")] public List<CameraEntry> Cameras { get; set; } = new();
    [JsonPropertyName("stream_mappings")] public List<StreamMapping> StreamMappings { get; set; } = new();
}
