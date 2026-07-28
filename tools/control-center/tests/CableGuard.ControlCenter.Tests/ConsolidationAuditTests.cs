using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class ConsolidationAuditTests
{
    [Fact]
    public void MediaMtx_CorrectPid_IsNoOp()
    {
        var d = MediaMtxLifecycleLogic.Decide(100, true, new[] { 100 }, null, false);
        Assert.Equal(MediaMtxLifecycleLogic.DecisionKind.NoOpAlreadyManaged, d.Kind);
        Assert.Equal(100, d.PidToAdopt);
    }

    [Fact]
    public void MediaMtx_StalePid_AdoptsLive()
    {
        var d = MediaMtxLifecycleLogic.Decide(100, false, new[] { 200 }, null, false);
        Assert.Equal(MediaMtxLifecycleLogic.DecisionKind.HealStalePidAndAdopt, d.Kind);
        Assert.Equal(200, d.PidToAdopt);
    }

    [Fact]
    public void MediaMtx_Orphan_Adopts()
    {
        var d = MediaMtxLifecycleLogic.Decide(null, false, new[] { 300 }, null, false);
        Assert.Equal(MediaMtxLifecycleLogic.DecisionKind.AdoptOrphan, d.Kind);
        Assert.Equal(300, d.PidToAdopt);
    }

    [Fact]
    public void MediaMtx_ForeignPortOwner_Refused()
    {
        var d = MediaMtxLifecycleLogic.Decide(null, false, Array.Empty<int>(), 999, portOwnerIsMediaMtx: false);
        Assert.Equal(MediaMtxLifecycleLogic.DecisionKind.RefuseForeignPortOwner, d.Kind);
    }

    [Fact]
    public void MediaMtx_MultipleProcesses_Refused()
    {
        var d = MediaMtxLifecycleLogic.Decide(null, false, new[] { 1, 2 }, null, false);
        Assert.Equal(MediaMtxLifecycleLogic.DecisionKind.RefuseMultipleProcesses, d.Kind);
    }

    [Fact]
    public void MediaMtx_HealPidFile_WritesExpected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cg-mtx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "mediamtx.pid");
        try
        {
            MediaMtxLifecycleLogic.HealPidFile(file, 4242);
            Assert.Equal("4242", File.ReadAllText(file).Trim());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Roi_SerializesSourceResolution_AndSavedState()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cg-roi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.json");
        try
        {
            var profile = new RoiProfile
            {
                Id = "test",
                StreamId = "office-test",
                DetectorType = "fall",
                RoiRole = "fall",
                SourceWidth = 1920,
                SourceHeight = 1080,
                ActivationState = "active", // must be forced to saved on Save
                Points = { new(0, 0), new(10, 0), new(10, 10) },
            };
            Assert.Contains(RoiProfileService.Validate(profile), e => e.Contains("saved"));
            profile.ActivationState = "saved";
            RoiProfileService.Save(profile, path);
            var loaded = RoiProfileService.Load(path);
            Assert.Equal(1920, loaded.SourceWidth);
            Assert.Equal(1080, loaded.SourceHeight);
            Assert.Equal("saved", loaded.ActivationState);
            Assert.False(string.Equals(loaded.ActivationState, "active", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Roi_NativeFramePoints_StayInPixelSpace()
    {
        // Click mapping is (x / displayW) * frameW — verify identity at corners.
        const int fw = 1920, fh = 1080;
        static (int x, int y) Map(double px, double py, double dw, double dh, int frameW, int frameH) =>
            ((int)(px / dw * frameW), (int)(py / dh * frameH));

        Assert.Equal((0, 0), Map(0, 0, 960, 540, fw, fh));
        Assert.Equal((1919, 1079), Map(959.5, 539.5, 960, 540, fw, fh));
    }

    [Fact]
    public void GlassToGlass_Fingerprint_MarksOutdated()
    {
        var samples = new List<GlassToGlassSample>
        {
            new()
            {
                StreamId = "s1",
                Method = "manual",
                LatencyMs = 180,
                ConfigurationFingerprint = "aaa",
                IsAuthoritative = true,
            }
        };
        VideoLabCollector.MarkOutdatedIfFingerprintChanged(samples, "s1", "bbb");
        Assert.True(samples[0].IsOutdated);
    }

    [Fact]
    public void GlassToGlass_SameFingerprint_StaysFresh()
    {
        var samples = new List<GlassToGlassSample>
        {
            new()
            {
                StreamId = "s1",
                Method = "manual",
                LatencyMs = 180,
                ConfigurationFingerprint = "same",
                IsAuthoritative = true,
            }
        };
        VideoLabCollector.MarkOutdatedIfFingerprintChanged(samples, "s1", "same");
        Assert.False(samples[0].IsOutdated);
    }

    [Fact]
    public void StreamLatencyFingerprint_ExcludesSecrets()
    {
        var payload = ConfigFingerprint.BuildStreamLatencyPayload(
            "cam", "stream", "path", 1280, 720, 25, "H264");
        var fp = ConfigFingerprint.Compute(payload);
        Assert.False(string.IsNullOrWhiteSpace(fp));
        Assert.Throws<InvalidOperationException>(() =>
            ConfigFingerprint.Compute(new { password = "x", stream_id = "s" }));
    }

    [Fact]
    public void LogRedactor_MasksRtspInGrabContext()
    {
        var line = "grab failed rtsp://user:secret@10.2.4.92:554/stream";
        var redacted = LogRedactor.Redact(line);
        Assert.DoesNotContain("secret", redacted);
        Assert.Contains("***:***@", redacted);
    }

    [Fact]
    public void StreamFrameGrabber_FindFfmpeg_DoesNotThrow()
    {
        // May be null in CI without ffmpeg — must not throw.
        var path = StreamFrameGrabber.FindFfmpeg();
        Assert.True(path is null || File.Exists(path));
    }
}
