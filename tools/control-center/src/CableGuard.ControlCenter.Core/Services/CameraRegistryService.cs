using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Loads, validates and saves the local camera registry (runtime/config/cameras.json, gitignored).
/// Rejects any document that carries credential material inline.
/// </summary>
public sealed class CameraRegistryService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>JSON keys that must never appear in the registry document.</summary>
    private static readonly string[] ForbiddenKeys = { "password", "passwd", "pwd", "secret", "rtsp_url" };

    public static IReadOnlyList<string> Validate(string json)
    {
        var errors = new List<string>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new[] { $"Invalid JSON: {ex.Message}" };
        }

        using (doc)
        {
            CheckForbiddenKeys(doc.RootElement, "", errors);
        }

        CameraRegistryDocument? registry;
        try
        {
            registry = JsonSerializer.Deserialize<CameraRegistryDocument>(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"Schema error: {ex.Message}");
            return errors;
        }
        if (registry is null) { errors.Add("Empty document."); return errors; }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cam in registry.Cameras)
        {
            var label = string.IsNullOrWhiteSpace(cam.CameraId) ? "<missing camera_id>" : cam.CameraId;
            if (string.IsNullOrWhiteSpace(cam.CameraId)) errors.Add("Camera with missing camera_id.");
            else if (!seenIds.Add(cam.CameraId)) errors.Add($"Duplicate camera_id: {cam.CameraId}");
            if (string.IsNullOrWhiteSpace(cam.DisplayName)) errors.Add($"{label}: missing display_name.");
            if (string.IsNullOrWhiteSpace(cam.SiteId)) errors.Add($"{label}: missing site_id.");
            if (string.IsNullOrWhiteSpace(cam.StationId)) errors.Add($"{label}: missing station_id.");
            if (string.IsNullOrWhiteSpace(cam.Host)) errors.Add($"{label}: missing host.");
            if (cam.RtspPort is < 1 or > 65535) errors.Add($"{label}: invalid rtsp_port {cam.RtspPort}.");
            if (string.IsNullOrWhiteSpace(cam.CredentialRef)) errors.Add($"{label}: missing credential_ref.");
            if (string.IsNullOrWhiteSpace(cam.MediaMtxPath)) errors.Add($"{label}: missing mediamtx_path.");
        }

        foreach (var mapping in registry.StreamMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.LogicalStream)) errors.Add("Stream mapping with missing logical_stream.");
            if (!seenIds.Contains(mapping.PrimaryCameraId))
                errors.Add($"Stream mapping '{mapping.LogicalStream}' references unknown camera '{mapping.PrimaryCameraId}'.");
        }

        return errors;
    }

    private static void CheckForbiddenKeys(JsonElement element, string path, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (ForbiddenKeys.Any(k => prop.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"Forbidden key '{prop.Name}' at {path}/ — credentials belong in Windows Credential Manager, never in cameras.json.");
                    CheckForbiddenKeys(prop.Value, $"{path}/{prop.Name}", errors);
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    CheckForbiddenKeys(item, $"{path}[{i++}]", errors);
                break;
        }
    }

    public static CameraRegistryDocument Load(string filePath)
    {
        if (!File.Exists(filePath)) return new CameraRegistryDocument();
        var json = File.ReadAllText(filePath);
        var errors = Validate(json);
        if (errors.Count > 0)
            throw new InvalidOperationException("cameras.json invalid: " + string.Join("; ", errors));
        return JsonSerializer.Deserialize<CameraRegistryDocument>(json)!;
    }

    public static void Save(CameraRegistryDocument registry, string filePath)
    {
        var json = JsonSerializer.Serialize(registry, JsonOpts);
        var errors = Validate(json);
        if (errors.Count > 0)
            throw new InvalidOperationException("Refusing to save invalid registry: " + string.Join("; ", errors));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, json);
    }

    public static CameraEntry? ResolvePrimaryCamera(CameraRegistryDocument registry, string logicalStream)
    {
        var mapping = registry.StreamMappings.FirstOrDefault(m => m.LogicalStream == logicalStream);
        if (mapping is null) return null;
        return registry.Cameras.FirstOrDefault(c => c.CameraId == mapping.PrimaryCameraId);
    }
}
