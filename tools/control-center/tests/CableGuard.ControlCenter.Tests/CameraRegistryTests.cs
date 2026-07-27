using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class CameraRegistryTests
{
    private const string ValidJson = """
    {
      "version": 1,
      "cameras": [
        {
          "camera_id": "zahradky-upper-92",
          "display_name": "Horni kamera 92",
          "site_id": "zahradky",
          "station_id": "horni-stanice",
          "host": "10.2.4.92",
          "rtsp_port": 554,
          "profile": "Streaming/Channels/102",
          "enabled": true,
          "credential_ref": "CableGuard.Camera.zahradky-upper-92",
          "mediamtx_path": "zahradky-horni-stanice-92"
        },
        {
          "camera_id": "zahradky-upper-90",
          "display_name": "Horni kamera 90",
          "site_id": "zahradky",
          "station_id": "horni-stanice",
          "host": "10.2.4.90",
          "rtsp_port": 554,
          "profile": "Streaming/Channels/102",
          "enabled": true,
          "credential_ref": "CableGuard.Camera.zahradky-upper-90",
          "mediamtx_path": "zahradky-horni-stanice-90"
        }
      ],
      "stream_mappings": [
        { "logical_stream": "zahradky-horni-stanice", "primary_camera_id": "zahradky-upper-92" }
      ]
    }
    """;

    [Fact]
    public void ValidRegistry_PassesValidation()
    {
        Assert.Empty(CameraRegistryService.Validate(ValidJson));
    }

    [Fact]
    public void PasswordKey_IsRejected()
    {
        // Credentials must never be stored inline in cameras.json.
        var json = ValidJson.Replace(
            "\"credential_ref\": \"CableGuard.Camera.zahradky-upper-92\",",
            "\"credential_ref\": \"CableGuard.Camera.zahradky-upper-92\", \"password\": \"tajne\",");
        var errors = CameraRegistryService.Validate(json);
        Assert.Contains(errors, e => e.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RtspUrlKey_IsRejected()
    {
        var json = ValidJson.Replace(
            "\"host\": \"10.2.4.92\",",
            "\"host\": \"10.2.4.92\", \"rtsp_url\": \"rtsp://u:p@10.2.4.92/x\",");
        var errors = CameraRegistryService.Validate(json);
        Assert.Contains(errors, e => e.Contains("rtsp_url"));
    }

    [Fact]
    public void MissingRequiredFields_AreReported()
    {
        var json = """{ "version": 1, "cameras": [ { "camera_id": "cam-1" } ], "stream_mappings": [] }""";
        var errors = CameraRegistryService.Validate(json);
        Assert.Contains(errors, e => e.Contains("display_name"));
        Assert.Contains(errors, e => e.Contains("host"));
        Assert.Contains(errors, e => e.Contains("credential_ref"));
    }

    [Fact]
    public void DuplicateCameraIds_AreReported()
    {
        var json = ValidJson.Replace("zahradky-upper-90", "zahradky-upper-92");
        var errors = CameraRegistryService.Validate(json);
        Assert.Contains(errors, e => e.Contains("Duplicate camera_id"));
    }

    [Fact]
    public void MappingToUnknownCamera_IsReported()
    {
        var json = ValidJson.Replace(
            "\"primary_camera_id\": \"zahradky-upper-92\"",
            "\"primary_camera_id\": \"neexistuje\"");
        var errors = CameraRegistryService.Validate(json);
        Assert.Contains(errors, e => e.Contains("unknown camera"));
    }

    [Fact]
    public void PrimaryMapping_ResolvesToPhysicalCamera()
    {
        var registry = System.Text.Json.JsonSerializer.Deserialize<CameraRegistryDocument>(ValidJson)!;
        var primary = CameraRegistryService.ResolvePrimaryCamera(registry, "zahradky-horni-stanice");
        Assert.NotNull(primary);
        Assert.Equal("zahradky-upper-92", primary!.CameraId);
        Assert.Equal("10.2.4.92", primary.Host);
    }

    [Fact]
    public void UnknownLogicalStream_ResolvesToNull()
    {
        var registry = System.Text.Json.JsonSerializer.Deserialize<CameraRegistryDocument>(ValidJson)!;
        Assert.Null(CameraRegistryService.ResolvePrimaryCamera(registry, "neexistujici-stream"));
    }
}
