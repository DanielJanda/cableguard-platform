using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Discovers Hikvision ISAPI stream profiles (current + capabilities) and observes RTSP via ffprobe.
/// Never logs credentials or full RTSP URLs with passwords.
/// </summary>
public sealed class CameraProfileAuditService
{
    private readonly ICredentialStore _credentials;
    private readonly ControlCenterLogger _logger;

    public CameraProfileAuditService(ICredentialStore credentials, ControlCenterLogger logger)
    {
        _credentials = credentials;
        _logger = logger;
    }

    public static string StreamTypeForChannel(int channelId) => channelId switch
    {
        101 or 201 => "main",
        102 or 202 => "sub",
        103 or 203 => "third",
        _ => "unknown",
    };

    public static string ProfileIdFor(string cameraId, int channelId) =>
        $"{cameraId}-ch{channelId}";

    public static string RtspPathForChannel(int channelId) =>
        $"Streaming/Channels/{channelId}";

    public static string ComputeFingerprint(CameraStreamProfile p)
    {
        var c = p.Current;
        var raw = string.Join("|",
            p.ChannelId,
            p.StreamType,
            c.Encoding ?? "",
            c.Width?.ToString() ?? "",
            c.Height?.ToString() ?? "",
            c.Fps?.ToString("0.##") ?? "",
            c.BitrateType ?? "",
            c.BitrateKbps?.ToString() ?? "",
            c.GovLength?.ToString() ?? "",
            c.SmartCodec?.ToString() ?? "",
            c.Svc?.ToString() ?? "",
            c.Audio?.ToString() ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
    }

    public static ValidatedCameraProfile CreateProvisionalBaseline(int channelId = 102) => new()
    {
        ValidatedProfileId = $"cg-provisional-h264-720p-ch{channelId}",
        Provisional = true,
        ChannelId = channelId,
        StreamType = StreamTypeForChannel(channelId),
        Encoding = "H.264",
        Width = 1280,
        Height = 720,
        Fps = 25,
        BitrateType = "CBR",
        BitrateKbps = 2048,
        GovLength = 25,
        H264Profile = "Main",
        SmartCodec = false,
        Svc = false,
        Audio = false,
        Transport = "tcp",
        Note = "PROVISIONAL CableGuard baseline — must be confirmed by Video Qualification Lab.",
    };

    /// <summary>Migrate legacy Profile string into StreamProfiles if empty.</summary>
    public static void EnsureMigrated(CameraEntry cam)
    {
        if (cam.StreamProfiles.Count > 0) return;
        var ch = ParseChannel(cam.Profile) ?? 102;
        var path = string.IsNullOrWhiteSpace(cam.Profile) ? RtspPathForChannel(ch) : cam.Profile.Trim().TrimStart('/');
        var p = new CameraStreamProfile
        {
            ProfileId = ProfileIdFor(cam.CameraId, ch),
            CameraId = cam.CameraId,
            ChannelId = ch,
            StreamType = StreamTypeForChannel(ch),
            RtspPath = path,
            Enabled = true,
            AuditStatus = "unknown",
            AuditMessage = "Migrated from legacy profile field — audit required.",
        };
        cam.StreamProfiles.Add(p);
        if (string.IsNullOrWhiteSpace(cam.SelectedProfileId))
            cam.SelectedProfileId = p.ProfileId;
        if (string.IsNullOrWhiteSpace(cam.Profile))
            cam.Profile = path;
    }

    public static int? ParseChannel(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) return null;
        var m = Regex.Match(profile, @"Channels/(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var ch)) return ch;
        if (int.TryParse(profile.Trim(), out var bare)) return bare;
        return null;
    }

    public async Task AuditCameraAsync(CameraEntry cam, CancellationToken ct = default)
    {
        EnsureMigrated(cam);
        if (!_credentials.TryRead(cam.CredentialRef, out var user, out var pass) || pass.Length == 0)
        {
            foreach (var p in cam.StreamProfiles)
            {
                p.AuditStatus = "unreachable";
                p.AuditMessage = "Credential missing.";
            }
            _logger.Warn($"[PROFILE AUDIT] {cam.CameraId}: credential missing");
            return;
        }

        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(user, pass),
            PreAuthenticate = false,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };

        try
        {
            var infoXml = await GetStringAsync(http, $"http://{cam.Host}/ISAPI/System/deviceInfo", ct);
            if (infoXml is not null)
            {
                cam.Model = XmlText(infoXml, "model") ?? cam.Model;
                cam.Firmware = XmlText(infoXml, "firmwareVersion") ?? cam.Firmware;
                cam.Manufacturer = XmlText(infoXml, "manufacturer") ?? cam.Manufacturer;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"[PROFILE AUDIT] {cam.CameraId}: deviceInfo failed: {ex.Message}");
        }

        var channels = new[] { 101, 102, 103 };
        var found = new List<CameraStreamProfile>();
        foreach (var ch in channels)
        {
            var profile = cam.StreamProfiles.FirstOrDefault(p => p.ChannelId == ch)
                          ?? new CameraStreamProfile
                          {
                              ProfileId = ProfileIdFor(cam.CameraId, ch),
                              CameraId = cam.CameraId,
                              ChannelId = ch,
                              StreamType = StreamTypeForChannel(ch),
                              RtspPath = RtspPathForChannel(ch),
                          };

            try
            {
                var currentXml = await GetStringAsync(http, $"http://{cam.Host}/ISAPI/Streaming/channels/{ch}", ct);
                var capsXml = await GetStringAsync(http, $"http://{cam.Host}/ISAPI/Streaming/channels/{ch}/capabilities", ct);
                if (currentXml is null && capsXml is null)
                {
                    profile.Enabled = false;
                    profile.AuditStatus = "unknown";
                    profile.AuditMessage = $"Channel {ch} not available.";
                    continue;
                }

                if (currentXml is not null)
                    profile.Current = ParseCurrent(currentXml);
                if (capsXml is not null)
                    profile.Capabilities = ParseCapabilities(capsXml);

                profile.Enabled = true;
                profile.RtspPath = RtspPathForChannel(ch);

                // Observed RTSP (credentials in memory only)
                var trial = new CameraEntry
                {
                    Host = cam.Host,
                    RtspPort = cam.RtspPort,
                    Profile = profile.RtspPath,
                    Transport = cam.Transport,
                };
                var source = CameraRuntimeApplyService.BuildRtspSource(trial, user, pass);
                profile.Observed = await ObserveRtspAsync(source, ct);

                profile.ConfigurationFingerprint = ComputeFingerprint(profile);
                profile.LastAuditedAt = DateTime.UtcNow.ToString("o");
                profile.AuditStatus = "unknown";
                profile.AuditMessage = BuildAuditSummary(profile);
                found.Add(profile);
            }
            catch (Exception ex)
            {
                profile.AuditStatus = "unreachable";
                profile.AuditMessage = ex.Message;
                found.Add(profile);
                _logger.Warn($"[PROFILE AUDIT] {cam.CameraId} ch{ch}: {ex.Message}");
            }
        }

        cam.StreamProfiles = found;
        if (string.IsNullOrWhiteSpace(cam.SelectedProfileId) ||
            cam.StreamProfiles.All(p => p.ProfileId != cam.SelectedProfileId))
        {
            var prefer = cam.StreamProfiles.FirstOrDefault(p => p.ChannelId == 102)
                         ?? cam.StreamProfiles.FirstOrDefault();
            if (prefer is not null)
            {
                cam.SelectedProfileId = prefer.ProfileId;
                cam.Profile = prefer.RtspPath;
            }
        }

        // Drift vs validated
        if (cam.ValidatedProfile is not null)
        {
            foreach (var p in cam.StreamProfiles.Where(x => x.ChannelId == cam.ValidatedProfile.ChannelId))
            {
                var drift = CompareToValidated(p, cam.ValidatedProfile);
                p.AuditStatus = drift.Status;
                p.AuditMessage = drift.Message;
            }
        }

        cam.LastProfileAuditAt = DateTime.UtcNow.ToString("o");
        _logger.Info($"[PROFILE AUDIT] {cam.CameraId}: {cam.StreamProfiles.Count} profiles, model={cam.Model}");
    }

    public static DriftReport CompareToValidated(CameraStreamProfile profile, ValidatedCameraProfile expected)
    {
        var changes = new List<DriftChange>();
        void Cmp(string field, string? exp, string? act)
        {
            if (string.IsNullOrWhiteSpace(exp)) return;
            var a = act ?? "";
            var e = exp;
            if (!string.Equals(NormalizeCodec(e), NormalizeCodec(a), StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(e, a, StringComparison.OrdinalIgnoreCase))
                changes.Add(new DriftChange { Field = field, Expected = e, Actual = string.IsNullOrEmpty(a) ? "(missing)" : a });
        }

        Cmp("encoding", expected.Encoding, profile.Current.Encoding);
        Cmp("resolution", $"{expected.Width}x{expected.Height}",
            profile.Current.Width is int w && profile.Current.Height is int h ? $"{w}x{h}" : null);
        Cmp("fps", expected.Fps.ToString("0.##"), profile.Current.Fps?.ToString("0.##"));
        Cmp("bitrate_type", expected.BitrateType, profile.Current.BitrateType);
        Cmp("bitrate_kbps", expected.BitrateKbps.ToString(), profile.Current.BitrateKbps?.ToString());
        Cmp("gov_length", expected.GovLength.ToString(), profile.Current.GovLength?.ToString());
        // Missing SmartCodec/SVC/Audio on camera XML ≈ OFF for compliance purposes.
        Cmp("smart_codec", expected.SmartCodec ? "true" : "false",
            profile.Current.SmartCodec is null ? "false" : (profile.Current.SmartCodec.Value ? "true" : "false"));
        Cmp("svc", expected.Svc ? "true" : "false",
            profile.Current.Svc is null ? "false" : (profile.Current.Svc.Value ? "true" : "false"));
        Cmp("audio", expected.Audio ? "true" : "false",
            profile.Current.Audio is null ? "false" : (profile.Current.Audio.Value ? "true" : "false"));

        if (changes.Count == 0)
        {
            return new DriftReport
            {
                Status = "compliant",
                ProfileId = profile.ProfileId,
                Message = "COMPLIANT with validated profile.",
            };
        }

        var msg = "DRIFTED: " + string.Join("; ",
            changes.Select(c => $"{c.Field}: expected {c.Expected}, actual {c.Actual}"));
        return new DriftReport
        {
            Status = "drifted",
            ProfileId = profile.ProfileId,
            Changes = changes,
            Message = msg,
        };
    }

    public static string NormalizeCodec(string? c)
    {
        if (string.IsNullOrWhiteSpace(c)) return "";
        c = c.Trim().ToUpperInvariant().Replace(" ", "");
        if (c is "H264" or "AVC") return "H.264";
        if (c is "H265" or "HEVC") return "H.265";
        if (c.Contains("264")) return "H.264";
        if (c.Contains("265") || c.Contains("HEVC")) return "H.265";
        return c;
    }

    public static bool RoiMatchesFingerprint(RoiProfile roi, string? currentFingerprint)
    {
        if (string.IsNullOrWhiteSpace(roi.StreamProfileFingerprint)) return true; // legacy
        if (string.IsNullOrWhiteSpace(currentFingerprint)) return false;
        return string.Equals(roi.StreamProfileFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAuditSummary(CameraStreamProfile p)
    {
        var cfg = $"{p.Current.Encoding ?? "?"} {p.Current.Width}x{p.Current.Height} @{p.Current.Fps?.ToString("0.#") ?? "?"}fps";
        var obs = p.Observed.Ok
            ? $"{p.Observed.CodecName} {p.Observed.Width}x{p.Observed.Height} @{p.Observed.Fps?.ToString("0.#") ?? "?"}"
            : $"probe:{p.Observed.Message}";
        return $"configured=[{cfg}] observed=[{obs}] caps=[{string.Join(",", p.Capabilities.Encodings)}]";
    }

    private static StreamConfigSnapshot ParseCurrent(string xml)
    {
        var fpsRaw = XmlText(xml, "maxFrameRate");
        double? fps = null;
        if (int.TryParse(fpsRaw, out var hikFps) && hikFps > 0)
            fps = hikFps >= 100 ? hikFps / 100.0 : hikFps;

        bool? ParseBool(string tag)
        {
            var t = XmlText(xml, tag);
            if (t is null) return null;
            return t is "true" or "1";
        }

        // SmartCodec may be absent
        var smart = ParseBool("SmartCodec");
        var svc = Regex.Match(xml, @"<SVC>\s*<enabled>(.*?)</enabled>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        bool? svcEn = svc.Success ? svc.Groups[1].Value is "true" or "1" : null;
        var audio = Regex.Match(xml, @"<Audio>\s*<enabled>(.*?)</enabled>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return new StreamConfigSnapshot
        {
            Encoding = XmlText(xml, "videoCodecType"),
            Width = IntXml(xml, "videoResolutionWidth"),
            Height = IntXml(xml, "videoResolutionHeight"),
            Fps = fps,
            BitrateType = XmlText(xml, "videoQualityControlType"),
            BitrateKbps = IntXml(xml, "constantBitRate"),
            H264Profile = XmlText(xml, "H264Profile"),
            GovLength = IntXml(xml, "GovLength"),
            SmartCodec = smart,
            Svc = svcEn,
            Audio = audio.Success ? audio.Groups[1].Value is "true" or "1" : null,
        };
    }

    private static StreamCapabilities ParseCapabilities(string xml)
    {
        var encOpt = XmlAttrOpt(xml, "videoCodecType");
        var encodings = SplitOpt(encOpt);
        if (encodings.Count == 0)
        {
            var cur = XmlText(xml, "videoCodecType");
            if (!string.IsNullOrWhiteSpace(cur)) encodings.Add(cur);
        }

        var widths = SplitOpt(XmlAttrOpt(xml, "videoResolutionWidth"));
        var heights = SplitOpt(XmlAttrOpt(xml, "videoResolutionHeight"));
        var res = new List<string>();
        for (var i = 0; i < Math.Min(widths.Count, heights.Count); i++)
            res.Add($"{widths[i]}x{heights[i]}");

        return new StreamCapabilities
        {
            Encodings = encodings,
            H264Supported = encodings.Any(e => NormalizeCodec(e) == "H.264"),
            H265Supported = encodings.Any(e => NormalizeCodec(e) == "H.265"),
            MjpegSupported = encodings.Any(e => e.Contains("MJPEG", StringComparison.OrdinalIgnoreCase)),
            Resolutions = res,
            H264Profiles = SplitOpt(XmlAttrOpt(xml, "H264Profile")),
            FpsOptions = SplitOpt(XmlAttrOpt(xml, "maxFrameRate")),
            GovMin = AttrInt(xml, "GovLength", "min"),
            GovMax = AttrInt(xml, "GovLength", "max"),
            SvcSupported = xml.Contains("<SVC>", StringComparison.OrdinalIgnoreCase),
            AudioOptional = xml.Contains("<Audio>", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static async Task<StreamObservedSnapshot> ObserveRtspAsync(string rtspUrl, CancellationToken ct)
    {
        var probe = await RtspProbe.ProbeAsync(rtspUrl, ct).ConfigureAwait(false);
        // Extended probe via ffprobe JSON if available
        var ffprobe = FindFfprobe();
        if (ffprobe is null)
        {
            return new StreamObservedSnapshot
            {
                Ok = probe.Ok,
                CodecName = probe.Codec,
                Width = ParseRes(probe.Resolution)?.w,
                Height = ParseRes(probe.Resolution)?.h,
                Fps = probe.Fps,
                Message = probe.Message,
            };
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v error -rtsp_transport tcp -timeout 8000000 -select_streams v:0 " +
                            $"-show_entries stream=codec_name,width,height,avg_frame_rate,r_frame_rate,bit_rate,profile,has_b_frames,pix_fmt " +
                            $"-of json \"{rtspUrl}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)
                             ?? throw new InvalidOperationException("ffprobe start failed");
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
                return new StreamObservedSnapshot { Ok = false, Message = "ffprobe failed" };

            using var doc = JsonDocument.Parse(stdout);
            var s = doc.RootElement.GetProperty("streams")[0];
            double? fps = null;
            if (s.TryGetProperty("avg_frame_rate", out var afr))
                fps = ParseRate(afr.GetString());
            if (fps is null or <= 0 && s.TryGetProperty("r_frame_rate", out var rfr))
                fps = ParseRate(rfr.GetString());

            return new StreamObservedSnapshot
            {
                Ok = true,
                CodecName = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null,
                Width = s.TryGetProperty("width", out var w) ? w.GetInt32() : null,
                Height = s.TryGetProperty("height", out var h) ? h.GetInt32() : null,
                AvgFrameRate = s.TryGetProperty("avg_frame_rate", out var af) ? af.GetString() : null,
                RFrameRate = s.TryGetProperty("r_frame_rate", out var rf) ? rf.GetString() : null,
                Fps = fps,
                Bitrate = s.TryGetProperty("bit_rate", out var br) ? br.GetString() : null,
                Profile = s.TryGetProperty("profile", out var pr) ? pr.GetString() : null,
                HasBFrames = s.TryGetProperty("has_b_frames", out var bf) && bf.ValueKind == JsonValueKind.Number
                    ? bf.GetInt32() > 0
                    : null,
                PixFmt = s.TryGetProperty("pix_fmt", out var pf) ? pf.GetString() : null,
                Message = "OK",
            };
        }
        catch (Exception ex)
        {
            return new StreamObservedSnapshot { Ok = false, Message = ex.Message };
        }
    }

    private static string? FindFfprobe()
    {
        var ffmpeg = StreamFrameGrabber.FindFfmpeg();
        if (ffmpeg is not null)
        {
            var probe = ffmpeg.Replace("ffmpeg.exe", "ffprobe.exe", StringComparison.OrdinalIgnoreCase)
                .Replace("ffmpeg", "ffprobe");
            if (File.Exists(probe)) return probe;
        }
        return null;
    }

    private static double? ParseRate(string? rate)
    {
        if (string.IsNullOrWhiteSpace(rate)) return null;
        var parts = rate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], out var a) && double.TryParse(parts[1], out var b) && b > 0)
            return a / b;
        return double.TryParse(rate, out var v) ? v : null;
    }

    private static (int w, int h)? ParseRes(string? res)
    {
        if (string.IsNullOrWhiteSpace(res)) return null;
        var p = res.ToLowerInvariant().Split('x');
        if (p.Length == 2 && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h))
            return (w, h);
        return null;
    }

    private static async Task<string?> GetStringAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static string? XmlText(string xml, string tag)
    {
        var m = Regex.Match(xml, $@"<{tag}(?:\s[^>]*)?>(.*?)</{tag}>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static int? IntXml(string xml, string tag) =>
        int.TryParse(XmlText(xml, tag), out var n) ? n : null;

    private static string? XmlAttrOpt(string xml, string tag)
    {
        var m = Regex.Match(xml, $@"<{tag}\s+[^>]*opt=""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static int? AttrInt(string xml, string tag, string attr)
    {
        var m = Regex.Match(xml, $@"<{tag}\s+[^>]*{attr}=""(\d+)""", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    private static List<string> SplitOpt(string? opt) =>
        string.IsNullOrWhiteSpace(opt)
            ? new List<string>()
            : opt.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
