using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public sealed class CameraDetectionLauncherTests
{
    [Fact]
    public void Resolve_office63_uses_pyav_rtsp_mediamtx_and_office_path()
    {
        var camera = new CameraEntry
        {
            CameraId = "office-63",
            DisplayName = "Kancelářská kamera",
            Environment = "test",
            MediaMtxPath = "office-test-camera",
            DetectorInputProfile = "pyav_rtsp",
            PreferredBackend = "pyav_rtsp",
            SourceMode = "mediamtx",
            EventGenerationAllowed = true,
        };
        var docs = new DetectorsDocument
        {
            Instances =
            {
                new DetectorInstance
                {
                    Id = "fall-office-test",
                    DetectorType = "fall",
                    InputStream = "office-test-camera",
                    ScriptRelative = "apps/zahradky_horni_pad.py",
                    Model = "yolo11m-pose.pt",
                    Device = "cuda:0",
                }
            }
        };

        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(camera, docs);
        var summary = CameraDetectionLauncher.BuildSafeSummary(camera, instance);
        var text = CameraDetectionLauncher.FormatOperatorSummary(summary);

        Assert.Equal("fall-office-test", instance.Id);
        Assert.Equal("pyav_rtsp", instance.InputProfile);
        Assert.Equal("mediamtx", instance.SourceMode);
        Assert.Equal("office-test-camera", instance.InputStream);
        Assert.DoesNotContain("rtsp://", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("camera=Kancelářská kamera", text);
        Assert.Contains("environment=TEST", text);
    }

    [Fact]
    public void LaunchBuilder_for_resolved_office_sets_mediamtx_env_without_credentials()
    {
        var instance = new DetectorInstance
        {
            Id = "fall-office-test",
            DetectorType = "fall",
            InputStream = "office-test-camera",
            InputProfile = "pyav_rtsp",
            SourceMode = "mediamtx",
            ScriptRelative = "apps/zahradky_horni_pad.py",
            Model = "yolo11m-pose.pt",
            Device = "cuda:0",
            Enabled = true,
        };
        var config = new ControlCenterConfig
        {
            PlatformRoot = Path.GetTempPath(),
            DetectorRoot = Path.GetTempPath(),
        };
        var spec = DetectorLaunchBuilder.Build(instance, config);
        Assert.Equal("pyav_rtsp", spec.Environment["CABLEGUARD_FALL_INPUT_PROFILE"]);
        Assert.Equal("mediamtx", spec.Environment["CABLEGUARD_FALL_SOURCE_MODE"]);
        Assert.Equal("rtsp://127.0.0.1:8554/office-test-camera", spec.Environment["CABLEGUARD_MEDIAMTX_RTSP_URL"]);
        Assert.Equal("http://127.0.0.1:8000", spec.Environment["CABLEGUARD_EVENT_CORE_URL"]);
        Assert.DoesNotContain(spec.Arguments, a => a.Contains("@") && a.Contains("rtsp://"));
    }

    [Fact]
    public void LaunchBuilder_with_ingest_key_enables_event_core_events()
    {
        var root = Path.Combine(Path.GetTempPath(), "cg-cc-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable("CABLEGUARD_INGEST_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CABLEGUARD_INGEST_API_KEY", null);
            File.WriteAllText(Path.Combine(root, ".env"), "CABLEGUARD_INGEST_API_KEY=test-ingest-key-not-real\n");
            var instance = new DetectorInstance
            {
                Id = "fall-office-test",
                DetectorType = "fall",
                InputStream = "office-test-camera",
                InputProfile = "pyav_rtsp",
                SourceMode = "mediamtx",
                ScriptRelative = "apps/zahradky_horni_pad.py",
                PublishEventCore = true,
                Enabled = true,
            };
            var config = new ControlCenterConfig
            {
                PlatformRoot = root,
                DetectorRoot = Path.GetTempPath(),
                EventCoreBaseLocal = "http://127.0.0.1:8000",
            };
            var spec = DetectorLaunchBuilder.Build(instance, config);
            Assert.Equal("true", spec.Environment["CABLEGUARD_EVENT_CORE_EVENTS"]);
            Assert.Equal("http://127.0.0.1:8000", spec.Environment["CABLEGUARD_EVENT_CORE_URL"]);
            Assert.Equal("test-ingest-key-not-real", spec.Environment["CABLEGUARD_INGEST_API_KEY"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CABLEGUARD_INGEST_API_KEY", prev);
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LaunchBuilder_without_ingest_key_disables_events_to_avoid_config_exit()
    {
        var root = Path.Combine(Path.GetTempPath(), "cg-cc-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var prev = Environment.GetEnvironmentVariable("CABLEGUARD_INGEST_API_KEY");
            Environment.SetEnvironmentVariable("CABLEGUARD_INGEST_API_KEY", null);
            try
            {
                var instance = new DetectorInstance
                {
                    Id = "fall-office-test",
                    DetectorType = "fall",
                    InputStream = "office-test-camera",
                    InputProfile = "pyav_rtsp",
                    SourceMode = "mediamtx",
                    ScriptRelative = "apps/zahradky_horni_pad.py",
                    PublishEventCore = true,
                    Enabled = true,
                };
                var config = new ControlCenterConfig
                {
                    PlatformRoot = root,
                    DetectorRoot = Path.GetTempPath(),
                };
                var spec = DetectorLaunchBuilder.Build(instance, config);
                Assert.Equal("false", spec.Environment["CABLEGUARD_EVENT_CORE_EVENTS"]);
                Assert.Equal("http://127.0.0.1:8000", spec.Environment["CABLEGUARD_EVENT_CORE_URL"]);
                Assert.False(spec.Environment.ContainsKey("CABLEGUARD_INGEST_API_KEY"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("CABLEGUARD_INGEST_API_KEY", prev);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
