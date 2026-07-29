using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class CameraStreamProfileTests
{
    [Theory]
    [InlineData(101, "main")]
    [InlineData(102, "sub")]
    [InlineData(103, "third")]
    [InlineData(201, "main")]
    [InlineData(202, "sub")]
    [InlineData(99, "unknown")]
    public void HikvisionChannel_MapsToStreamType(int channel, string expected)
    {
        Assert.Equal(expected, CameraProfileAuditService.StreamTypeForChannel(channel));
        Assert.Equal($"Streaming/Channels/{channel}", CameraProfileAuditService.RtspPathForChannel(channel));
    }

    [Fact]
    public void ParseChannel_FromProfilePath()
    {
        Assert.Equal(102, CameraProfileAuditService.ParseChannel("Streaming/Channels/102"));
        Assert.Equal(101, CameraProfileAuditService.ParseChannel("/Streaming/Channels/101"));
        Assert.Equal(103, CameraProfileAuditService.ParseChannel("103"));
        Assert.Null(CameraProfileAuditService.ParseChannel(""));
    }

    [Fact]
    public void LegacyCamera_MigratesToStreamProfiles()
    {
        var cam = new CameraEntry
        {
            CameraId = "camera-122727",
            Profile = "Streaming/Channels/102",
            Host = "10.6.1.63",
        };
        CameraProfileAuditService.EnsureMigrated(cam);
        Assert.Single(cam.StreamProfiles);
        Assert.Equal(102, cam.StreamProfiles[0].ChannelId);
        Assert.Equal("sub", cam.StreamProfiles[0].StreamType);
        Assert.Equal("camera-122727-ch102", cam.SelectedProfileId);
        // Idempotent
        CameraProfileAuditService.EnsureMigrated(cam);
        Assert.Single(cam.StreamProfiles);
    }

    [Fact]
    public void CameraWithMultipleProfiles_SelectAndBuildSource()
    {
        var cam = new CameraEntry
        {
            CameraId = "office",
            Host = "10.6.1.63",
            RtspPort = 554,
            Transport = "tcp",
            Profile = "Streaming/Channels/102",
            StreamProfiles =
            {
                new CameraStreamProfile
                {
                    ProfileId = "office-ch101", CameraId = "office", ChannelId = 101,
                    StreamType = "main", RtspPath = "Streaming/Channels/101",
                },
                new CameraStreamProfile
                {
                    ProfileId = "office-ch102", CameraId = "office", ChannelId = 102,
                    StreamType = "sub", RtspPath = "Streaming/Channels/102",
                },
            },
            SelectedProfileId = "office-ch102",
        };

        var subSrc = CameraRuntimeApplyService.BuildRtspSource(cam, "u", "p");
        Assert.Contains("/Streaming/Channels/102?", subSrc);

        var main = cam.StreamProfiles.First(p => p.ChannelId == 101);
        CameraRuntimeApplyService.SelectProfile(cam, main);
        var mainSrc = CameraRuntimeApplyService.BuildRtspSource(cam, "u", "p");
        Assert.Contains("/Streaming/Channels/101?", mainSrc);
        Assert.Equal("office-ch101", cam.SelectedProfileId);
        Assert.DoesNotContain("password", JsonSerializer.Serialize(cam));
    }

    [Fact]
    public void BackwardMigration_StreamsEnrichFromCamera()
    {
        var cams = new CameraRegistryDocument
        {
            Cameras =
            {
                new CameraEntry
                {
                    CameraId = "camera-122727",
                    DisplayName = "Office",
                    Host = "10.6.1.63",
                    CredentialRef = "CableGuard.Camera.x",
                    Profile = "Streaming/Channels/102",
                    MediaMtxPath = "office-test-camera",
                },
            },
        };
        CameraProfileAuditService.EnsureMigrated(cams.Cameras[0]);
        var streams = new StreamsDocument
        {
            Version = 1,
            Streams =
            {
                new LogicalStream
                {
                    StreamId = "office-test-camera",
                    MediaMtxPath = "office-test-camera",
                    CameraId = "camera-122727",
                },
            },
        };
        StreamsService.EnrichMissingProfileFields(cams, streams);
        Assert.Equal(2, streams.Version);
        Assert.Equal("camera-122727-ch102", streams.Streams[0].ProfileId);
        Assert.Equal("sub", streams.Streams[0].StreamType);
        Assert.Equal(102, streams.Streams[0].ChannelId);
    }

    [Fact]
    public void MigrateFromLegacy_EmptyStreams_CopiesMappingsWithProfile()
    {
        var cams = JsonSerializer.Deserialize<CameraRegistryDocument>("""
        {
          "version": 1,
          "cameras": [{
            "camera_id": "zahradky-upper-92",
            "display_name": "Horni",
            "host": "10.2.4.92",
            "credential_ref": "CableGuard.Camera.zahradky-upper-92",
            "profile": "Streaming/Channels/102"
          }],
          "stream_mappings": [
            { "logical_stream": "zahradky-horni-stanice", "primary_camera_id": "zahradky-upper-92" }
          ]
        }
        """)!;
        var doc = StreamsService.MigrateFromLegacy(cams, new StreamsDocument());
        Assert.Single(doc.Streams);
        Assert.Equal("zahradky-horni-stanice", doc.Streams[0].StreamId);
        Assert.Equal(102, doc.Streams[0].ChannelId);
        Assert.Equal("sub", doc.Streams[0].StreamType);
        Assert.True(doc.Streams[0].IsProduction);
    }

    [Fact]
    public void CapabilitiesVsCurrent_DriftDetection()
    {
        var profile = new CameraStreamProfile
        {
            ProfileId = "p1",
            ChannelId = 102,
            Current = new StreamConfigSnapshot
            {
                Encoding = "H.265",
                Width = 1920,
                Height = 1080,
                Fps = 25,
                BitrateType = "CBR",
                BitrateKbps = 4096,
                GovLength = 50,
                SmartCodec = true,
                Svc = false,
                Audio = false,
            },
        };
        var expected = CameraProfileAuditService.CreateProvisionalBaseline(102);
        var drift = CameraProfileAuditService.CompareToValidated(profile, expected);
        Assert.Equal("drifted", drift.Status);
        Assert.Contains(drift.Changes, c => c.Field == "encoding" && c.Expected == "H.264" && c.Actual == "H.265");
        Assert.Contains(drift.Changes, c => c.Field == "resolution");
        Assert.Contains(drift.Changes, c => c.Field == "gov_length");
        Assert.Contains("DRIFTED", drift.Message);
    }

    [Fact]
    public void CompliantWhenMatchesValidated()
    {
        var expected = CameraProfileAuditService.CreateProvisionalBaseline(102);
        var profile = new CameraStreamProfile
        {
            ProfileId = "p1",
            ChannelId = 102,
            Current = new StreamConfigSnapshot
            {
                Encoding = "H.264",
                Width = 1280,
                Height = 720,
                Fps = 25,
                BitrateType = "CBR",
                BitrateKbps = 2048,
                GovLength = 25,
                SmartCodec = false,
                Svc = false,
                Audio = false,
            },
        };
        var drift = CameraProfileAuditService.CompareToValidated(profile, expected);
        Assert.Equal("compliant", drift.Status);
    }

    [Fact]
    public void ObservedMismatch_DoesNotAffectConfiguredDrift()
    {
        var expected = CameraProfileAuditService.CreateProvisionalBaseline(102);
        var profile = new CameraStreamProfile
        {
            ProfileId = "p1",
            ChannelId = 102,
            Current = new StreamConfigSnapshot
            {
                Encoding = "H.264", Width = 1280, Height = 720, Fps = 25,
                BitrateType = "CBR", BitrateKbps = 2048, GovLength = 25,
                SmartCodec = false, Svc = false, Audio = false,
            },
            Observed = new StreamObservedSnapshot
            {
                Ok = true, CodecName = "hevc", Width = 1920, Height = 1080, Fps = 30,
            },
        };
        // Drift compares configured vs validated, not observed.
        Assert.Equal("compliant", CameraProfileAuditService.CompareToValidated(profile, expected).Status);
        Assert.NotEqual(
            CameraProfileAuditService.NormalizeCodec(profile.Current.Encoding),
            CameraProfileAuditService.NormalizeCodec(profile.Observed.CodecName));
    }

    [Fact]
    public void ValidateAgainstCapabilities_RejectsUnsupportedEncoding()
    {
        var caps = new StreamCapabilities { Encodings = { "H.265", "MJPEG" } };
        var target = CameraProfileAuditService.CreateProvisionalBaseline(102);
        var errors = CameraValidatedProfileApplyService.ValidateAgainstCapabilities(caps, target);
        Assert.Contains(errors, e => e.Contains("encoding"));
    }

    [Fact]
    public void PatchStreamingChannelXml_UpdatesKeyFields()
    {
        var xml = """
            <StreamingChannel>
              <Video>
                <videoCodecType>H.265</videoCodecType>
                <videoResolutionWidth>1920</videoResolutionWidth>
                <videoResolutionHeight>1080</videoResolutionHeight>
                <maxFrameRate>2500</maxFrameRate>
                <videoQualityControlType>VBR</videoQualityControlType>
                <constantBitRate>4096</constantBitRate>
                <GovLength>50</GovLength>
                <H264Profile>Main</H264Profile>
                <SmartCodec>true</SmartCodec>
              </Video>
              <Audio><enabled>true</enabled></Audio>
              <SVC><enabled>true</enabled></SVC>
            </StreamingChannel>
            """;
        var target = CameraProfileAuditService.CreateProvisionalBaseline(102);
        var patched = CameraValidatedProfileApplyService.PatchStreamingChannelXml(xml, target);
        Assert.Contains("<videoCodecType>H.264</videoCodecType>", patched);
        Assert.Contains("<videoResolutionWidth>1280</videoResolutionWidth>", patched);
        Assert.Contains("<videoResolutionHeight>720</videoResolutionHeight>", patched);
        Assert.Contains("<constantBitRate>2048</constantBitRate>", patched);
        Assert.Contains("<GovLength>25</GovLength>", patched);
        Assert.Contains("<SmartCodec>false</SmartCodec>", patched);
        Assert.Contains("<enabled>false</enabled>", patched); // audio/svc
        Assert.DoesNotContain("password", patched);
    }

    [Fact]
    public void RoiMismatch_WhenFingerprintDiffers()
    {
        var roi = new RoiProfile
        {
            Id = "office-fall-test",
            StreamProfileFingerprint = "abc123",
            SourceWidth = 1280,
            SourceHeight = 720,
            Points = { new(0, 0), new(10, 0), new(10, 10) },
        };
        Assert.Equal("RESOLUTION/STREAM MISMATCH",
            RoiProfileService.MismatchStatus(roi, "different", 1280, 720));
        Assert.Null(RoiProfileService.MismatchStatus(roi, "abc123", 1280, 720));
        Assert.Equal("RESOLUTION/STREAM MISMATCH",
            RoiProfileService.MismatchStatus(roi, "abc123", 1920, 1080));
    }

    [Fact]
    public void GlassToGlassFingerprint_IncludesChannelAndProfile()
    {
        var a = ConfigFingerprint.BuildStreamLatencyPayload(
            "camera-122727", "office-test-camera", "office-test-camera",
            1280, 720, 25, "h264", 102, "sub", "camera-122727-ch102");
        var b = ConfigFingerprint.BuildStreamLatencyPayload(
            "camera-122727", "office-test-camera", "office-test-camera",
            1280, 720, 25, "h264", 101, "main", "camera-122727-ch101");
        Assert.NotEqual(ConfigFingerprint.Compute(a), ConfigFingerprint.Compute(b));
    }

    [Fact]
    public void RegistryLoad_MigratesAndRejectsSecrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cg-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cameras.json");
        File.WriteAllText(path, """
        {
          "version": 1,
          "cameras": [{
            "camera_id": "camera-122727",
            "display_name": "Office 63",
            "host": "10.6.1.63",
            "credential_ref": "CableGuard.Camera.office",
            "profile": "Streaming/Channels/102",
            "mediamtx_path": "office-test-camera"
          }],
          "stream_mappings": []
        }
        """);
        var loaded = CameraRegistryService.Load(path);
        Assert.Equal(2, loaded.Version);
        Assert.Equal("camera-122727-ch102", loaded.Cameras[0].SelectedProfileId);
        Assert.Equal("sub", loaded.Cameras[0].StreamProfiles[0].StreamType);
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void OfficeMapping_SubAndMainLogicalStreamsAreDistinct()
    {
        var cam = new CameraEntry
        {
            CameraId = "camera-122727",
            Host = "10.6.1.63",
            Profile = "Streaming/Channels/102",
            MediaMtxPath = "office-test-camera",
        };
        CameraProfileAuditService.EnsureMigrated(cam);
        cam.StreamProfiles.Add(new CameraStreamProfile
        {
            ProfileId = "camera-122727-ch101",
            CameraId = cam.CameraId,
            ChannelId = 101,
            StreamType = "main",
            RtspPath = "Streaming/Channels/101",
        });

        var streams = new StreamsDocument
        {
            Streams =
            {
                new LogicalStream
                {
                    StreamId = "office-test-camera",
                    MediaMtxPath = "office-test-camera",
                    CameraId = cam.CameraId,
                    ProfileId = "camera-122727-ch102",
                    ChannelId = 102,
                    StreamType = "sub",
                },
                new LogicalStream
                {
                    StreamId = "office-test-camera-main",
                    MediaMtxPath = "office-test-camera-main",
                    CameraId = cam.CameraId,
                    ProfileId = "camera-122727-ch101",
                    ChannelId = 101,
                    StreamType = "main",
                },
            },
        };

        Assert.Equal(102, streams.Streams.First(s => s.StreamId == "office-test-camera").ChannelId);
        Assert.Equal(101, streams.Streams.First(s => s.StreamId == "office-test-camera-main").ChannelId);
        Assert.Equal("office-test-camera", cam.MediaMtxPath); // primary unchanged
    }

    [Fact]
    public void ProductionLike_RequiresConfirmation()
    {
        var prod = new CameraEntry { CameraId = "zahradky-upper-92", SiteId = "zahradky", MediaMtxPath = "zahradky-horni-stanice" };
        Assert.True(prod.IsProductionLike());
        var office = new CameraEntry { CameraId = "camera-122727", SiteId = "office", MediaMtxPath = "office-test-camera" };
        Assert.False(office.IsProductionLike());
    }
}
