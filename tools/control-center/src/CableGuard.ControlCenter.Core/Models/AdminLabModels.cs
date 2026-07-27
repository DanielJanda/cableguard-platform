using System.Text.Json.Serialization;

namespace CableGuard.ControlCenter.Core.Models;

public enum DetectorType
{
    Fall,
    Barrier,
}

public enum AdminMode
{
    Operations,
    TestLab,
}

public sealed class DetectorInstance
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("detector_type")] public string DetectorType { get; set; } = "fall";
    [JsonPropertyName("input_stream")] public string InputStream { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("roi_profile")] public string RoiProfile { get; set; } = "";
    [JsonPropertyName("device")] public string Device { get; set; } = "cuda:0";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("debug_overlay")] public bool DebugOverlay { get; set; }
    [JsonPropertyName("publish_jsonl")] public bool PublishJsonl { get; set; } = true;
    [JsonPropertyName("publish_event_core")] public bool PublishEventCore { get; set; }
    [JsonPropertyName("publish_telegram")] public bool PublishTelegram { get; set; }
    [JsonPropertyName("script_relative")] public string ScriptRelative { get; set; } = "";
    [JsonPropertyName("process_hint")] public string ProcessHint { get; set; } = "";
    [JsonPropertyName("extra_args")] public List<string> ExtraArgs { get; set; } = new();
}

public sealed class DetectorsDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("instances")] public List<DetectorInstance> Instances { get; set; } = new();
}

/// <summary>ROI polygon profile — stored under runtime/config/roi/{id}.json. Not algorithm thresholds.</summary>
public sealed class RoiProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("stream_id")] public string StreamId { get; set; } = "";
    [JsonPropertyName("detector_type")] public string DetectorType { get; set; } = "fall";
    [JsonPropertyName("points")] public List<RoiPoint> Points { get; set; } = new();
    [JsonPropertyName("is_production")] public bool IsProduction { get; set; }
}

public sealed class RoiPoint
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    public RoiPoint() { }
    public RoiPoint(int x, int y) { X = x; Y = y; }
}

public sealed class NotificationsDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("telegram_enabled")] public bool TelegramEnabled { get; set; }
    [JsonPropertyName("telegram_credential_ref")] public string TelegramCredentialRef { get; set; } = "CableGuard.Telegram.AdminAlerts";
    [JsonPropertyName("telegram_target_label")] public string TelegramTargetLabel { get; set; } = "CableGuard Admin Alerts";
    [JsonPropertyName("notify_fall")] public bool NotifyFall { get; set; } = true;
    [JsonPropertyName("notify_barrier")] public bool NotifyBarrier { get; set; } = true;
    [JsonPropertyName("notify_technical_errors")] public bool NotifyTechnicalErrors { get; set; } = true;
    [JsonPropertyName("event_core_publisher_enabled")] public bool EventCorePublisherEnabled { get; set; }
}

public sealed class HardwareDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("device_description")] public string DeviceDescription { get; set; } = "USB-4761";
    [JsonPropertyName("relay_server_host")] public string RelayServerHost { get; set; } = "127.0.0.1";
    [JsonPropertyName("relay_server_port")] public int RelayServerPort { get; set; } = 9877;
    [JsonPropertyName("auto_off_seconds")] public int AutoOffSeconds { get; set; } = 2;
}

public sealed class ScenarioDocument
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("stream_id")] public string StreamId { get; set; } = "";
    [JsonPropertyName("detector_ids")] public List<string> DetectorIds { get; set; } = new();
    [JsonPropertyName("roi_profile")] public string RoiProfile { get; set; } = "";
    [JsonPropertyName("debug_overlay")] public bool DebugOverlay { get; set; }
    [JsonPropertyName("telegram")] public bool Telegram { get; set; }
    [JsonPropertyName("event_core")] public bool EventCore { get; set; }
    [JsonPropertyName("hardware_test")] public bool HardwareTest { get; set; }
}

public sealed class ScenariosDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("scenarios")] public List<ScenarioDocument> Scenarios { get; set; } = new();
}

/// <summary>Read-only fall algorithm parameters — NEVER editable via Control Center.</summary>
public static class FallAlgorithmInfo
{
    public const string Note =
        "ANGLE / TORSO / MOV / AXIS / BOX / weights / risk threshold are frozen and protected by golden-master tests. " +
        "Edit only via detector algorithm change + new version + tests — never via Admin GUI.";
}
