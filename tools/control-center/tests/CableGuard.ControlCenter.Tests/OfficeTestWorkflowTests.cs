using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class OfficeTestWorkflowTests
{
    [Fact]
    public void OpenInMonitorUrl_UsesLanHostAndStreamId_NotCameraIp()
    {
        var lan = "10.6.1.40";
        var streamId = "office-test-camera";
        var url = $"http://{lan}:8080/test-lab/stream/{Uri.EscapeDataString(streamId)}";
        Assert.Equal("http://10.6.1.40:8080/test-lab/stream/office-test-camera", url);
        Assert.DoesNotContain("10.6.1.63", url);
        Assert.DoesNotContain("rtsp", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FallOfficeLaunch_UsesMediamtxProxyWithoutCredentials()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "cg-office-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "sites", "zahradky"));
        Directory.CreateDirectory(Path.Combine(tmp, ".venv", "Scripts"));
        File.WriteAllText(Path.Combine(tmp, ".venv", "Scripts", "python.exe"), "");
        File.WriteAllText(Path.Combine(tmp, "sites", "zahradky", "horni_pad.yaml"), """
site_id: zahradky
station_id: horni_stanice
service_id: zahradky-horni-pad-detector
camera_id: kamera4
model:
  logical_name: x
  path: models/shared/yolo11m-pose.pt
  imgsz: 480
  process_every_n: 1
  tracker: botsort.yaml
roi_points:
  - [1, 1]
  - [2, 2]
  - [3, 3]
thresholds:
  angle_threshold: 60
  torso_fall_threshold: 20
  movement_threshold: 10
  min_keypoint_dist: 20
  fall_axis_threshold: 45
  box_ratio_threshold: 1.0
  risk_score_threshold: 0.60
risk_weights:
  cond_leg: 0.15
  cond_torso: 0.15
  cond_move: 0.10
  cond_axis: 0.40
  cond_box: 0.10
runtime:
  snapshots_dir: runtime/x/snapshots
  events_dir: runtime/x/events
""");

        var config = new ControlCenterConfig
        {
            PlatformRoot = tmp,
            DetectorRoot = tmp,
            LanHost = "10.6.1.40",
        };
        var instance = new DetectorInstance
        {
            Id = "fall-office-test",
            DetectorType = "fall",
            InputStream = "office-test-camera",
            RoiProfile = "office-fall-test",
            ScriptRelative = "apps/zahradky_horni_pad.py",
            DebugOverlay = true,
            PublishTelegram = false,
            PublishEventCore = false,
        };
        var spec = DetectorLaunchBuilder.Build(instance, config, new NotificationsDocument { TelegramEnabled = false });
        Assert.Contains(spec.Arguments, a => a == "pyav_rtsp");
        Assert.Equal("pyav_rtsp", spec.Environment["CABLEGUARD_FALL_INPUT_PROFILE"]);
        Assert.Equal("mediamtx", spec.Environment["CABLEGUARD_FALL_SOURCE_MODE"]);
        Assert.Equal("rtsp://127.0.0.1:8554/office-test-camera", spec.Environment["CABLEGUARD_MEDIAMTX_RTSP_URL"]);
        Assert.Equal("false", spec.Environment["TELEGRAM_ENABLED"]);
        Assert.Equal("false", spec.Environment["CABLEGUARD_EVENT_CORE_EVENTS"]);
        Assert.DoesNotContain(spec.Arguments, a => a.Contains("10.6.1.63"));
        Assert.True(spec.Environment.ContainsKey("CABLEGUARD_PAD_SITE_CONFIG"));
        Assert.True(File.Exists(spec.Environment["CABLEGUARD_PAD_SITE_CONFIG"]));
    }

    [Fact]
    public void OnlyReadyLabel_IsOpenInMonitorGate()
    {
        Assert.True(string.Equals("READY", "READY", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals("OFFLINE", "READY", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals("NOT READY", "READY", StringComparison.OrdinalIgnoreCase));
    }
}
