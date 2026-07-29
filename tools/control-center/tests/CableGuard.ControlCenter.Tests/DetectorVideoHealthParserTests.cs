using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class DetectorVideoHealthParserTests
{
    [Fact]
    public void Parses_VideoInput_And_Redacts_Rtsp()
    {
        const string json = """
        {
          "services": [
            {
              "service_id": "zahradky-horni-pad-detector",
              "status": "healthy",
              "camera_connected": true,
              "last_error": "rtsp://user:pass@10.2.4.92/fail",
              "details_json": {
                "input_stream": "zahradky-horni-stanice",
                "video_input": {
                  "backend": "pyav_rtsp",
                  "source_mode": "mediamtx",
                  "connection_state": "connected",
                  "decoded_fps": 19.5,
                  "latest_frame_age_ms": 16.0,
                  "reconnect_count": 0
                }
              }
            }
          ]
        }
        """;
        var h = DetectorVideoHealthParser.TryParse(json, null, "zahradky-horni-stanice");
        Assert.NotNull(h);
        Assert.True(h!.Available);
        Assert.Equal("pyav_rtsp", h.Backend);
        Assert.Equal("mediamtx", h.SourceMode);
        Assert.Equal(19.5, h.DecodedFps);
        Assert.Equal(0, h.ReconnectCount);
        Assert.DoesNotContain("pass", h.LastErrorRedacted ?? "");
        Assert.Contains("rtsp://***", h.LastErrorRedacted ?? "");
    }

    [Fact]
    public void Missing_Services_Returns_Null()
    {
        Assert.Null(DetectorVideoHealthParser.TryParse("{}", null, null));
    }
}
