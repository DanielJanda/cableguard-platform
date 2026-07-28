using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public static class RoiProfileService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static IReadOnlyList<string> Validate(RoiProfile profile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Id)) errors.Add("ROI profile missing id.");
        if (profile.Points.Count is < 3 or > 32)
            errors.Add($"ROI '{profile.Id}': need 3–32 points, got {profile.Points.Count}.");
        foreach (var p in profile.Points)
        {
            if (p.X < 0 || p.Y < 0) errors.Add($"ROI '{profile.Id}': negative coordinates not allowed.");
        }
        if (profile.SourceWidth < 0 || profile.SourceHeight < 0)
            errors.Add($"ROI '{profile.Id}': source_width/height must be >= 0.");
        if (!string.IsNullOrWhiteSpace(profile.ActivationState) &&
            !string.Equals(profile.ActivationState, "saved", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"ROI '{profile.Id}': activation_state must be 'saved' (ACTIVE is detector runtime only).");
        }
        return errors;
    }

    /// <summary>
    /// Returns RESOLUTION/STREAM MISMATCH when ROI fingerprint or capture resolution no longer matches stream.
    /// Legacy ROI without fingerprint is treated as unknown (not auto-invalid).
    /// </summary>
    public static string? MismatchStatus(RoiProfile roi, string? currentFingerprint, int? currentWidth, int? currentHeight)
    {
        if (!string.IsNullOrWhiteSpace(roi.StreamProfileFingerprint))
        {
            if (string.IsNullOrWhiteSpace(currentFingerprint) ||
                !string.Equals(roi.StreamProfileFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase))
                return "RESOLUTION/STREAM MISMATCH";
        }

        if (roi.SourceWidth > 0 && roi.SourceHeight > 0 &&
            currentWidth is int w && currentHeight is int h &&
            (roi.SourceWidth != w || roi.SourceHeight != h))
            return "RESOLUTION/STREAM MISMATCH";

        return null;
    }

    public static RoiProfile Load(string filePath)
    {
        var profile = JsonSerializer.Deserialize<RoiProfile>(File.ReadAllText(filePath))
                      ?? throw new InvalidOperationException("Empty ROI profile.");
        if (string.IsNullOrWhiteSpace(profile.ActivationState))
            profile.ActivationState = "saved";
        var errors = Validate(profile);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        return profile;
    }

    public static void Save(RoiProfile profile, string filePath)
    {
        profile.ActivationState = "saved";
        var errors = Validate(profile);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, JsonOpts));
        File.Move(tmp, filePath, overwrite: true);
    }

    public static IReadOnlyList<RoiProfile> ListAll(string roiDir)
    {
        if (!Directory.Exists(roiDir)) return Array.Empty<RoiProfile>();
        return Directory.GetFiles(roiDir, "*.json")
            .Select(f => { try { return Load(f); } catch { return null; } })
            .Where(p => p is not null)
            .Cast<RoiProfile>()
            .ToList();
    }

    public static string ToYamlPointsLiteral(RoiProfile profile) =>
        "[" + string.Join(", ", profile.Points.Select(p => $"[{p.X}, {p.Y}]")) + "]";
}
