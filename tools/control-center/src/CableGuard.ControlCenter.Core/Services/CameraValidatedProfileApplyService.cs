using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Applies a validated CableGuard stream profile via Hikvision ISAPI with backup/rollback.
/// Never auto-applies to production without explicit confirmation (caller responsibility).
/// Never logs credentials.
/// </summary>
public sealed class CameraValidatedProfileApplyService
{
    private readonly ICredentialStore _credentials;
    private readonly ControlCenterLogger _logger;
    private readonly CameraProfileAuditService _audit;
    private readonly CameraRuntimeApplyService _runtime;

    public CameraValidatedProfileApplyService(
        ICredentialStore credentials,
        ControlCenterLogger logger,
        CameraProfileAuditService audit,
        CameraRuntimeApplyService runtime)
    {
        _credentials = credentials;
        _logger = logger;
        _audit = audit;
        _runtime = runtime;
    }

    public sealed class ApplyResult
    {
        public bool Success { get; init; }
        public bool RolledBack { get; init; }
        public string Message { get; init; } = "";
        public DriftReport? DriftAfter { get; init; }
    }

    public async Task<ApplyResult> ApplyAsync(
        CameraEntry cam,
        ValidatedCameraProfile target,
        string? mediaMtxPath = null,
        bool confirmProduction = false,
        CancellationToken ct = default)
    {
        if (cam.IsProductionLike() && !confirmProduction)
        {
            return new ApplyResult
            {
                Success = false,
                Message = "Production camera requires explicit confirmProduction=true.",
            };
        }

        if (!_credentials.TryRead(cam.CredentialRef, out var user, out var pass) || pass.Length == 0)
            return new ApplyResult { Success = false, Message = "Credential missing." };

        await _audit.AuditCameraAsync(cam, ct).ConfigureAwait(false);
        var profile = cam.StreamProfiles.FirstOrDefault(p => p.ChannelId == target.ChannelId);
        if (profile is null)
            return new ApplyResult { Success = false, Message = $"Channel {target.ChannelId} not discovered." };

        var supportErrors = ValidateAgainstCapabilities(profile.Capabilities, target);
        if (supportErrors.Count > 0)
        {
            return new ApplyResult
            {
                Success = false,
                Message = "UNSUPPORTED: " + string.Join("; ", supportErrors),
            };
        }

        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(user, pass),
            PreAuthenticate = false,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var url = $"http://{cam.Host}/ISAPI/Streaming/channels/{target.ChannelId}";

        string backupXml;
        try
        {
            backupXml = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ApplyResult { Success = false, Message = $"Cannot read current config: {ex.Message}" };
        }

        string newXml;
        try
        {
            newXml = PatchStreamingChannelXml(backupXml, target);
        }
        catch (Exception ex)
        {
            return new ApplyResult { Success = false, Message = $"Cannot build config XML: {ex.Message}" };
        }

        _logger.Info($"[VALIDATED APPLY] {cam.CameraId} ch{target.ChannelId}: putting profile (credentials redacted)");

        try
        {
            using var content = new StringContent(newXml, Encoding.UTF8, "application/xml");
            using var resp = await http.PutAsync(url, content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new ApplyResult
                {
                    Success = false,
                    Message = $"ISAPI PUT failed {(int)resp.StatusCode}: {Truncate(body)}",
                };
            }
        }
        catch (Exception ex)
        {
            return new ApplyResult { Success = false, Message = $"ISAPI PUT error: {ex.Message}" };
        }

        await Task.Delay(1500, ct).ConfigureAwait(false);
        await _audit.AuditCameraAsync(cam, ct).ConfigureAwait(false);
        profile = cam.StreamProfiles.FirstOrDefault(p => p.ChannelId == target.ChannelId)!;
        var drift = CameraProfileAuditService.CompareToValidated(profile, target);

        if (drift.Status != "compliant" || profile.Observed is { Ok: false })
        {
            _logger.Warn($"[VALIDATED APPLY] verify failed — rolling back {cam.CameraId} ch{target.ChannelId}");
            try
            {
                using var content = new StringContent(backupXml, Encoding.UTF8, "application/xml");
                await http.PutAsync(url, content, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new ApplyResult
                {
                    Success = false,
                    RolledBack = false,
                    Message = $"Verify failed and rollback failed: {ex.Message}. Drift: {drift.Message}",
                    DriftAfter = drift,
                };
            }

            await _audit.AuditCameraAsync(cam, ct).ConfigureAwait(false);
            return new ApplyResult
            {
                Success = false,
                RolledBack = true,
                Message = $"Verify failed — rolled back. {drift.Message}",
                DriftAfter = drift,
            };
        }

        // Refresh MediaMTX path for this profile
        var path = mediaMtxPath ?? CameraRuntimeApplyService.SuggestPath(cam);
        CameraRuntimeApplyService.SelectProfile(cam, profile);
        var apply = await _runtime.ApplyPathAsync(cam, profile, path, ct).ConfigureAwait(false);
        if (!apply.Success)
        {
            return new ApplyResult
            {
                Success = false,
                Message = $"Camera config OK but MediaMTX apply failed: {apply.Message}",
                DriftAfter = drift,
            };
        }

        target.ValidatedAt ??= DateTime.UtcNow.ToString("o");
        target.CameraModel = cam.Model;
        target.Firmware = cam.Firmware;
        target.ConfigurationFingerprint = profile.ConfigurationFingerprint;
        cam.ValidatedProfile = target;

        return new ApplyResult
        {
            Success = true,
            Message = $"Applied validated profile to ch{target.ChannelId}; MediaMTX '{path}' READY.",
            DriftAfter = drift,
        };
    }

    public static List<string> ValidateAgainstCapabilities(StreamCapabilities caps, ValidatedCameraProfile target)
    {
        var errors = new List<string>();
        if (caps.Encodings.Count > 0 &&
            !caps.Encodings.Any(e =>
                string.Equals(
                    CameraProfileAuditService.NormalizeCodec(e),
                    CameraProfileAuditService.NormalizeCodec(target.Encoding),
                    StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"encoding {target.Encoding} not in [{string.Join(",", caps.Encodings)}]");
        }

        var res = $"{target.Width}x{target.Height}";
        if (caps.Resolutions.Count > 0 &&
            !caps.Resolutions.Any(r => string.Equals(r, res, StringComparison.OrdinalIgnoreCase)))
        {
            // Some cameras list widths/heights separately — soft warning only if exact pair missing
            errors.Add($"resolution {res} not listed in capabilities");
        }

        return errors;
    }

    public static string PatchStreamingChannelXml(string currentXml, ValidatedCameraProfile target)
    {
        // Hikvision uses hundredths for maxFrameRate (2500 = 25 fps).
        var fpsHundredths = (int)Math.Round(target.Fps * 100);
        var xml = currentXml;
        xml = ReplaceTag(xml, "videoCodecType", MapCodec(target.Encoding));
        xml = ReplaceTag(xml, "videoResolutionWidth", target.Width.ToString());
        xml = ReplaceTag(xml, "videoResolutionHeight", target.Height.ToString());
        xml = ReplaceTag(xml, "maxFrameRate", fpsHundredths.ToString());
        xml = ReplaceTag(xml, "videoQualityControlType", target.BitrateType);
        xml = ReplaceTag(xml, "constantBitRate", target.BitrateKbps.ToString());
        xml = ReplaceTag(xml, "GovLength", target.GovLength.ToString());
        if (!string.IsNullOrWhiteSpace(target.H264Profile))
            xml = ReplaceTag(xml, "H264Profile", target.H264Profile);

        // Best-effort SmartCodec / Audio / SVC
        xml = ReplaceTag(xml, "SmartCodec", target.SmartCodec ? "true" : "false");
        xml = Regex.Replace(xml, @"(<Audio>\s*<enabled>)(.*?)(</enabled>)",
            m => m.Groups[1].Value + (target.Audio ? "true" : "false") + m.Groups[3].Value,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        xml = Regex.Replace(xml, @"(<SVC>\s*<enabled>)(.*?)(</enabled>)",
            m => m.Groups[1].Value + (target.Svc ? "true" : "false") + m.Groups[3].Value,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return xml;
    }

    private static string MapCodec(string encoding)
    {
        var n = CameraProfileAuditService.NormalizeCodec(encoding);
        return n switch
        {
            "H.264" => "H.264",
            "H.265" => "H.265",
            _ => encoding,
        };
    }

    private static string ReplaceTag(string xml, string tag, string value)
    {
        var pattern = $@"(<{tag}(?:\s[^>]*)?>)(.*?)(</{tag}>)";
        if (!Regex.IsMatch(xml, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            return xml;
        return Regex.Replace(xml, pattern, m => m.Groups[1].Value + value + m.Groups[3].Value,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static string Truncate(string s) =>
        s.Length <= 200 ? s : s[..200] + "…";
}

public static class CameraEntryExtensions
{
    public static bool IsProductionLike(this CameraEntry cam) =>
        cam.SiteId.Contains("zahradky", StringComparison.OrdinalIgnoreCase) ||
        cam.MediaMtxPath.Contains("zahradky", StringComparison.OrdinalIgnoreCase) ||
        cam.CameraId.Contains("zahradky", StringComparison.OrdinalIgnoreCase);
}
