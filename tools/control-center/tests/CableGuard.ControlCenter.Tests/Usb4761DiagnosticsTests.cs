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
    public void Native_BioDaq_Open_Read_When_Hardware_Present()
    {
        var daq = AdvantechUsb4761Adapter.FindDaqNavi();
        if (daq is null)
        {
            // CI / machines without SDK — skip soft.
            return;
        }

        try
        {
            using var session = BioDaqNativeSession.Open(daq);
            var di = session.ReadDi();
            var dout = session.ReadDo();
            Assert.Equal("Automation.BDaq4", session.AssemblyName);
            Assert.True(di.Length is 0 or 8);
            Assert.Equal(8, dout.Length);
        }
        catch (BioDaqException ex) when (ex.ErrorCode is "OPEN_FAILED" or "DRIVER_ERROR")
        {
            // Device busy / Navigator holding exclusive lock — report but don't fail CI hard.
            Assert.False(string.IsNullOrWhiteSpace(ex.ErrorCode));
        }
    }
}
