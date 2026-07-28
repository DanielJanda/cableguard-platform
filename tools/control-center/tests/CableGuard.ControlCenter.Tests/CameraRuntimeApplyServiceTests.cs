using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class CameraRuntimeApplyServiceTests
{
    private static CameraEntry OfficeCam() => new()
    {
        CameraId = "camera-office",
        DisplayName = "Office",
        SiteId = "office",
        StationId = "test",
        Host = "10.6.1.63",
        RtspPort = 554,
        Profile = "Streaming/Channels/102",
        Transport = "tcp",
        CredentialRef = "CableGuard.Camera.office",
        MediaMtxPath = "office-test-camera",
        Enabled = true,
    };

    private static (FakeMediaMtxApi api, FakePersister persister, FakeCreds creds, FakeProber prober, CameraRuntimeApplyService svc, string tmpDir)
        Harness(bool withCreds = true)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "cg-cam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "runtime", "config"));
        Directory.CreateDirectory(Path.Combine(tmp, "deploy", "mediamtx"));
        File.WriteAllText(Path.Combine(tmp, "deploy", "mediamtx", "mediamtx.local.yml"), "paths:\n");
        File.WriteAllText(Path.Combine(tmp, "runtime", "config", "cameras.json"),
            """{"version":1,"cameras":[],"stream_mappings":[]}""");
        File.WriteAllText(Path.Combine(tmp, "runtime", "config", "streams.json"),
            """{"version":1,"streams":[]}""");

        var config = new ControlCenterConfig { PlatformRoot = tmp, WhepBaseLocal = "http://127.0.0.1:8889" };
        var api = new FakeMediaMtxApi();
        var persister = new FakePersister();
        var creds = new FakeCreds();
        if (withCreds) creds.Store["CableGuard.Camera.office"] = ("admin", "x");
        var prober = new FakeProber { OptionsResult = 204 };
        var logger = new ControlCenterLogger(Path.Combine(tmp, "logs"));
        var svc = new CameraRuntimeApplyService(config, api, persister, creds, prober, logger);
        return (api, persister, creds, prober, svc, tmp);
    }

    [Fact]
    public void SuggestPath_PrefersOfficeTestCamera()
    {
        var cam = OfficeCam();
        cam.MediaMtxPath = "";
        Assert.Equal("office-test-camera", CameraRuntimeApplyService.SuggestPath(cam));
    }

    [Fact]
    public void BuildRtspSource_DoesNotEmbedPlainHostOnly()
    {
        var url = CameraRuntimeApplyService.BuildRtspSource(OfficeCam(), "u", "p");
        Assert.Contains("10.6.1.63", url);
        Assert.Contains("Streaming/Channels/102", url);
        Assert.DoesNotContain("password", CameraRuntimeApplyService.RedactRtsp(url), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedCredential_RemainsNotReady()
    {
        var (_, _, _, _, svc, _) = Harness(withCreds: false);
        var result = await svc.ApplyAsync(OfficeCam());
        Assert.False(result.Success);
        Assert.Equal(CameraRuntimeState.ConfiguredNotReady, result.State);
        Assert.Contains("Credential", result.Message);
    }

    [Fact]
    public void FindDependencies_BlocksProductionAndDetectors()
    {
        var cam = OfficeCam();
        var registry = new CameraRegistryDocument
        {
            Cameras = { cam },
            StreamMappings = { new StreamMapping { LogicalStream = "zahradky-horni-stanice", PrimaryCameraId = cam.CameraId } },
        };
        var streams = new StreamsDocument
        {
            Streams =
            {
                new LogicalStream
                {
                    StreamId = "zahradky-horni-stanice", CameraId = cam.CameraId,
                    MediaMtxPath = "zahradky-horni-stanice", IsProduction = true, Enabled = true,
                },
            },
        };
        var detectors = new[] { new DetectorInstance { Id = "fall-1", InputStream = "office-test-camera" } };
        var hits = CameraRuntimeApplyService.FindDependencies(
            cam, registry, streams, detectors, Array.Empty<RoiProfile>(), Array.Empty<ScenarioDocument>());
        Assert.Contains(hits, h => h.Kind == "production_stream");
        Assert.Contains(hits, h => h.Kind == "detector");
        Assert.Contains(hits, h => h.Kind == "stream_mapping");
    }

    [Fact]
    public async Task RemovePath_CallsApiAndPersister()
    {
        var (api, persister, _, _, svc, _) = Harness();
        api.Sources["office-test-camera"] = "rtsp://x";
        var (ok, msg) = await svc.RemovePathAsync("office-test-camera");
        Assert.True(ok, msg);
        Assert.Contains("office-test-camera", api.DeleteCalls);
        Assert.Contains("office-test-camera", persister.Removed);
    }
}

public class MediaMtxUpsertPersisterTests : IDisposable
{
    private readonly string _ymlPath = Path.Combine(Path.GetTempPath(), $"mediamtx-up-{Guid.NewGuid():N}.yml");

    public MediaMtxUpsertPersisterTests()
    {
        File.WriteAllText(_ymlPath, """
paths:
  zahradky-horni-stanice:
    source: rtsp://u:p@10.2.4.92:554/x
    rtspTransport: tcp
    sourceOnDemand: no
""");
    }

    public void Dispose()
    {
        File.Delete(_ymlPath);
        File.Delete(_ymlPath + ".bak");
    }

    [Fact]
    public void Upsert_CreatesNewPathBlock()
    {
        var p = new MediaMtxLocalConfigPersister(_ymlPath);
        Assert.True(p.UpsertPath("office-test-camera", "rtsp://a:b@10.6.1.63:554/Streaming/Channels/102", "tcp", out var msg), msg);
        var text = File.ReadAllText(_ymlPath);
        Assert.Contains("office-test-camera:", text);
        Assert.Contains("10.6.1.63", text);
        Assert.Contains("zahradky-horni-stanice:", text);
    }

    [Fact]
    public void RemovePath_DeletesBlock()
    {
        var p = new MediaMtxLocalConfigPersister(_ymlPath);
        Assert.True(p.UpsertPath("office-test-camera", "rtsp://a:b@h/x", "tcp", out _));
        Assert.True(p.RemovePath("office-test-camera", out var msg), msg);
        Assert.DoesNotContain("office-test-camera:", File.ReadAllText(_ymlPath));
    }

    [Fact]
    public void Upsert_ExistingPath_UpdatesSourceOnly()
    {
        var p = new MediaMtxLocalConfigPersister(_ymlPath);
        Assert.True(p.UpsertPath("zahradky-horni-stanice", "rtsp://n:w@10.2.4.90:554/y", "tcp", out _));
        var text = File.ReadAllText(_ymlPath);
        Assert.Contains("10.2.4.90", text);
        Assert.DoesNotContain("10.2.4.92", text);
    }
}

public class HardwareGuardTests
{
    [Fact]
    public void NotAvailable_EnsureTestModeFails()
    {
        var hw = new NotAvailableHardwareAdapter { IsTestMode = true };
        Assert.Throws<InvalidOperationException>(() => HardwareSafety.EnsureTestMode(hw));
    }

    [Fact]
    public void PulseClamp_Max500ms()
    {
        var clamped = HardwareSafety.ClampPulse(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.FromMilliseconds(500), clamped);
    }

    [Fact]
    public void WriteRequiresTestMode_WhenAvailable()
    {
        var hw = new FakeAvailableHardware { IsTestMode = false };
        Assert.Throws<InvalidOperationException>(() => HardwareSafety.EnsureTestMode(hw));
        hw.IsTestMode = true;
        HardwareSafety.EnsureTestMode(hw); // does not throw
    }
}

internal sealed class FakeAvailableHardware : IHardwareAdapter
{
    public string StatusDetail => "CONNECTED test fake";
    public bool IsAvailable => true;
    public bool IsTestMode { get; set; }
    public Task<bool> EnsureConnectedAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<string, bool>> ReadDigitalInputsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    public Task PulseRelayAsync(int channel, TimeSpan duration, CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        return Task.CompletedTask;
    }
    public Task SetSemaphoreAsync(string color, bool on, CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        return Task.CompletedTask;
    }
    public Task AllOffAsync(CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCreds : ICredentialStore
{
    public Dictionary<string, (string User, string Pass)> Store { get; } = new();

    public bool TryRead(string credentialRef, out string username, out string password)
    {
        if (Store.TryGetValue(credentialRef, out var v))
        {
            username = v.User; password = v.Pass; return true;
        }
        username = ""; password = ""; return false;
    }

    public bool Write(string credentialRef, string username, string password)
    {
        Store[credentialRef] = (username, password); return true;
    }

    public bool Delete(string credentialRef) => Store.Remove(credentialRef);
}
