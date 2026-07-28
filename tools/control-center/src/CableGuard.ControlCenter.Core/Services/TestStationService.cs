using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public static class TestStationService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static IReadOnlyList<string> Validate(TestStationProfile s)
    {
        var errors = new List<string>();
        var label = string.IsNullOrWhiteSpace(s.StationId) ? "<station>" : s.StationId;
        if (string.IsNullOrWhiteSpace(s.StationId)) errors.Add("station_id missing.");
        if (string.IsNullOrWhiteSpace(s.SiteId)) errors.Add($"{label}: site_id missing.");
        if (string.IsNullOrWhiteSpace(s.DisplayName)) errors.Add($"{label}: display_name missing.");
        if (string.IsNullOrWhiteSpace(s.CameraId)) errors.Add($"{label}: camera_id missing.");
        if (string.IsNullOrWhiteSpace(s.VideoStream)) errors.Add($"{label}: video_stream missing.");
        if (string.IsNullOrWhiteSpace(s.FallServiceId)) errors.Add($"{label}: fall_service_id missing.");
        if (s.Mode is not ("test" or "production"))
            errors.Add($"{label}: mode must be test or production.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateDocument(TestStationsDocument doc)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in doc.Stations)
        {
            errors.AddRange(Validate(s));
            if (!string.IsNullOrWhiteSpace(s.StationId) && !seen.Add(s.StationId))
                errors.Add($"Duplicate station_id: {s.StationId}");
        }
        return errors;
    }

    public static TestStationsDocument Load(string path)
    {
        if (!File.Exists(path)) return OfficeDefault();
        var doc = JsonSerializer.Deserialize<TestStationsDocument>(File.ReadAllText(path))
                  ?? OfficeDefault();
        var errors = ValidateDocument(doc);
        if (errors.Count > 0)
            throw new InvalidOperationException("test-stations.json invalid: " + string.Join("; ", errors));
        return doc;
    }

    public static void Save(TestStationsDocument doc, string path)
    {
        var errors = ValidateDocument(doc);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    public static TestStationsDocument OfficeDefault() => new()
    {
        Version = 1,
        Stations =
        {
            new TestStationProfile
            {
                StationId = "office-test",
                SiteId = "office",
                DisplayName = "Kancelář – test pádu",
                CameraId = "camera-122727",
                VideoStream = "office-test-camera",
                FallServiceId = "fall-office-test",
                RoiProfile = "office-fall-test",
                MonitorPath = "/test-lab/office-fall",
                Mode = "test",
            },
        },
    };

    public static TestStationProfile? Find(TestStationsDocument doc, string stationId) =>
        doc.Stations.FirstOrDefault(s =>
            string.Equals(s.StationId, stationId, StringComparison.OrdinalIgnoreCase));
}
