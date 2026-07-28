using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class TestStationProfileTests
{
    [Fact]
    public void OfficeDefault_IsValidTestStation()
    {
        var doc = TestStationService.OfficeDefault();
        Assert.Empty(TestStationService.ValidateDocument(doc));
        var s = Assert.Single(doc.Stations);
        Assert.Equal("office-test", s.StationId);
        Assert.Equal("office", s.SiteId);
        Assert.Equal("office-test-camera", s.VideoStream);
        Assert.Equal("fall-office-test", s.FallServiceId);
        Assert.Equal("camera-122727", s.CameraId);
        Assert.Equal("test", s.Mode);
        Assert.Equal("/test-lab/office-fall", s.MonitorPath);
    }

    [Fact]
    public void Validate_RejectsMissingFields()
    {
        var errors = TestStationService.Validate(new());
        Assert.Contains(errors, e => e.Contains("station_id"));
        Assert.Contains(errors, e => e.Contains("video_stream"));
    }
}
