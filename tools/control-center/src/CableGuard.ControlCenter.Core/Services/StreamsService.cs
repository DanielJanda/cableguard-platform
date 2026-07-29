using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public static class StreamsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static IReadOnlyList<string> Validate(StreamsDocument doc, IEnumerable<string> knownCameraIds)
    {
        var errors = new List<string>();
        var cams = new HashSet<string>(knownCameraIds, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in doc.Streams)
        {
            if (string.IsNullOrWhiteSpace(s.StreamId)) errors.Add("Stream with missing stream_id.");
            else if (!seen.Add(s.StreamId)) errors.Add($"Duplicate stream_id: {s.StreamId}");
            if (string.IsNullOrWhiteSpace(s.MediaMtxPath)) errors.Add($"{s.StreamId}: missing mediamtx_path.");
            if (string.IsNullOrWhiteSpace(s.CameraId)) errors.Add($"{s.StreamId}: missing camera_id.");
            else if (!cams.Contains(s.CameraId)) errors.Add($"{s.StreamId}: unknown camera '{s.CameraId}'.");
        }
        if (doc.Streams.Count(x => x.IsProduction) > 1)
            errors.Add("At most one stream may be marked is_production.");
        return errors;
    }

    public static StreamsDocument Load(string path)
    {
        if (!File.Exists(path)) return new StreamsDocument();
        return JsonSerializer.Deserialize<StreamsDocument>(File.ReadAllText(path)) ?? new StreamsDocument();
    }

    public static void Save(StreamsDocument doc, string path, IEnumerable<string> knownCameraIds)
    {
        var errors = Validate(doc, knownCameraIds);
        if (errors.Count > 0)
            throw new InvalidOperationException("streams.json invalid: " + string.Join("; ", errors));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Migrate legacy cameras.json stream_mappings into streams.json if streams file is empty.</summary>
    public static StreamsDocument MigrateFromLegacy(CameraRegistryDocument cameras, StreamsDocument existing)
    {
        if (existing.Streams.Count > 0)
        {
            EnrichMissingProfileFields(cameras, existing);
            return existing;
        }
        var doc = new StreamsDocument { Version = 2 };
        foreach (var m in cameras.StreamMappings)
        {
            var cam = cameras.Cameras.FirstOrDefault(c => c.CameraId == m.PrimaryCameraId);
            if (cam is not null) CameraProfileAuditService.EnsureMigrated(cam);
            var profile = cam?.StreamProfiles.FirstOrDefault(p => p.ProfileId == cam.SelectedProfileId)
                          ?? cam?.StreamProfiles.FirstOrDefault();
            doc.Streams.Add(new LogicalStream
            {
                StreamId = m.LogicalStream,
                DisplayName = m.LogicalStream,
                MediaMtxPath = m.LogicalStream,
                CameraId = m.PrimaryCameraId,
                ProfileId = profile?.ProfileId ?? "",
                StreamType = profile?.StreamType ?? "",
                ChannelId = profile?.ChannelId ?? CameraProfileAuditService.ParseChannel(cam?.Profile),
                ProfileFingerprint = profile?.ConfigurationFingerprint ?? "",
                Enabled = true,
                IsProduction = m.LogicalStream == "zahradky-horni-stanice",
            });
        }
        return doc;
    }

    /// <summary>Backfill profile_id/channel/stream_type on v1 streams without rewriting camera selection.</summary>
    public static void EnrichMissingProfileFields(CameraRegistryDocument cameras, StreamsDocument doc)
    {
        if (doc.Version < 2) doc.Version = 2;
        foreach (var s in doc.Streams)
        {
            if (!string.IsNullOrWhiteSpace(s.ProfileId) && s.ChannelId is not null) continue;
            var cam = cameras.Cameras.FirstOrDefault(c => c.CameraId == s.CameraId);
            if (cam is null) continue;
            CameraProfileAuditService.EnsureMigrated(cam);
            var profile = cam.StreamProfiles.FirstOrDefault(p => p.ProfileId == cam.SelectedProfileId)
                          ?? cam.StreamProfiles.FirstOrDefault();
            if (profile is null) continue;
            if (string.IsNullOrWhiteSpace(s.ProfileId)) s.ProfileId = profile.ProfileId;
            if (string.IsNullOrWhiteSpace(s.StreamType)) s.StreamType = profile.StreamType;
            s.ChannelId ??= profile.ChannelId;
            if (string.IsNullOrWhiteSpace(s.ProfileFingerprint))
                s.ProfileFingerprint = profile.ConfigurationFingerprint;
        }
    }
}
