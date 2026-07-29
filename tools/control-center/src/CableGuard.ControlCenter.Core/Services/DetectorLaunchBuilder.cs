using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Builds detector process launch specs without changing algorithm code.
/// Fall uses MediaMTX logical stream via mediamtx_proxy when available; never embeds RTSP credentials in args.
/// </summary>
public static class DetectorLaunchBuilder
{
    public sealed record LaunchSpec(
        string WorkingDirectory,
        string Executable,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> Environment,
        string ProcessHint);

    public static IReadOnlyList<string> ValidateInstance(DetectorInstance d, IEnumerable<string> knownStreams)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Id)) errors.Add("Detector missing id.");
        if (d.DetectorType is not ("fall" or "barrier"))
            errors.Add($"{d.Id}: detector_type must be fall or barrier.");
        if (string.IsNullOrWhiteSpace(d.InputStream)) errors.Add($"{d.Id}: missing input_stream.");
        else if (!knownStreams.Contains(d.InputStream, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{d.Id}: unknown input_stream '{d.InputStream}'.");
        if (string.IsNullOrWhiteSpace(d.ScriptRelative)) errors.Add($"{d.Id}: missing script_relative.");
        return errors;
    }

    public static LaunchSpec Build(
        DetectorInstance instance,
        ControlCenterConfig config,
        NotificationsDocument? notifications = null,
        bool forceDebug = false)
    {
        var python = ResolvePython(config.DetectorRoot);
        var args = new List<string> { instance.ScriptRelative.Replace('/', Path.DirectorySeparatorChar) };
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CABLEGUARD_MODE"] = "production",
        };

        if (instance.DetectorType == "fall")
        {
            args.Add("--mode"); args.Add("production");

            var profile = string.IsNullOrWhiteSpace(instance.InputProfile)
                ? "pyav_rtsp"
                : instance.InputProfile.Trim();
            var sourceMode = string.IsNullOrWhiteSpace(instance.SourceMode)
                ? "mediamtx"
                : instance.SourceMode.Trim();
            if (profile is not ("pyav_rtsp" or "mediamtx_proxy" or "direct_camera" or "pyav_direct_office63"))
                profile = "pyav_rtsp";
            if (sourceMode is not ("mediamtx" or "direct_camera"))
                sourceMode = "mediamtx";

            args.Add("--input-profile"); args.Add(profile);
            env["CABLEGUARD_FALL_INPUT_PROFILE"] = profile;
            env["CABLEGUARD_FALL_SOURCE_MODE"] = sourceMode;

            // Localhost MediaMTX RTSP — never camera credentials in args/env here.
            if (profile is "mediamtx_proxy" || (profile == "pyav_rtsp" && sourceMode == "mediamtx"))
            {
                env["CABLEGUARD_MEDIAMTX_RTSP_URL"] =
                    $"rtsp://127.0.0.1:8554/{instance.InputStream}";
            }

            // Optional per-instance YAML (gitignored) — never embeds camera IP.
            var officeInput = Path.Combine(config.DetectorRoot, "runtime", "config", $"{instance.Id}.yaml");
            if (File.Exists(officeInput))
            {
                args.Add("--input-config");
                args.Add(officeInput);
            }
            else if (profile == "pyav_rtsp" && sourceMode == "mediamtx")
            {
                var pyavYaml = Path.Combine(
                    config.DetectorRoot, "sites", "zahradky", "horni_stanice", "fall_pyav_mediamtx.yaml");
                if (File.Exists(pyavYaml))
                {
                    args.Add("--input-config");
                    args.Add(pyavYaml);
                }
            }
            else if (profile == "pyav_rtsp" && sourceMode == "direct_camera")
            {
                var pyavDirect = Path.Combine(
                    config.DetectorRoot, "sites", "zahradky", "horni_stanice", "fall_pyav_direct.yaml");
                if (File.Exists(pyavDirect))
                {
                    args.Add("--input-config");
                    args.Add(pyavDirect);
                }
            }

            var prepared = DetectorRuntimeConfigAdapter.TryPrepareFallSiteConfig(instance, config, out _);
            if (prepared is not null)
            {
                env["CABLEGUARD_PAD_SITE_CONFIG"] = prepared.SiteConfigPath;
                env["CABLEGUARD_ROI_PROFILE"] = prepared.RoiProfileId;
                env["CABLEGUARD_ROI_SHA256"] = prepared.RoiSha256;
            }

            var isOfficeTest = string.Equals(instance.Id, "fall-office-test", StringComparison.OrdinalIgnoreCase);
            var isProdFall = string.Equals(instance.Id, "fall-zahradky-upper", StringComparison.OrdinalIgnoreCase);

            // Office Test Lab: heartbeat always; alarms only when PublishEventCore; never Telegram/relay.
            if (isOfficeTest)
            {
                env["CABLEGUARD_RUNTIME_STATUS_ENABLED"] = "true";
                env["CABLEGUARD_TEST_MODE"] = "true";
                env["CABLEGUARD_EVENT_CORE_HEARTBEAT_ONLY"] =
                    instance.PublishEventCore ? "false" : "true";
            }

            // Enable heartbeats for production fall so Control Center can show video_input health.
            if (isProdFall)
                env["CABLEGUARD_RUNTIME_STATUS_ENABLED"] = "true";

            var debug = forceDebug || instance.DebugOverlay;
            if (debug)
                args.Add("--debug-overlay");
            else
                args.Add("--no-window");

            var telegramOn = instance.PublishTelegram && (notifications?.TelegramEnabled ?? false);
            env["TELEGRAM_ENABLED"] = telegramOn ? "true" : "false";

            // Always inject Event Core URL for fall — RuntimeStatus / events need it.
            // Ingest key from host env / platform .env (never log). Missing key → events OFF (no ConfigError).
            var wantEvents = instance.PublishEventCore;
            var url = PlatformEnvSecrets.TryGet(PlatformEnvSecrets.EventCoreUrl, config.PlatformRoot)
                      ?? config.EventCoreBaseLocal;
            if (!string.IsNullOrWhiteSpace(url))
                env[PlatformEnvSecrets.EventCoreUrl] = url.TrimEnd('/');

            var ingestKey = PlatformEnvSecrets.TryGet(PlatformEnvSecrets.IngestApiKey, config.PlatformRoot);
            if (!string.IsNullOrWhiteSpace(ingestKey))
                env[PlatformEnvSecrets.IngestApiKey] = ingestKey;

            if (wantEvents && string.IsNullOrWhiteSpace(ingestKey))
            {
                wantEvents = false;
                if (isOfficeTest)
                    env["CABLEGUARD_EVENT_CORE_HEARTBEAT_ONLY"] = "true";
            }

            env["CABLEGUARD_EVENT_CORE_EVENTS"] = wantEvents ? "true" : "false";
            // Token/chat stay in Credential Manager / process env set by host — never in LaunchSpec args.
        }
        else if (instance.DetectorType == "barrier")
        {
            args.Add("--mode"); args.Add("production");
            // Barrier source must be MediaMTX proxy URL without credentials in args.
            env["CABLEGUARD_MEDIAMTX_RTSP_URL"] =
                $"rtsp://127.0.0.1:8554/{instance.InputStream}";
        }

        foreach (var extra in instance.ExtraArgs)
            args.Add(extra);

        var hint = string.IsNullOrWhiteSpace(instance.ProcessHint)
            ? Path.GetFileNameWithoutExtension(instance.ScriptRelative)
            : instance.ProcessHint;

        return new LaunchSpec(config.DetectorRoot, python, args, env, hint);
    }

    public static string FormatCommandLine(LaunchSpec spec) =>
        $"{spec.Executable} {string.Join(" ", spec.Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}";

    private static string ResolvePython(string detectorRoot)
    {
        var venv = Path.Combine(detectorRoot, ".venv", "Scripts", "python.exe");
        if (File.Exists(venv)) return venv;
        return "python";
    }

    public static DetectorsDocument Load(string path)
    {
        if (!File.Exists(path)) return new DetectorsDocument();
        return JsonSerializer.Deserialize<DetectorsDocument>(File.ReadAllText(path)) ?? new DetectorsDocument();
    }

    public static void Save(DetectorsDocument doc, string path, IEnumerable<string> knownStreams)
    {
        var errors = doc.Instances.SelectMany(i => ValidateInstance(i, knownStreams)).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, opts));
        File.Move(tmp, path, overwrite: true);
    }
}
