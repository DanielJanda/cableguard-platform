using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public enum CameraRuntimeState
{
    NotConfigured,
    ConfiguredNotReady,
    Ready,
    Fault,
}

public sealed class CameraApplyResult
{
    public bool Success { get; init; }
    public CameraRuntimeState State { get; init; }
    public string Message { get; init; } = "";
    public string MediaMtxPath { get; init; } = "";
    public string? SelectedProfile { get; init; }
    public string? Codec { get; init; }
    public string? Resolution { get; init; }
    public double? Fps { get; init; }
    public bool PathReady { get; init; }
    public bool WhepOk { get; init; }
    public bool PersistOk { get; init; }
    public bool NonPersistent { get; init; }
}

public sealed class CameraDependencyHit
{
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>
/// Applies camera registry entries to MediaMTX runtime + local YAML and keeps streams.json in sync.
/// Never logs RTSP URLs with credentials.
/// </summary>
public sealed class CameraRuntimeApplyService
{
    private readonly ControlCenterConfig _config;
    private readonly IMediaMtxApi _api;
    private readonly IMediaMtxConfigPersister _persister;
    private readonly ICredentialStore _credentials;
    private readonly IHttpProber _prober;
    private readonly ControlCenterLogger _logger;

    public CameraRuntimeApplyService(
        ControlCenterConfig config,
        IMediaMtxApi api,
        IMediaMtxConfigPersister persister,
        ICredentialStore credentials,
        IHttpProber prober,
        ControlCenterLogger logger)
    {
        _config = config;
        _api = api;
        _persister = persister;
        _credentials = credentials;
        _prober = prober;
        _logger = logger;
    }

    public static string SuggestPath(CameraEntry cam)
    {
        if (!string.IsNullOrWhiteSpace(cam.MediaMtxPath)) return cam.MediaMtxPath.Trim();
        if (cam.Host.StartsWith("10.6.1.", StringComparison.Ordinal) ||
            cam.SiteId.Contains("office", StringComparison.OrdinalIgnoreCase))
            return "office-test-camera";
        var slug = Regex.Replace(cam.CameraId.ToLowerInvariant(), @"[^a-z0-9\-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "camera-path" : slug;
    }

    public static string BuildRtspSource(CameraEntry cam, string username, string password)
    {
        var user = Uri.EscapeDataString(username);
        var pass = Uri.EscapeDataString(password);
        var profile = cam.Profile.Trim().TrimStart('/');
        var transport = string.Equals(cam.Transport, "udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
        return $"rtsp://{user}:{pass}@{cam.Host}:{cam.RtspPort}/{profile}?rtsp_transport={transport}";
    }

    public static string RedactRtsp(string url) =>
        LogRedactor.Redact(url);

    public async Task<CameraApplyResult> ApplyAsync(
        CameraEntry cam,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        void Report(string msg)
        {
            progress?.Report(msg);
            _logger.Info($"[CAMERA APPLY] {cam.CameraId}: {msg}");
        }

        if (!_credentials.TryRead(cam.CredentialRef, out var user, out var pass) || pass.Length == 0)
        {
            return Fail(CameraRuntimeState.ConfiguredNotReady,
                $"Credential ref '{cam.CredentialRef}' missing or empty. Set Přihlášení first.");
        }

        var path = SuggestPath(cam);
        cam.MediaMtxPath = path;

        Report($"RTSP probe (credentials redacted) host={cam.Host} profile={cam.Profile}");

        var probe = await RtspProbe.ProbeAsync(BuildRtspSource(cam, user, pass), ct).ConfigureAwait(false);
        if (!probe.Ok)
        {
            return Fail(CameraRuntimeState.ConfiguredNotReady,
                $"RTSP test failed: {probe.Message}. Camera saved as CONFIGURED / NOT READY.");
        }

        // Prefer H.264 when the configured profile is HEVC/H.265 (WebRTC/browser friendlier).
        if (!string.Equals(probe.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            Report($"Codec {probe.Codec} on {cam.Profile} — scanning for H.264 alternate…");
            var h264 = await TryFindH264ProfileAsync(cam, user, pass, ct).ConfigureAwait(false);
            if (h264 is not null)
            {
                cam.Profile = h264.Value.Profile;
                probe = h264.Value.Probe;
                Report($"Selected H.264 profile {cam.Profile}");
            }
            else
            {
                Report($"No H.264 alternate found — continuing with {probe.Codec} on {cam.Profile}");
            }
        }

        var source = BuildRtspSource(cam, user, pass);
        Report($"RTSP OK codec={probe.Codec ?? "?"} res={probe.Resolution ?? "?"} fps={probe.Fps?.ToString("0.#") ?? "?"}");

        var exists = await _api.ConfigPathExistsAsync(path, ct).ConfigureAwait(false);
        bool apiOk;
        if (exists == true)
        {
            Report($"Patching existing MediaMTX path '{path}'");
            apiOk = await _api.PatchPathSourceAsync(path, source, ct).ConfigureAwait(false);
        }
        else
        {
            Report($"Adding MediaMTX path '{path}'");
            apiOk = await _api.AddPathAsync(path, source, cam.Transport, ct).ConfigureAwait(false);
            if (!apiOk)
            {
                // Race: path created elsewhere — try patch.
                apiOk = await _api.PatchPathSourceAsync(path, source, ct).ConfigureAwait(false);
            }
        }

        if (!apiOk)
        {
            return Fail(CameraRuntimeState.Fault,
                $"MediaMTX API failed to create/patch path '{path}'. Camera stays NOT READY.");
        }

        var ready = await WaitReadyAsync(path, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        var whepUrl = $"{_config.WhepBaseLocal.TrimEnd('/')}/{path}/whep";
        var whepCode = await _prober.OptionsStatusCodeAsync(whepUrl, ct).ConfigureAwait(false);
        var whepOk = whepCode is >= 200 and < 300;
        Report($"path ready={ready} WHEP OPTIONS={whepCode?.ToString() ?? "null"}");

        var persistOk = _persister.UpsertPath(path, source, cam.Transport, out var persistMsg);
        Report(persistOk ? "YAML persist OK" : $"YAML persist FAILED: {persistMsg}");

        SyncOfficeStream(cam, path);

        if (!ready || !whepOk)
        {
            return new CameraApplyResult
            {
                Success = false,
                State = CameraRuntimeState.ConfiguredNotReady,
                Message = $"MediaMTX path '{path}' applied but not READY/WHEP yet. {persistMsg}",
                MediaMtxPath = path,
                SelectedProfile = cam.Profile,
                Codec = probe.Codec,
                Resolution = probe.Resolution,
                Fps = probe.Fps,
                PathReady = ready,
                WhepOk = whepOk,
                PersistOk = persistOk,
                NonPersistent = !persistOk,
            };
        }

        if (!persistOk)
        {
            return new CameraApplyResult
            {
                Success = true,
                State = CameraRuntimeState.Ready,
                Message = $"READY but NON-PERSISTENT — YAML write failed ({persistMsg}). Restart may lose path.",
                MediaMtxPath = path,
                SelectedProfile = cam.Profile,
                Codec = probe.Codec,
                Resolution = probe.Resolution,
                Fps = probe.Fps,
                PathReady = true,
                WhepOk = true,
                PersistOk = false,
                NonPersistent = true,
            };
        }

        return new CameraApplyResult
        {
            Success = true,
            State = CameraRuntimeState.Ready,
            Message = $"Camera READY on path '{path}'.",
            MediaMtxPath = path,
            SelectedProfile = cam.Profile,
            Codec = probe.Codec,
            Resolution = probe.Resolution,
            Fps = probe.Fps,
            PathReady = true,
            WhepOk = true,
            PersistOk = true,
        };
    }

    public async Task<(bool Ok, string Message)> RemovePathAsync(string pathName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathName)) return (true, "No path.");
        var deleted = await _api.DeletePathAsync(pathName, ct).ConfigureAwait(false);
        var yamlOk = _persister.RemovePath(pathName, out var msg);
        if (!deleted && !yamlOk)
            return (false, $"Failed to remove path '{pathName}' from API and YAML. {msg}");
        return (true, $"Path '{pathName}' removed (api={deleted}, yaml={yamlOk}). {msg}");
    }

    public static IReadOnlyList<CameraDependencyHit> FindDependencies(
        CameraEntry cam,
        CameraRegistryDocument registry,
        StreamsDocument streams,
        IEnumerable<DetectorInstance> detectors,
        IEnumerable<RoiProfile> rois,
        IEnumerable<ScenarioDocument> scenarios)
    {
        var hits = new List<CameraDependencyHit>();
        var path = string.IsNullOrWhiteSpace(cam.MediaMtxPath) ? cam.CameraId : cam.MediaMtxPath;

        foreach (var m in registry.StreamMappings.Where(m => m.PrimaryCameraId == cam.CameraId))
            hits.Add(new CameraDependencyHit { Kind = "stream_mapping", Id = m.LogicalStream, Detail = "primary_camera_id" });

        foreach (var s in streams.Streams.Where(s => s.CameraId == cam.CameraId || s.MediaMtxPath == path))
        {
            if (s.IsProduction)
                hits.Add(new CameraDependencyHit { Kind = "production_stream", Id = s.StreamId, Detail = "is_production=true" });
            else
                hits.Add(new CameraDependencyHit { Kind = "stream", Id = s.StreamId, Detail = $"mediamtx_path={s.MediaMtxPath}" });
        }

        foreach (var d in detectors.Where(d => d.InputStream == path || d.InputStream == cam.CameraId ||
                                               streams.Streams.Any(s => s.StreamId == d.InputStream && s.CameraId == cam.CameraId)))
            hits.Add(new CameraDependencyHit { Kind = "detector", Id = d.Id, Detail = $"input_stream={d.InputStream}" });

        foreach (var r in rois.Where(r => r.StreamId == path || r.StreamId == cam.CameraId ||
                                          streams.Streams.Any(s => s.StreamId == r.StreamId && s.CameraId == cam.CameraId)))
            hits.Add(new CameraDependencyHit { Kind = "roi", Id = r.Id, Detail = $"stream_id={r.StreamId}" });

        foreach (var sc in scenarios.Where(sc => sc.StreamId == path || sc.StreamId == cam.CameraId ||
                                                 streams.Streams.Any(s => s.StreamId == sc.StreamId && s.CameraId == cam.CameraId)))
            hits.Add(new CameraDependencyHit { Kind = "scenario", Id = sc.Id, Detail = $"stream_id={sc.StreamId}" });

        return hits;
    }

    private void SyncOfficeStream(CameraEntry cam, string path)
    {
        try
        {
            var registry = CameraRegistryService.Load(_config.CamerasJsonPath);
            var knownCams = registry.Cameras.Select(c => c.CameraId).Append(cam.CameraId).Distinct().ToList();

            var doc = StreamsService.Load(_config.StreamsJsonPath);
            var existing = doc.Streams.FirstOrDefault(s => s.MediaMtxPath == path || s.StreamId == path);
            if (existing is null)
            {
                doc.Streams.Add(new LogicalStream
                {
                    StreamId = path,
                    DisplayName = cam.DisplayName,
                    MediaMtxPath = path,
                    CameraId = cam.CameraId,
                    Enabled = true,
                    IsProduction = false,
                });
            }
            else
            {
                existing.CameraId = cam.CameraId;
                existing.MediaMtxPath = path;
                existing.Enabled = true;
                if (string.IsNullOrWhiteSpace(existing.DisplayName))
                    existing.DisplayName = cam.DisplayName;
            }

            // Disable stale office-test pointing at a missing / different camera id.
            foreach (var stale in doc.Streams.Where(s =>
                         s.StreamId == "office-test" &&
                         s.CameraId != cam.CameraId &&
                         path == "office-test-camera"))
            {
                stale.Enabled = false;
            }

            // Drop orphan streams that reference unknown cameras (blocks validate).
            doc.Streams.RemoveAll(s => !knownCams.Contains(s.CameraId, StringComparer.OrdinalIgnoreCase));

            StreamsService.Save(doc, _config.StreamsJsonPath, knownCams);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[CAMERA APPLY] streams.json sync warning: {ex.Message}");
        }
    }

    public void UpdateCameraRuntimeState(CameraEntry cam, CameraApplyResult result)
    {
        cam.MediaMtxPath = string.IsNullOrWhiteSpace(result.MediaMtxPath) ? cam.MediaMtxPath : result.MediaMtxPath;
        cam.RuntimeState = result.State switch
        {
            CameraRuntimeState.Ready => "ready",
            CameraRuntimeState.Fault => "fault",
            _ => "configured_not_ready",
        };
        cam.LastApplyMessage = result.Message;
        if (result.NonPersistent)
            cam.LastApplyMessage += " [NON-PERSISTENT]";
    }

    private async Task<bool> WaitReadyAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var ready = await _api.IsPathReadyAsync(path, ct).ConfigureAwait(false);
            if (ready == true) return true;
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        return false;
    }

    private static async Task<(string Profile, RtspProbe.Result Probe)?> TryFindH264ProfileAsync(
        CameraEntry cam, string user, string pass, CancellationToken ct)
    {
        foreach (var ch in new[] { 101, 102, 103, 201, 202 })
        {
            var profile = $"Streaming/Channels/{ch}";
            if (string.Equals(profile, cam.Profile, StringComparison.OrdinalIgnoreCase))
                continue;
            var trial = new CameraEntry
            {
                Host = cam.Host,
                RtspPort = cam.RtspPort,
                Profile = profile,
                Transport = cam.Transport,
            };
            var probe = await RtspProbe.ProbeAsync(BuildRtspSource(trial, user, pass), ct).ConfigureAwait(false);
            if (probe.Ok &&
                string.Equals(probe.Codec, "h264", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(probe.Resolution) &&
                probe.Fps is > 0 and < 120)
                return (profile, probe);
        }
        return null;
    }

    private static CameraApplyResult Fail(CameraRuntimeState state, string message) => new()
    {
        Success = false,
        State = state,
        Message = message,
    };
}

public static class RtspProbe
{
    public sealed record Result(bool Ok, string Message, string? Codec, string? Resolution, double? Fps);

    public static async Task<Result> ProbeAsync(string rtspUrl, CancellationToken ct = default)
    {
        var ffprobe = StreamFrameGrabber.FindFfmpeg()?.Replace("ffmpeg.exe", "ffprobe.exe", StringComparison.OrdinalIgnoreCase)
                      ?? FindFfprobe();
        if (ffprobe is null || !File.Exists(ffprobe))
        {
            // Fallback: one-frame ffmpeg grab proves connectivity without parsing JSON.
            var grab = await StreamFrameGrabber.GrabJpegFromUrlAsync(rtspUrl, ct).ConfigureAwait(false);
            return grab.Ok
                ? new Result(true, "ffmpeg frame grab OK", null, null, null)
                : new Result(false, grab.Message, null, null, null);
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            Arguments = $"-v error -rtsp_transport tcp -timeout 8000000 -select_streams v:0 " +
                        $"-show_entries stream=codec_name,width,height,r_frame_rate -of json \"{rtspUrl}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe failed to start");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* ignore */ }
            return new Result(false, "ffprobe timeout", null, null, null);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (proc.ExitCode != 0)
            return new Result(false, Truncate(LogRedactor.Redact(stderr)), null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var stream = doc.RootElement.GetProperty("streams")[0];
            var codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() : null;
            var w = stream.TryGetProperty("width", out var ww) ? ww.GetInt32() : 0;
            var h = stream.TryGetProperty("height", out var hh) ? hh.GetInt32() : 0;
            double? fps = null;
            if (stream.TryGetProperty("r_frame_rate", out var fr))
            {
                var parts = fr.GetString()?.Split('/');
                if (parts is { Length: 2 } &&
                    double.TryParse(parts[0], out var a) && double.TryParse(parts[1], out var b) && b > 0)
                    fps = a / b;
            }
            return new Result(true, "OK", codec, w > 0 && h > 0 ? $"{w}x{h}" : null, fps);
        }
        catch
        {
            return new Result(true, "OK (unparsed)", null, null, null);
        }
    }

    private static string? FindFfprobe()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir.Trim(), "ffprobe.exe");
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string Truncate(string s) =>
        s.Length <= 200 ? s.Trim() : s.Trim()[..200] + "…";
}
