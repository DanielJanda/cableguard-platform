using System.Text.Json.Serialization;

namespace CableGuard.ControlCenter.Core.Models;

/// <summary>
/// Shared office/production-like test station binding.
/// Runtime copy lives under gitignored runtime/config/test-stations.json.
/// Never stores credentials or camera passwords.
/// </summary>
public sealed class TestStationProfile
{
    [JsonPropertyName("station_id")] public string StationId { get; set; } = "";
    [JsonPropertyName("site_id")] public string SiteId { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("camera_id")] public string CameraId { get; set; } = "";
    [JsonPropertyName("video_stream")] public string VideoStream { get; set; } = "";
    [JsonPropertyName("fall_service_id")] public string FallServiceId { get; set; } = "";
    [JsonPropertyName("roi_profile")] public string RoiProfile { get; set; } = "";
    [JsonPropertyName("monitor_path")] public string MonitorPath { get; set; } = "/test-lab/office-fall";
    /// <summary>test | production — office E2E uses test.</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "test";
}

public sealed class TestStationsDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("stations")] public List<TestStationProfile> Stations { get; set; } = new();
}
