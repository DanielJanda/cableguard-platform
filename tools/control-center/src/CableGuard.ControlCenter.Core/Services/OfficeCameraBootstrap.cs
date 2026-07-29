using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Ensures the office .63 test camera exists as camera_id=office-63 with operational fields.
/// Migrates legacy ids (camera-122727 / office-test-1) that already point at office-test-camera.
/// </summary>
public static class OfficeCameraBootstrap
{
    public const string OfficeCameraId = "office-63";
    public const string OfficeDisplayName = "Kancelářská kamera";
    public const string OfficeHost = "10.6.1.63";
    public const string OfficePath = "office-test-camera";

    public static bool EnsureOffice63(ControlCenterConfig config, ControlCenterLogger logger)
    {
        var changed = false;
        var registry = CameraRegistryService.Load(config.CamerasJsonPath);
        var office = registry.Cameras.FirstOrDefault(c =>
            string.Equals(c.CameraId, OfficeCameraId, StringComparison.OrdinalIgnoreCase));

        if (office is null)
        {
            office = registry.Cameras.FirstOrDefault(c =>
                string.Equals(c.Host, OfficeHost, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.MediaMtxPath, OfficePath, StringComparison.OrdinalIgnoreCase));
            if (office is not null)
            {
                var oldId = office.CameraId;
                office.CameraId = OfficeCameraId;
                logger.Info($"[BOOT] Migrated camera_id '{oldId}' → '{OfficeCameraId}'");
                changed = true;
                RetargetStreams(config, oldId, OfficeCameraId, logger);
            }
            else
            {
                office = new CameraEntry
                {
                    CameraId = OfficeCameraId,
                    Host = OfficeHost,
                    RtspPort = 554,
                    Profile = "Streaming/Channels/102",
                    Transport = "tcp",
                    CredentialRef = "CableGuard.Camera.office-63",
                    MediaMtxPath = OfficePath,
                    Enabled = true,
                    RuntimeState = "configured_not_ready",
                };
                registry.Cameras.Add(office);
                logger.Info($"[BOOT] Added {OfficeCameraId} to cameras.json");
                changed = true;
            }
        }

        // Normalize operator-facing fields every boot (safe, no credentials).
        if (office.DisplayName != OfficeDisplayName) { office.DisplayName = OfficeDisplayName; changed = true; }
        if (!string.Equals(office.SiteId, "office", StringComparison.OrdinalIgnoreCase))
        { office.SiteId = "office"; changed = true; }
        if (string.IsNullOrWhiteSpace(office.StationId)) { office.StationId = "desk"; changed = true; }
        if (!string.Equals(office.Environment, "test", StringComparison.OrdinalIgnoreCase))
        { office.Environment = "test"; changed = true; }
        if (!string.Equals(office.MediaMtxPath, OfficePath, StringComparison.OrdinalIgnoreCase))
        { office.MediaMtxPath = OfficePath; changed = true; }
        if (!string.Equals(office.DetectorInputProfile, "pyav_rtsp", StringComparison.OrdinalIgnoreCase))
        { office.DetectorInputProfile = "pyav_rtsp"; changed = true; }
        if (!string.Equals(office.PreferredBackend, "pyav_rtsp", StringComparison.OrdinalIgnoreCase))
        { office.PreferredBackend = "pyav_rtsp"; changed = true; }
        if (!string.Equals(office.SourceMode, "mediamtx", StringComparison.OrdinalIgnoreCase))
        { office.SourceMode = "mediamtx"; changed = true; }
        if (!office.RecordingAllowed) { office.RecordingAllowed = true; changed = true; }
        if (!office.EventGenerationAllowed) { office.EventGenerationAllowed = true; changed = true; }
        if (!office.Enabled) { office.Enabled = true; changed = true; }
        if (string.IsNullOrWhiteSpace(office.CredentialRef))
        { office.CredentialRef = "CableGuard.Camera.office-63"; changed = true; }

        // Mark Zahrádky as production when unset.
        foreach (var cam in registry.Cameras.Where(c =>
                     c.SiteId.Contains("zahradky", StringComparison.OrdinalIgnoreCase) &&
                     string.IsNullOrWhiteSpace(c.Environment)))
        {
            cam.Environment = "production";
            cam.PreferredBackend = string.IsNullOrWhiteSpace(cam.PreferredBackend) ? "pyav_rtsp" : cam.PreferredBackend;
            cam.DetectorInputProfile = string.IsNullOrWhiteSpace(cam.DetectorInputProfile) ? "pyav_rtsp" : cam.DetectorInputProfile;
            cam.SourceMode = string.IsNullOrWhiteSpace(cam.SourceMode) ? "mediamtx" : cam.SourceMode;
            changed = true;
        }

        if (changed)
            CameraRegistryService.Save(registry, config.CamerasJsonPath);

        EnsureOfficeStream(config, logger);
        return changed;
    }

    private static void RetargetStreams(
        ControlCenterConfig config, string oldCameraId, string newCameraId, ControlCenterLogger logger)
    {
        try
        {
            var streams = StreamsService.Load(config.StreamsJsonPath);
            var hit = false;
            foreach (var s in streams.Streams.Where(s =>
                         string.Equals(s.CameraId, oldCameraId, StringComparison.OrdinalIgnoreCase)))
            {
                s.CameraId = newCameraId;
                hit = true;
            }
            if (hit)
            {
                var known = CameraRegistryService.Load(config.CamerasJsonPath).Cameras.Select(c => c.CameraId);
                StreamsService.Save(streams, config.StreamsJsonPath, known);
                logger.Info($"[BOOT] Retargeted streams from '{oldCameraId}' → '{newCameraId}'");
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"[BOOT] stream retarget skipped: {ex.Message}");
        }
    }

    private static void EnsureOfficeStream(ControlCenterConfig config, ControlCenterLogger logger)
    {
        try
        {
            var streams = StreamsService.Load(config.StreamsJsonPath);
            var office = streams.Streams.FirstOrDefault(s =>
                string.Equals(s.MediaMtxPath, OfficePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.StreamId, OfficePath, StringComparison.OrdinalIgnoreCase));
            if (office is null)
            {
                streams.Streams.Add(new LogicalStream
                {
                    StreamId = OfficePath,
                    DisplayName = "Kancelářská kamera (test)",
                    MediaMtxPath = OfficePath,
                    CameraId = OfficeCameraId,
                    Enabled = true,
                    IsProduction = false,
                });
            }
            else
            {
                office.StreamId = OfficePath;
                office.MediaMtxPath = OfficePath;
                office.CameraId = OfficeCameraId;
                office.Enabled = true;
                office.IsProduction = false;
                if (string.IsNullOrWhiteSpace(office.DisplayName))
                    office.DisplayName = "Kancelářská kamera (test)";
            }

            var known = CameraRegistryService.Load(config.CamerasJsonPath).Cameras.Select(c => c.CameraId);
            StreamsService.Save(streams, config.StreamsJsonPath, known);
        }
        catch (Exception ex)
        {
            logger.Warn($"[BOOT] office stream ensure skipped: {ex.Message}");
        }
    }
}
