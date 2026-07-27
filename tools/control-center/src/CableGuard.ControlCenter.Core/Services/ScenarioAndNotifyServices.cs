using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

public static class ScenarioService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static IReadOnlyList<string> Validate(
        ScenarioDocument scenario,
        IEnumerable<string> knownStreams,
        IEnumerable<string> knownDetectors)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(scenario.Id)) errors.Add("Scenario missing id.");
        if (string.IsNullOrWhiteSpace(scenario.DisplayName)) errors.Add($"{scenario.Id}: missing display_name.");
        if (!string.IsNullOrWhiteSpace(scenario.StreamId) &&
            !knownStreams.Contains(scenario.StreamId, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{scenario.Id}: unknown stream '{scenario.StreamId}'.");
        foreach (var det in scenario.DetectorIds)
        {
            if (!knownDetectors.Contains(det, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{scenario.Id}: unknown detector '{det}'.");
        }
        return errors;
    }

    /// <summary>Human-readable diff of scenario intent vs current runtime flags.</summary>
    public static IReadOnlyList<string> Diff(
        ScenarioDocument scenario,
        bool currentTelegram,
        bool currentEventCore,
        bool currentHardwareTest,
        IEnumerable<string> runningDetectorIds)
    {
        var lines = new List<string>();
        var running = new HashSet<string>(runningDetectorIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in scenario.DetectorIds)
            lines.Add(running.Contains(id) ? $"detector {id}: already running" : $"detector {id}: will START");
        foreach (var id in running.Where(r => !scenario.DetectorIds.Contains(r, StringComparer.OrdinalIgnoreCase)))
            lines.Add($"detector {id}: will STOP (not in scenario)");
        if (scenario.Telegram != currentTelegram)
            lines.Add($"telegram: {currentTelegram} → {scenario.Telegram}");
        if (scenario.EventCore != currentEventCore)
            lines.Add($"event_core: {currentEventCore} → {scenario.EventCore}");
        if (scenario.HardwareTest != currentHardwareTest)
            lines.Add($"hardware_test: {currentHardwareTest} → {scenario.HardwareTest}");
        if (scenario.DebugOverlay) lines.Add("debug_overlay: ON");
        if (!string.IsNullOrWhiteSpace(scenario.StreamId))
            lines.Add($"stream: {scenario.StreamId}");
        if (!string.IsNullOrWhiteSpace(scenario.RoiProfile))
            lines.Add($"roi: {scenario.RoiProfile}");
        if (lines.Count == 0) lines.Add("(no changes)");
        return lines;
    }

    public static ScenariosDocument Load(string path)
    {
        if (!File.Exists(path)) return new ScenariosDocument();
        return JsonSerializer.Deserialize<ScenariosDocument>(File.ReadAllText(path)) ?? new ScenariosDocument();
    }

    public static void Save(ScenariosDocument doc, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }
}

public static class NotificationsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static NotificationsDocument Load(string path)
    {
        if (!File.Exists(path)) return new NotificationsDocument();
        return JsonSerializer.Deserialize<NotificationsDocument>(File.ReadAllText(path))
               ?? new NotificationsDocument();
    }

    public static void Save(NotificationsDocument doc, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(doc, JsonOpts);
        if (json.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            json.Contains("token", StringComparison.OrdinalIgnoreCase) &&
            json.Contains(": \"", StringComparison.Ordinal))
        {
            // Soft guard: credential_ref is fine; literal tokens are not.
            using var parsed = JsonDocument.Parse(json);
            foreach (var prop in parsed.RootElement.EnumerateObject())
            {
                if (prop.Name.Contains("token", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.String &&
                    !prop.Name.Contains("credential", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    throw new InvalidOperationException("Refusing to save Telegram token into notifications.json.");
            }
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}

public static class HardwareConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static HardwareDocument Load(string path)
    {
        if (!File.Exists(path)) return new HardwareDocument();
        return JsonSerializer.Deserialize<HardwareDocument>(File.ReadAllText(path)) ?? new HardwareDocument();
    }

    public static void Save(HardwareDocument doc, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }
}
