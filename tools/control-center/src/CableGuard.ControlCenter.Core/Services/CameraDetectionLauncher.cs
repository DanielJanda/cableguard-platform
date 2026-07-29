using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Maps a camera registry entry to a fall DetectorInstance for START DETECTION.
/// Prefers an existing detectors.json instance whose input_stream matches the MediaMTX path.
/// </summary>
public static class CameraDetectionLauncher
{
    public sealed record LaunchSummary(
        string CameraId,
        string DisplayName,
        string InputProfile,
        string Backend,
        string SourceMode,
        string MediaMtxPath,
        string Model,
        string Device,
        string DetectorInstanceId,
        bool IsTestEnvironment);

    public static DetectorInstance ResolveOrCreateInstance(
        CameraEntry camera,
        DetectorsDocument detectors)
    {
        var path = string.IsNullOrWhiteSpace(camera.MediaMtxPath) ? camera.CameraId : camera.MediaMtxPath;
        var existing = detectors.Instances.FirstOrDefault(i =>
            i.DetectorType == "fall" &&
            string.Equals(i.InputStream, path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            ApplyCameraDefaults(existing, camera, path);
            return existing;
        }

        // Prefer canonical office instance id when path is office-test-camera.
        var id = string.Equals(path, "office-test-camera", StringComparison.OrdinalIgnoreCase)
            ? "fall-office-test"
            : $"fall-{camera.CameraId}";

        var byId = detectors.Instances.FirstOrDefault(i =>
            string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            ApplyCameraDefaults(byId, camera, path);
            return byId;
        }

        var created = new DetectorInstance
        {
            Id = id,
            DisplayName = $"Fall – {camera.DisplayName}",
            DetectorType = "fall",
            InputStream = path,
            Model = "yolo11m-pose.pt",
            RoiProfile = string.Equals(camera.Environment, "test", StringComparison.OrdinalIgnoreCase)
                ? "office-fall-test"
                : "",
            Device = "cuda:0",
            Enabled = true,
            DebugOverlay = false,
            InputProfile = string.IsNullOrWhiteSpace(camera.DetectorInputProfile)
                ? "pyav_rtsp"
                : camera.DetectorInputProfile,
            SourceMode = string.IsNullOrWhiteSpace(camera.SourceMode) ? "mediamtx" : camera.SourceMode,
            PublishJsonl = true,
            PublishEventCore = camera.EventGenerationAllowed,
            PublishTelegram = false,
            ScriptRelative = "apps/zahradky_horni_pad.py",
            ProcessHint = "zahradky_horni_pad",
        };
        detectors.Instances.Add(created);
        return created;
    }

    public static LaunchSummary BuildSafeSummary(CameraEntry camera, DetectorInstance instance)
    {
        var profile = string.IsNullOrWhiteSpace(instance.InputProfile) ? "pyav_rtsp" : instance.InputProfile;
        var source = string.IsNullOrWhiteSpace(instance.SourceMode) ? "mediamtx" : instance.SourceMode;
        return new LaunchSummary(
            camera.CameraId,
            camera.DisplayName,
            profile,
            string.IsNullOrWhiteSpace(camera.PreferredBackend) ? profile : camera.PreferredBackend,
            source,
            instance.InputStream,
            string.IsNullOrWhiteSpace(instance.Model) ? "yolo11m-pose.pt" : instance.Model,
            string.IsNullOrWhiteSpace(instance.Device) ? "cuda:0" : instance.Device,
            instance.Id,
            string.Equals(camera.Environment, "test", StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatOperatorSummary(LaunchSummary s) =>
        $"camera={s.DisplayName} ({s.CameraId})\n" +
        $"environment={(s.IsTestEnvironment ? "TEST" : "PRODUCTION")}\n" +
        $"input_profile={s.InputProfile}\n" +
        $"backend={s.Backend}\n" +
        $"source_mode={s.SourceMode}\n" +
        $"mediamtx_path={s.MediaMtxPath}\n" +
        $"model={s.Model}\n" +
        $"device={s.Device}\n" +
        $"detector_id={s.DetectorInstanceId}\n" +
        "(credentials / RTSP URL not shown)";

    private static void ApplyCameraDefaults(DetectorInstance instance, CameraEntry camera, string path)
    {
        instance.InputStream = path;
        if (!string.IsNullOrWhiteSpace(camera.DetectorInputProfile))
            instance.InputProfile = camera.DetectorInputProfile;
        else if (string.IsNullOrWhiteSpace(instance.InputProfile))
            instance.InputProfile = "pyav_rtsp";

        if (!string.IsNullOrWhiteSpace(camera.SourceMode))
            instance.SourceMode = camera.SourceMode;
        else if (string.IsNullOrWhiteSpace(instance.SourceMode))
            instance.SourceMode = "mediamtx";

        instance.Enabled = true;
        if (string.Equals(camera.Environment, "test", StringComparison.OrdinalIgnoreCase))
        {
            instance.PublishTelegram = false;
            if (camera.EventGenerationAllowed)
                instance.PublishEventCore = true;
        }
    }
}
