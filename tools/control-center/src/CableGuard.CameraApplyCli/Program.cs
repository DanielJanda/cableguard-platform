using System.Net.Http;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.CameraApplyCli;

/// <summary>
/// Headless apply / probe. Never prints RTSP URLs with credentials.
/// Usage:
///   CableGuard.CameraApplyCli [camera_id]
///   CableGuard.CameraApplyCli --probe-channels [camera_id]
///   CableGuard.CameraApplyCli --prefer-h264 [camera_id]
///   CableGuard.CameraApplyCli --audit-profiles [camera_id]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var config = ControlCenterConfig.LoadOrDefault();
        var logger = new ControlCenterLogger(config.LogsDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var api = new MediaMtxApiClient(http, config.MediaMtxApiBase);
        var persister = new MediaMtxLocalConfigPersister(config.MediaMtxLocalYml);
        var creds = new WindowsCredentialStore();
        var prober = new HttpProber(http);
        var apply = new CameraRuntimeApplyService(config, api, persister, creds, prober, logger);
        var audit = new CameraProfileAuditService(creds, logger);

        var registry = CameraRegistryService.Load(config.CamerasJsonPath);
        var probeOnly = args.Contains("--probe-channels");
        var preferH264 = args.Contains("--prefer-h264");
        var auditProfiles = args.Contains("--audit-profiles");
        var cameraId = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        CameraEntry? cam = null;
        if (!string.IsNullOrWhiteSpace(cameraId))
            cam = registry.Cameras.FirstOrDefault(c => c.CameraId == cameraId);
        cam ??= registry.Cameras.FirstOrDefault(c => c.Host == "10.6.1.63");
        if (cam is null)
        {
            Console.Error.WriteLine("Camera 10.6.1.63 / camera_id not found in registry.");
            return 2;
        }

        if (auditProfiles)
        {
            Console.WriteLine($"Auditing profiles for {cam.CameraId} host={cam.Host} (credentials redacted)");
            await audit.AuditCameraAsync(cam);
            cam.ValidatedProfile ??= CameraProfileAuditService.CreateProvisionalBaseline(
                CameraProfileAuditService.ParseChannel(cam.Profile) ?? 102);
            foreach (var p in cam.StreamProfiles.OrderBy(x => x.ChannelId))
            {
                Console.WriteLine($"--- {p.ChannelId} {p.StreamType.ToUpperInvariant()} enabled={p.Enabled} status={p.AuditStatus}");
                Console.WriteLine($"  configured: {p.Current.Encoding} {p.Current.Width}x{p.Current.Height} @{p.Current.Fps}fps " +
                                  $"{p.Current.BitrateType} {p.Current.BitrateKbps}kbps gov={p.Current.GovLength} " +
                                  $"smart={p.Current.SmartCodec} svc={p.Current.Svc} audio={p.Current.Audio}");
                Console.WriteLine($"  capabilities encodings=[{string.Join(",", p.Capabilities.Encodings)}] " +
                                  $"resolutions=[{string.Join(",", p.Capabilities.Resolutions.Take(8))}…]");
                Console.WriteLine($"  observed: ok={p.Observed.Ok} {p.Observed.CodecName} {p.Observed.Width}x{p.Observed.Height} " +
                                  $"avg={p.Observed.AvgFrameRate} r={p.Observed.RFrameRate} profile={p.Observed.Profile} " +
                                  $"bframes={p.Observed.HasBFrames} pix={p.Observed.PixFmt}");
                Console.WriteLine($"  msg: {p.AuditMessage}");
                if (cam.ValidatedProfile is not null && p.ChannelId == cam.ValidatedProfile.ChannelId)
                {
                    var drift = CameraProfileAuditService.CompareToValidated(p, cam.ValidatedProfile);
                    Console.WriteLine($"  drift vs provisional: {drift.Status} — {drift.Message}");
                }
            }
            Console.WriteLine($"model={cam.Model} firmware={cam.Firmware}");
            var idxA = registry.Cameras.FindIndex(c => c.CameraId == cam.CameraId);
            if (idxA >= 0) registry.Cameras[idxA] = cam;
            CameraRegistryService.Save(registry, config.CamerasJsonPath);

            // Ensure office-test-camera logical stream maps to 102 SUB without touching production.
            var streams = StreamsService.Load(config.StreamsJsonPath);
            StreamsService.EnrichMissingProfileFields(registry, streams);
            var office = streams.Streams.FirstOrDefault(s => s.StreamId == "office-test-camera" || s.MediaMtxPath == "office-test-camera");
            if (office is not null)
            {
                var sub = cam.StreamProfiles.FirstOrDefault(p => p.ChannelId == 102);
                if (sub is not null)
                {
                    office.CameraId = cam.CameraId;
                    office.ProfileId = sub.ProfileId;
                    office.ChannelId = 102;
                    office.StreamType = "sub";
                    office.ProfileFingerprint = sub.ConfigurationFingerprint;
                }
            }
            var known = registry.Cameras.Select(c => c.CameraId);
            StreamsService.Save(streams, config.StreamsJsonPath, known);
            Console.WriteLine("Saved cameras.json + enriched streams.json (no production apply).");
            return 0;
        }

        if (!creds.TryRead(cam.CredentialRef, out var user, out var pass) || pass.Length == 0)
        {
            Console.Error.WriteLine($"Credential ref '{cam.CredentialRef}' missing.");
            return 3;
        }

        if (probeOnly || preferH264)
        {
            var channels = new[] { 101, 102, 103, 201, 202 };
            string? h264Profile = null;
            foreach (var ch in channels)
            {
                var trial = Clone(cam);
                trial.Profile = $"Streaming/Channels/{ch}";
                var source = CameraRuntimeApplyService.BuildRtspSource(trial, user, pass);
                var probe = await RtspProbe.ProbeAsync(source);
                Console.WriteLine($"channel={ch} ok={probe.Ok} codec={probe.Codec ?? "-"} res={probe.Resolution ?? "-"} fps={probe.Fps?.ToString("0.#") ?? "-"} msg={probe.Message}");
                if (probe.Ok && string.Equals(probe.Codec, "h264", StringComparison.OrdinalIgnoreCase) && h264Profile is null)
                    h264Profile = trial.Profile;
            }

            if (probeOnly && !preferH264)
                return 0;

            if (h264Profile is null)
            {
                Console.WriteLine("No H.264 channel found — keeping current profile.");
            }
            else
            {
                Console.WriteLine($"Selecting H.264 profile {h264Profile}");
                cam.Profile = h264Profile;
            }
        }

        if (string.IsNullOrWhiteSpace(cam.MediaMtxPath) ||
            cam.MediaMtxPath is "office-test" or "office")
            cam.MediaMtxPath = "office-test-camera";

        Console.WriteLine($"Applying {cam.CameraId} host={cam.Host} path={cam.MediaMtxPath} profile={cam.Profile}");
        var result = await apply.ApplyAsync(cam, new Progress<string>(m => Console.WriteLine("  " + m)));
        apply.UpdateCameraRuntimeState(cam, result);
        var idx = registry.Cameras.FindIndex(c => c.CameraId == cam.CameraId);
        if (idx >= 0) registry.Cameras[idx] = cam;
        CameraRegistryService.Save(registry, config.CamerasJsonPath);

        Console.WriteLine($"RESULT success={result.Success} state={result.State}");
        Console.WriteLine($"  path={result.MediaMtxPath} ready={result.PathReady} whep={result.WhepOk} persist={result.PersistOk}");
        Console.WriteLine($"  codec={result.Codec} res={result.Resolution} fps={result.Fps}");
        Console.WriteLine($"  msg={result.Message}");
        return result.Success ? 0 : 1;
    }

    private static CameraEntry Clone(CameraEntry c) => new()
    {
        CameraId = c.CameraId,
        DisplayName = c.DisplayName,
        SiteId = c.SiteId,
        StationId = c.StationId,
        Host = c.Host,
        RtspPort = c.RtspPort,
        Profile = c.Profile,
        Transport = c.Transport,
        Enabled = c.Enabled,
        CredentialRef = c.CredentialRef,
        MediaMtxPath = c.MediaMtxPath,
        RuntimeState = c.RuntimeState,
        LastApplyMessage = c.LastApplyMessage,
    };
}
