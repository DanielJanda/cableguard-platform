using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>Validates and persists physical camera registry (no secrets).</summary>
public sealed partial class CameraRegistryService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly string[] ForbiddenKeys = { "password", "passwd", "pwd", "secret", "rtsp_url", "token" };

    public static IReadOnlyList<string> Validate(string json)
    {
        var errors = new List<string>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return new[] { $"Invalid JSON: {ex.Message}" }; }

        using (doc) CheckForbiddenKeys(doc.RootElement, "", errors);

        CameraRegistryDocument? registry;
        try { registry = JsonSerializer.Deserialize<CameraRegistryDocument>(json); }
        catch (JsonException ex) { errors.Add($"Schema error: {ex.Message}"); return errors; }
        if (registry is null) { errors.Add("Empty document."); return errors; }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cam in registry.Cameras)
            errors.AddRange(ValidateCamera(cam, seenIds));

        foreach (var mapping in registry.StreamMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.LogicalStream))
                errors.Add("Stream mapping with missing logical_stream.");
            if (!string.IsNullOrWhiteSpace(mapping.PrimaryCameraId) && !seenIds.Contains(mapping.PrimaryCameraId))
                errors.Add($"Stream mapping '{mapping.LogicalStream}' references unknown camera '{mapping.PrimaryCameraId}'.");
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateCamera(CameraEntry cam, HashSet<string>? seenIds = null)
    {
        var errors = new List<string>();
        var label = string.IsNullOrWhiteSpace(cam.CameraId) ? "<missing camera_id>" : cam.CameraId;
        if (string.IsNullOrWhiteSpace(cam.CameraId)) errors.Add("Camera with missing camera_id.");
        else if (seenIds is not null && !seenIds.Add(cam.CameraId)) errors.Add($"Duplicate camera_id: {cam.CameraId}");
        if (string.IsNullOrWhiteSpace(cam.DisplayName)) errors.Add($"{label}: missing display_name.");
        if (string.IsNullOrWhiteSpace(cam.Host)) errors.Add($"{label}: missing host.");
        else if (!IsValidHost(cam.Host)) errors.Add($"{label}: invalid host/IP '{cam.Host}'.");
        if (cam.RtspPort is < 1 or > 65535) errors.Add($"{label}: invalid rtsp_port {cam.RtspPort}.");
        if (string.IsNullOrWhiteSpace(cam.CredentialRef)) errors.Add($"{label}: missing credential_ref.");
        if (!string.IsNullOrWhiteSpace(cam.Transport) &&
            cam.Transport is not ("tcp" or "udp" or "TCP" or "UDP"))
            errors.Add($"{label}: transport must be tcp or udp.");
        return errors;
    }

    public static bool IsValidHost(string host)
    {
        if (LooksLikeIpv4(host))
        {
            var parts = host.Split('.');
            return parts.Length == 4 && parts.All(p => int.TryParse(p, out var n) && n is >= 0 and <= 255);
        }
        if (IPAddress.TryParse(host, out _)) return true; // IPv6
        return HostnameRegex().IsMatch(host);
    }

    private static bool LooksLikeIpv4(string host)
    {
        var parts = host.Split('.');
        if (parts.Length != 4) return false;
        return parts.All(p => p.Length > 0 && p.All(char.IsDigit));
    }

    private static void CheckForbiddenKeys(JsonElement element, string path, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (ForbiddenKeys.Any(k => prop.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"Forbidden key '{prop.Name}' at {path}/ — credentials belong in Windows Credential Manager.");
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
        var registry = JsonSerializer.Deserialize<CameraRegistryDocument>(json)!;
        foreach (var cam in registry.Cameras)
            CameraProfileAuditService.EnsureMigrated(cam);
        if (registry.Version < 2)
            registry.Version = 2;
        return registry;
    }

    public static void Save(CameraRegistryDocument registry, string filePath)
    {
        var json = JsonSerializer.Serialize(registry, JsonOpts);
        var errors = Validate(json);
        if (errors.Count > 0)
            throw new InvalidOperationException("Refusing to save invalid registry: " + string.Join("; ", errors));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, filePath, overwrite: true);
    }

    public static CameraEntry? ResolvePrimaryCamera(CameraRegistryDocument registry, string logicalStream)
    {
        var mapping = registry.StreamMappings.FirstOrDefault(m => m.LogicalStream == logicalStream);
        if (mapping is null) return null;
        return registry.Cameras.FirstOrDefault(c => c.CameraId == mapping.PrimaryCameraId);
    }

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)*$")]
    private static partial Regex HostnameRegex();
}
