using System.Text.Json;
using System.Text.Json.Serialization;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Control Center configuration. Defaults match the 10.6.1.40 deployment;
/// overrides live in the gitignored runtime/config/controlcenter.json.
/// </summary>
public sealed class ControlCenterConfig
{
    [JsonPropertyName("platform_root")] public string PlatformRoot { get; set; } = "";
    [JsonPropertyName("monitor_root")] public string MonitorRoot { get; set; } = "";
    [JsonPropertyName("detector_root")] public string DetectorRoot { get; set; } = "";
    [JsonPropertyName("lan_host")] public string LanHost { get; set; } = "10.6.1.40";
    /// <summary>Canonical public browser origin (monitor + MediaMTX allow origin + kiosk).</summary>
    [JsonPropertyName("public_origin")] public string PublicOrigin { get; set; } = "http://10.6.1.40:8080";
    [JsonPropertyName("production_stream")] public string ProductionStream { get; set; } = "zahradky-horni-stanice";
    [JsonPropertyName("mediamtx_api_base")] public string MediaMtxApiBase { get; set; } = "http://127.0.0.1:9997";
    [JsonPropertyName("whep_base_local")] public string WhepBaseLocal { get; set; } = "http://127.0.0.1:8889";
    [JsonPropertyName("event_core_base_local")] public string EventCoreBaseLocal { get; set; } = "http://127.0.0.1:8000";
    [JsonPropertyName("monitor_base_local")] public string MonitorBaseLocal { get; set; } = "http://127.0.0.1:8080";
    /// <summary>When true, OPERATIONS starts production monitor (Nitro+Event Core), never Vite dev.</summary>
    [JsonPropertyName("use_production_monitor")] public bool UseProductionMonitor { get; set; } = true;
    [JsonPropertyName("kiosk_path")] public string KioskPath { get; set; } = "/kiosk/zahradky/horni-stanice";
    /// <summary>Optional detector start command; empty = detector NOT CONFIGURED (Phase 3).</summary>
    [JsonPropertyName("detector_start_command")] public string DetectorStartCommand { get; set; } = "";
    [JsonPropertyName("detector_process_hint")] public string DetectorProcessHint { get; set; } = "zahradky_horni_pad";
    [JsonPropertyName("readiness_timeout_seconds")] public int ReadinessTimeoutSeconds { get; set; } = 60;

    [JsonIgnore] public string RuntimeDir => Path.Combine(PlatformRoot, "runtime");
    [JsonIgnore] public string LogsDir => Path.Combine(PlatformRoot, "runtime", "logs");
    [JsonIgnore] public string CamerasJsonPath => RuntimeConfigPaths.Cameras(this);
    [JsonIgnore] public string StreamsJsonPath => RuntimeConfigPaths.Streams(this);
    [JsonIgnore] public string DetectorsJsonPath => RuntimeConfigPaths.Detectors(this);
    [JsonIgnore] public string NotificationsJsonPath => RuntimeConfigPaths.Notifications(this);
    [JsonIgnore] public string HardwareJsonPath => RuntimeConfigPaths.Hardware(this);
    [JsonIgnore] public string ScenariosJsonPath => RuntimeConfigPaths.Scenarios(this);
    [JsonIgnore] public string TestStationsJsonPath => RuntimeConfigPaths.TestStations(this);
    [JsonIgnore] public string RoiDir => RuntimeConfigPaths.RoiDir(this);
    [JsonIgnore] public string ConfigJsonPath => Path.Combine(PlatformRoot, "runtime", "config", "controlcenter.json");
    [JsonIgnore] public string MediaMtxLocalYml => Path.Combine(PlatformRoot, "deploy", "mediamtx", "mediamtx.local.yml");
    [JsonIgnore] public string ScriptsDir => Path.Combine(PlatformRoot, "scripts");
    [JsonIgnore] public string ResolvedPublicOrigin =>
        string.IsNullOrWhiteSpace(PublicOrigin) ? $"http://{LanHost}:8080" : PublicOrigin.TrimEnd('/');

    public string DashboardUrl => $"{ResolvedPublicOrigin}/dashboard";
    public string KioskUrl => $"{ResolvedPublicOrigin}{(KioskPath.StartsWith('/') ? KioskPath : "/" + KioskPath)}";
    public string PreviewUrl(string mediaMtxPath) => $"{WhepBaseLocal}/{mediaMtxPath}/";
    public string MonitorRuntimeStatusUrl => $"{ResolvedPublicOrigin}/api/v1/health";
    public string ChromeKioskManageScript => Path.Combine(ScriptsDir, "manage_operator_kiosk.ps1");
    public string ProductionMonitorStartScript => Path.Combine(ScriptsDir, "start_production_monitor.ps1");
    public string ProductionMonitorStopScript => Path.Combine(ScriptsDir, "stop_production_monitor.ps1");
    public string DevMonitorStartScript => Path.Combine(MonitorRoot, "scripts", "start_internal_monitor.ps1");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Locates the platform repo by walking up from the app base dir; falls back to the known dev path.</summary>
    public static ControlCenterConfig LoadOrDefault(string? baseDir = null)
    {
        var platformRoot = FindPlatformRoot(baseDir ?? AppContext.BaseDirectory)
                           ?? @"C:\Users\mega\Documents\cableguard-platform";
        var parent = Path.GetDirectoryName(platformRoot) ?? platformRoot;

        var config = new ControlCenterConfig
        {
            PlatformRoot = platformRoot,
            MonitorRoot = Path.Combine(parent, "cableguard-monitor"),
            DetectorRoot = Path.Combine(parent, "cableguard-detector"),
        };

        var overridePath = config.ConfigJsonPath;
        if (File.Exists(overridePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<ControlCenterConfig>(File.ReadAllText(overridePath));
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.PlatformRoot)) loaded.PlatformRoot = platformRoot;
                    if (string.IsNullOrWhiteSpace(loaded.MonitorRoot)) loaded.MonitorRoot = config.MonitorRoot;
                    if (string.IsNullOrWhiteSpace(loaded.DetectorRoot)) loaded.DetectorRoot = config.DetectorRoot;
                    return loaded;
                }
            }
            catch (JsonException)
            {
                // Corrupt override file: fall back to defaults rather than failing to start.
            }
        }
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigJsonPath)!);
        File.WriteAllText(ConfigJsonPath, JsonSerializer.Serialize(this, JsonOpts));
    }

    private static string? FindPlatformRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "scripts", "start_mediamtx.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
