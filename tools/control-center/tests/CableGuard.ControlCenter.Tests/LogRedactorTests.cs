using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class LogRedactorTests
{
    [Fact]
    public void RtspCredentials_AreMasked()
    {
        var line = "connecting to rtsp://admin:SuperTajne123@10.2.4.92:554/Streaming/Channels/102";
        var redacted = LogRedactor.Redact(line);
        Assert.DoesNotContain("SuperTajne123", redacted);
        Assert.DoesNotContain("admin:", redacted);
        Assert.Contains("rtsp://***:***@10.2.4.92:554", redacted);
    }

    [Fact]
    public void ApiKeyHeaders_AreMasked()
    {
        Assert.DoesNotContain("abc123", LogRedactor.Redact("sending X-API-Key: abc123"));
        Assert.DoesNotContain("kioskkey9", LogRedactor.Redact("header X-Kiosk-Key=kioskkey9"));
    }

    [Fact]
    public void PasswordAssignments_AreMasked()
    {
        Assert.DoesNotContain("hunter2", LogRedactor.Redact("password=hunter2"));
        Assert.DoesNotContain("t0ken", LogRedactor.Redact("api_key: t0ken"));
    }

    [Fact]
    public void NormalLines_PassThroughUnchanged()
    {
        var line = "2026-07-27 12:00:00 INF [path zahradky-horni-stanice] is ready";
        Assert.Equal(line, LogRedactor.Redact(line));
    }
}
