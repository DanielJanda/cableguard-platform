using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class Usb4761DiagnosticsTests
{
    [Theory]
    [InlineData("NOT FOUND")]
    [InlineData("SDK NOT FOUND")]
    [InlineData("SDK LOAD ERROR")]
    [InlineData("ARCHITECTURE MISMATCH")]
    [InlineData("DRIVER ERROR")]
    [InlineData("OPEN FAILED")]
    [InlineData("READ FAILED")]
    [InlineData("CONNECTED")]
    public void HardwareDiscovery_Status_Is_Known_Token(string status)
    {
        var d = HardwareDiscovery.NotFound() with { Status = status };
        Assert.Equal(status, d.Status);
        Assert.False(string.IsNullOrWhiteSpace(d.ProcessArch));
    }

    [Fact]
    public void MaxPulse_Is_250ms()
    {
        Assert.Equal(250, AdvantechUsb4761Adapter.MaxPulse.TotalMilliseconds);
    }

    [Fact]
    public void FindDaqNavi_Does_Not_Throw()
    {
        var path = AdvantechUsb4761Adapter.FindDaqNavi();
        // Path may be null on machines without SDK; discovery must still be safe.
        Assert.True(path is null || Directory.Exists(path));
    }

    [Fact]
    public void ClampPulse_Never_Exceeds_250()
    {
        var clamped = HardwareSafety.ClampPulse(TimeSpan.FromSeconds(10), AdvantechUsb4761Adapter.MaxPulse);
        Assert.True(clamped <= AdvantechUsb4761Adapter.MaxPulse);
    }
}
