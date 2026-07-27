using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class AdminLabServicesTests
{
    [Fact]
    public void InvalidIp_IsRejected()
    {
        var cam = new CameraEntry
        {
            CameraId = "x", DisplayName = "X", Host = "999.1.2.3", RtspPort = 554,
            CredentialRef = "ref", SiteId = "s", StationId = "st",
        };
        var errors = CameraRegistryService.ValidateCamera(cam);
        Assert.Contains(errors, e => e.Contains("invalid host"));
    }

    [Fact]
    public void ValidHostname_IsAccepted()
    {
        Assert.True(CameraRegistryService.IsValidHost("cam-office.local"));
        Assert.True(CameraRegistryService.IsValidHost("10.6.1.123"));
    }

    [Fact]
    public void Streams_UnknownCamera_FailsValidation()
    {
        var doc = new StreamsDocument
        {
            Streams = { new LogicalStream { StreamId = "office", MediaMtxPath = "office", CameraId = "missing" } }
        };
        var errors = StreamsService.Validate(doc, new[] { "office-cam" });
        Assert.Contains(errors, e => e.Contains("unknown camera"));
    }

    [Fact]
    public void DetectorLaunch_Fall_UsesMediaMtxProxy_NoCredentialsInArgs()
    {
        var instance = new DetectorInstance
        {
            Id = "fall-office",
            DetectorType = "fall",
            InputStream = "office-test",
            ScriptRelative = "apps/zahradky_horni_pad.py",
            DebugOverlay = true,
            PublishTelegram = true,
        };
        var config = new ControlCenterConfig
        {
            PlatformRoot = @"C:\tmp\platform",
            DetectorRoot = @"C:\tmp\detector",
        };
        var spec = DetectorLaunchBuilder.Build(instance, config, new NotificationsDocument { TelegramEnabled = false });
        var cmdline = DetectorLaunchBuilder.FormatCommandLine(spec);
        Assert.Contains("mediamtx_proxy", cmdline);
        Assert.Contains("--debug-overlay", cmdline);
        Assert.DoesNotContain("rtsp://", string.Join(" ", spec.Arguments));
        Assert.DoesNotContain("password", cmdline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("false", spec.Environment["TELEGRAM_ENABLED"]);
        Assert.Contains("office-test", spec.Environment["CABLEGUARD_MEDIAMTX_RTSP_URL"]);
    }

    [Fact]
    public void RoiProfile_RequiresAtLeastThreePoints()
    {
        var profile = new RoiProfile { Id = "office", Points = { new(1, 1), new(2, 2) } };
        Assert.Contains(RoiProfileService.Validate(profile), e => e.Contains("3–32"));
    }

    [Fact]
    public void RoiProfile_SerializesAndLoads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cg-roi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profile = new RoiProfile
            {
                Id = "office-fall",
                DisplayName = "Office",
                StreamId = "office-test",
                Points = { new(10, 10), new(100, 10), new(100, 80), new(10, 80) },
            };
            var path = Path.Combine(dir, "office-fall.json");
            RoiProfileService.Save(profile, path);
            var loaded = RoiProfileService.Load(path);
            Assert.Equal(4, loaded.Points.Count);
            Assert.Contains("[10, 10]", RoiProfileService.ToYamlPointsLiteral(loaded));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ScenarioDiff_ReportsStartStopAndFlagChanges()
    {
        var scenario = new ScenarioDocument
        {
            Id = "office",
            DetectorIds = { "fall-office" },
            Telegram = false,
            EventCore = false,
            HardwareTest = true,
            DebugOverlay = true,
            StreamId = "office-test",
        };
        var diff = ScenarioService.Diff(scenario, currentTelegram: true, currentEventCore: false,
            currentHardwareTest: false, runningDetectorIds: new[] { "fall-zahradky" });
        Assert.Contains(diff, d => d.Contains("fall-office") && d.Contains("START"));
        Assert.Contains(diff, d => d.Contains("fall-zahradky") && d.Contains("STOP"));
        Assert.Contains(diff, d => d.Contains("telegram"));
        Assert.Contains(diff, d => d.Contains("hardware_test"));
    }

    [Fact]
    public void Notifications_RefuseLiteralToken()
    {
        var path = Path.Combine(Path.GetTempPath(), "notif-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"version":1,"telegram_token":"123:ABC","telegram_enabled":true}""");
            // Load is fine; Save of a doc that somehow got a token field is guarded via credential_ref model.
            var doc = NotificationsService.Load(path);
            Assert.True(string.IsNullOrEmpty(
                typeof(NotificationsDocument).GetProperty("TelegramToken")?.ToString()));
            NotificationsService.Save(doc, path); // model has no token property — OK
            Assert.DoesNotContain("123:ABC", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void HardwareSafety_RequiresTestMode()
    {
        var hw = new NotAvailableHardwareAdapter { IsTestMode = false };
        Assert.Throws<InvalidOperationException>(() => HardwareSafety.EnsureTestMode(hw));
        hw.IsTestMode = true;
        Assert.Throws<InvalidOperationException>(() => HardwareSafety.EnsureTestMode(hw)); // still not available
    }

    [Fact]
    public void HardwarePulse_IsClamped()
    {
        var clamped = HardwareSafety.ClampPulse(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), clamped);
    }

    [Fact]
    public void ConfigSave_IsAtomic_Cameras()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cg-cam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cameras.json");
        try
        {
            var doc = new CameraRegistryDocument
            {
                Cameras =
                {
                    new CameraEntry
                    {
                        CameraId = "office-1", DisplayName = "Office", Host = "10.6.1.123",
                        RtspPort = 554, CredentialRef = "CableGuard.Camera.office-1",
                        SiteId = "office", StationId = "desk", Transport = "tcp", Profile = "102",
                    }
                }
            };
            CameraRegistryService.Save(doc, path);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Empty(CameraRegistryService.Validate(File.ReadAllText(path)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FallAlgorithmInfo_IsReadOnlyNote()
    {
        Assert.Contains("golden-master", FallAlgorithmInfo.Note);
        Assert.Contains("never via Admin GUI", FallAlgorithmInfo.Note);
    }
}
