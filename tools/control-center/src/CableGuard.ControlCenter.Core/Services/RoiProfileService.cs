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
        return errors;
    }

    public static RoiProfile Load(string filePath)
    {
        var profile = JsonSerializer.Deserialize<RoiProfile>(File.ReadAllText(filePath))
                      ?? throw new InvalidOperationException("Empty ROI profile.");
        var errors = Validate(profile);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        return profile;
    }

    public static void Save(RoiProfile profile, string filePath)
    {
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
