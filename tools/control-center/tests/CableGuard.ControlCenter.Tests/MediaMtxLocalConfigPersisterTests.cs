using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class MediaMtxLocalConfigPersisterTests : IDisposable
{
    private readonly string _ymlPath = Path.Combine(Path.GetTempPath(), $"mediamtx-test-{Guid.NewGuid():N}.yml");

    private const string Yml = """
webrtcAddress: :8889

paths:
  zahradky-horni-stanice:
    source: rtsp://old-user:old-pass@10.2.4.92:554/Streaming/Channels/102
    rtspTransport: tcp
    sourceOnDemand: no
  zahradky-horni-stanice-90:
    source: rtsp://u:p@10.2.4.90:554/Streaming/Channels/102
    rtspTransport: tcp
""";

    public MediaMtxLocalConfigPersisterTests() => File.WriteAllText(_ymlPath, Yml);

    public void Dispose()
    {
        File.Delete(_ymlPath);
        File.Delete(_ymlPath + ".bak");
    }

    [Fact]
    public void PersistsSource_OnlyForTargetPath_AndCreatesBackup()
    {
        var persister = new MediaMtxLocalConfigPersister(_ymlPath);
        var ok = persister.PersistPathSource("zahradky-horni-stanice", "rtsp://u:p@10.2.4.90:554/new", out var message);

        Assert.True(ok, message);
        var content = File.ReadAllText(_ymlPath);
        Assert.Contains("source: rtsp://u:p@10.2.4.90:554/new", content);
        Assert.DoesNotContain("old-user", content);
        Assert.Contains("source: rtsp://u:p@10.2.4.90:554/Streaming/Channels/102", content); // other path untouched
        Assert.True(File.Exists(_ymlPath + ".bak"));
    }

    [Fact]
    public void UnknownPath_FailsWithoutTouchingFile()
    {
        var before = File.ReadAllText(_ymlPath);
        var persister = new MediaMtxLocalConfigPersister(_ymlPath);
        var ok = persister.PersistPathSource("neexistuje", "rtsp://x", out _);
        Assert.False(ok);
        Assert.Equal(before, File.ReadAllText(_ymlPath));
    }

    [Fact]
    public void MissingFile_FailsGracefully()
    {
        var persister = new MediaMtxLocalConfigPersister(_ymlPath + ".missing");
        Assert.False(persister.PersistPathSource("x", "y", out var message));
        Assert.Contains("not found", message);
    }
}
