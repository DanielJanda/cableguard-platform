using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

internal sealed class FakeMediaMtxApi : IMediaMtxApi
{
    public Dictionary<string, string> Sources { get; } = new();
    public HashSet<string> ReadyPaths { get; } = new();
    /// <summary>Path+source combinations that never become ready (simulates a source dying on a specific path).</summary>
    public HashSet<(string Path, string Source)> DeadCombos { get; } = new();
    public List<(string Path, string Source)> PatchCalls { get; } = new();
    public List<(string Path, string Source)> AddCalls { get; } = new();
    public List<string> DeleteCalls { get; } = new();
    public bool FailNextAdd { get; set; }

    public Task<bool?> IsControlApiReadyAsync(CancellationToken ct = default) =>
        Task.FromResult<bool?>(true);

    public Task<bool?> IsPathReadyAsync(string pathName, CancellationToken ct = default)
    {
        if (!Sources.TryGetValue(pathName, out var source)) return Task.FromResult<bool?>(false);
        if (DeadCombos.Contains((pathName, source))) return Task.FromResult<bool?>(false);
        return Task.FromResult<bool?>(ReadyPaths.Contains(pathName));
    }

    public Task<string?> GetConfiguredSourceAsync(string pathName, CancellationToken ct = default) =>
        Task.FromResult(Sources.TryGetValue(pathName, out var s) ? s : null);

    public Task<bool?> ConfigPathExistsAsync(string pathName, CancellationToken ct = default) =>
        Task.FromResult<bool?>(Sources.ContainsKey(pathName));

    public Task<bool> PatchPathSourceAsync(string pathName, string source, CancellationToken ct = default)
    {
        PatchCalls.Add((pathName, source));
        Sources[pathName] = source;
        ReadyPaths.Add(pathName);
        return Task.FromResult(true);
    }

    public Task<bool> AddPathAsync(string pathName, string source, string? rtspTransport = "tcp", CancellationToken ct = default)
    {
        AddCalls.Add((pathName, source));
        if (FailNextAdd) { FailNextAdd = false; return Task.FromResult(false); }
        if (Sources.ContainsKey(pathName)) return Task.FromResult(false);
        Sources[pathName] = source;
        ReadyPaths.Add(pathName);
        return Task.FromResult(true);
    }

    public Task<bool> DeletePathAsync(string pathName, CancellationToken ct = default)
    {
        DeleteCalls.Add(pathName);
        Sources.Remove(pathName);
        ReadyPaths.Remove(pathName);
        return Task.FromResult(true);
    }
}

internal sealed class FakePersister : IMediaMtxConfigPersister
{
    public List<(string Path, string Source)> Persisted { get; } = new();
    public List<string> Upserted { get; } = new();
    public List<string> Removed { get; } = new();
    public bool FailPersist { get; set; }

    public bool PersistPathSource(string pathName, string newSource, out string message)
    {
        if (FailPersist) { message = "persist failed"; return false; }
        Persisted.Add((pathName, newSource));
        message = "persisted";
        return true;
    }

    public bool UpsertPath(string pathName, string source, string? rtspTransport, out string message)
    {
        if (FailPersist) { message = "upsert failed"; return false; }
        Upserted.Add(pathName);
        Persisted.Add((pathName, source));
        message = "upserted";
        return true;
    }

    public bool RemovePath(string pathName, out string message)
    {
        Removed.Add(pathName);
        message = "removed";
        return true;
    }
}

internal sealed class FakeProber : IHttpProber
{
    public int? OptionsResult { get; set; } = 204;
    public Task<int?> GetStatusCodeAsync(string url, CancellationToken ct = default) => Task.FromResult<int?>(200);
    public Task<int?> OptionsStatusCodeAsync(string url, CancellationToken ct = default) => Task.FromResult(OptionsResult);
    public Task<string?> GetBodyAsync(string url, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

public class StreamSwitchServiceTests
{
    private const string Production = "zahradky-horni-stanice";
    private const string OldSource = "rtsp://***92***";
    private const string NewSource = "rtsp://***90***";

    private static CameraRegistryDocument Registry() => new()
    {
        Cameras =
        {
            new CameraEntry { CameraId = "zahradky-upper-92", DisplayName = "Kamera 92", SiteId = "zahradky", StationId = "horni-stanice", Host = "10.2.4.92", CredentialRef = "ref92", MediaMtxPath = "zahradky-horni-stanice-92", Enabled = true },
            new CameraEntry { CameraId = "zahradky-upper-90", DisplayName = "Kamera 90", SiteId = "zahradky", StationId = "horni-stanice", Host = "10.2.4.90", CredentialRef = "ref90", MediaMtxPath = "zahradky-horni-stanice-90", Enabled = true },
        },
        StreamMappings = { new StreamMapping { LogicalStream = Production, PrimaryCameraId = "zahradky-upper-92" } },
    };

    private static FakeMediaMtxApi HealthyApi()
    {
        var api = new FakeMediaMtxApi();
        api.Sources[Production] = OldSource;
        api.Sources["zahradky-horni-stanice-92"] = OldSource;
        api.Sources["zahradky-horni-stanice-90"] = NewSource;
        api.ReadyPaths.Add(Production);
        api.ReadyPaths.Add("zahradky-horni-stanice-92");
        api.ReadyPaths.Add("zahradky-horni-stanice-90");
        return api;
    }

    private static StreamSwitchService Service(FakeMediaMtxApi api, FakePersister persister, FakeProber? prober = null) =>
        new(api, persister, prober ?? new FakeProber(), "http://127.0.0.1:8889",
            readyTimeout: TimeSpan.FromMilliseconds(300), pollInterval: TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task SuccessfulSwitch_PatchesVerifiesPersists_AndUpdatesMapping()
    {
        var api = HealthyApi();
        var persister = new FakePersister();
        var registry = Registry();
        CameraRegistryDocument? saved = null;

        var result = await Service(api, persister).SwitchPrimaryAsync(
            registry, Production, "zahradky-upper-90", r => saved = r);

        Assert.True(result.Success);
        Assert.False(result.RolledBack);
        Assert.Single(api.PatchCalls);
        Assert.Equal((Production, NewSource), api.PatchCalls[0]);
        Assert.Single(persister.Persisted);
        Assert.Equal(NewSource, persister.Persisted[0].Source);
        Assert.NotNull(saved);
        Assert.Equal("zahradky-upper-90",
            saved!.StreamMappings.Single(m => m.LogicalStream == Production).PrimaryCameraId);
    }

    [Fact]
    public async Task FailedSwitch_NewStreamNeverReady_RollsBackToOldSource()
    {
        var api = HealthyApi();
        api.DeadCombos.Add((Production, NewSource)); // new source dies on the production path right after the patch
        var persister = new FakePersister();
        var registry = Registry();

        var result = await Service(api, persister).SwitchPrimaryAsync(registry, Production, "zahradky-upper-90");

        Assert.False(result.Success);
        Assert.True(result.RolledBack);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(2, api.PatchCalls.Count);
        Assert.Equal(NewSource, api.PatchCalls[0].Source);
        Assert.Equal(OldSource, api.PatchCalls[1].Source);   // rollback restored the previous source
        Assert.Empty(persister.Persisted);                    // nothing persisted on failure
        Assert.Equal("zahradky-upper-92",
            registry.StreamMappings.Single(m => m.LogicalStream == Production).PrimaryCameraId); // mapping unchanged
    }

    [Fact]
    public async Task Switch_ToDisabledCamera_IsRefusedWithoutChanges()
    {
        var api = HealthyApi();
        var registry = Registry();
        registry.Cameras.Single(c => c.CameraId == "zahradky-upper-90").Enabled = false;

        var result = await Service(api, new FakePersister()).SwitchPrimaryAsync(registry, Production, "zahradky-upper-90");

        Assert.False(result.Success);
        Assert.Empty(api.PatchCalls);
    }

    [Fact]
    public async Task Switch_ToNotReadyCamera_IsRefusedBeforeAnyChange()
    {
        var api = HealthyApi();
        api.ReadyPaths.Remove("zahradky-horni-stanice-90"); // candidate camera path is down
        var registry = Registry();

        var result = await Service(api, new FakePersister()).SwitchPrimaryAsync(registry, Production, "zahradky-upper-90");

        Assert.False(result.Success);
        Assert.Contains("not READY", result.Message);
        Assert.Empty(api.PatchCalls);
    }

    [Fact]
    public async Task SwitchMessages_NeverContainRtspSources()
    {
        var api = HealthyApi();
        api.Sources["zahradky-horni-stanice-90"] = "rtsp://admin:secret@10.2.4.90:554/x";
        api.DeadCombos.Add((Production, "rtsp://admin:secret@10.2.4.90:554/x"));
        var progress = new List<string>();

        var result = await Service(api, new FakePersister()).SwitchPrimaryAsync(
            Registry(), Production, "zahradky-upper-90",
            progress: new Progress<string>(progress.Add));
        await Task.Delay(50);

        var everything = result.Message + string.Join("\n", progress);
        Assert.DoesNotContain("secret", everything);
        Assert.DoesNotContain("rtsp://", everything);
    }
}
