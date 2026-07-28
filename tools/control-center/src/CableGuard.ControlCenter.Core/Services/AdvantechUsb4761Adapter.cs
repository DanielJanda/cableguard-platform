using System.Diagnostics;
using System.Management;
using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Advantech USB-4761 discovery + guarded writes via a Python CLI bridge.
/// Never reports CONNECTED without a real PnP match. No detector→relay automation.
/// </summary>
public sealed class AdvantechUsb4761Adapter : IHardwareAdapter
{
    public const string VendorProductHint = "VID_1809&PID_4761";
    public const int RelayChannels = 8;
    public static readonly TimeSpan MaxPulse = TimeSpan.FromMilliseconds(500);

    private readonly ControlCenterLogger _logger;
    private readonly string _cliPath;
    private readonly string? _python;
    private readonly HardwareDocument _cfg;
    private HardwareDiscovery _discovery = HardwareDiscovery.NotFound();
    private string _lastOperation = "—";
    private string _lastError = "";

    private AdvantechUsb4761Adapter(
        ControlCenterLogger logger,
        string cliPath,
        string? python,
        HardwareDocument cfg)
    {
        _logger = logger;
        _cliPath = cliPath;
        _python = python;
        _cfg = cfg;
        RefreshDiscovery();
    }

    public static IHardwareAdapter Create(ControlCenterConfig config, ControlCenterLogger logger)
    {
        var cfgPath = RuntimeConfigPaths.Hardware(config);
        var cfg = File.Exists(cfgPath) ? HardwareConfigService.Load(cfgPath) : new HardwareDocument();
        var cli = Path.Combine(config.PlatformRoot, "tools", "control-center", "scripts", "usb4761_guarded_cli.py");
        var python = FindPython();
        return new AdvantechUsb4761Adapter(logger, cli, python, cfg);
    }

    public string StatusDetail
    {
        get
        {
            var map = MappingConfigured
                ? $"mapping green={_cfg.GreenChannel} red={_cfg.RedChannel} buzzer={_cfg.BuzzerChannel}"
                : "semantic mapping NOT CONFIGURED (Green/Red/Buzzer disabled)";
            return $"{_discovery.Status} | {_discovery.Model} | driver={_discovery.DriverStatus} | " +
                   $"relays={_discovery.RelayCount} DI={_discovery.DiCount} | {map} | last={_lastOperation}" +
                   (string.IsNullOrWhiteSpace(_lastError) ? "" : $" | err={_lastError}");
        }
    }

    public bool IsAvailable => _discovery.Status is "CONNECTED";
    public bool IsTestMode { get; set; }
    public bool MappingConfigured =>
        _cfg.GreenChannel is > 0 && _cfg.RedChannel is > 0 && _cfg.BuzzerChannel is > 0;
    public HardwareDiscovery Discovery => _discovery;
    public string LastOperation => _lastOperation;
    public string LastError => _lastError;

    public void RefreshDiscovery()
    {
        _discovery = Discover();
        _logger.Info($"[HW] Discovery: {_discovery.Status} model={_discovery.Model} driver={_discovery.DriverStatus}");
    }

    public Task<bool> EnsureConnectedAsync(CancellationToken ct = default)
    {
        RefreshDiscovery();
        return Task.FromResult(IsAvailable);
    }

    public Task<IReadOnlyDictionary<string, bool>> ReadDigitalInputsAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
            return Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());

        var result = RunCli("read-di", Array.Empty<string>(), ct);
        if (!result.Ok || result.Json is null)
            return Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());

        var map = new Dictionary<string, bool>();
        if (result.Json.RootElement.TryGetProperty("di", out var di) && di.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var el in di.EnumerateArray())
            {
                map[$"DI{i}"] = el.GetBoolean();
                i++;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<string, bool>>(map);
    }

    public async Task PulseRelayAsync(int channel, TimeSpan duration, CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        if (channel is < 1 or > RelayChannels)
            throw new InvalidOperationException($"Channel {channel} out of range 1..{RelayChannels}.");
        var clamped = HardwareSafety.ClampPulse(duration, MaxPulse);
        await AllOffAsync(ct).ConfigureAwait(false);
        var ms = (int)clamped.TotalMilliseconds;
        var result = RunCli("pulse", new[] { "--channel", channel.ToString(), "--ms", ms.ToString() }, ct);
        _lastOperation = $"pulse ch={channel} {ms}ms ok={result.Ok}";
        if (!result.Ok)
        {
            _lastError = result.Message;
            throw new InvalidOperationException(result.Message);
        }
        _lastError = "";
        _logger.Info($"[HW] {_lastOperation}");
    }

    public async Task SetSemaphoreAsync(string color, bool on, CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        if (!MappingConfigured)
            throw new InvalidOperationException("Semantic mapping NOT CONFIGURED — Green/Red/Buzzer disabled.");

        var ch = color.ToLowerInvariant() switch
        {
            "green" => _cfg.GreenChannel!.Value,
            "red" => _cfg.RedChannel!.Value,
            "buzzer" => _cfg.BuzzerChannel!.Value,
            _ => throw new InvalidOperationException($"Unknown semaphore color '{color}'."),
        };

        if (on)
            await PulseRelayAsync(ch, MaxPulse, ct).ConfigureAwait(false);
        else
            await AllOffAsync(ct).ConfigureAwait(false);
    }

    public Task AllOffAsync(CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        var result = RunCli("all-off", Array.Empty<string>(), ct);
        _lastOperation = $"all-off ok={result.Ok}";
        if (!result.Ok)
        {
            _lastError = result.Message;
            throw new InvalidOperationException(result.Message);
        }
        _lastError = "";
        _logger.Info($"[HW] {_lastOperation}");
        return Task.CompletedTask;
    }

    private CliResult RunCli(string command, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (_python is null || !File.Exists(_cliPath))
            return CliResult.Fail("USB-4761 CLI / Python NOT AVAILABLE.");

        var psi = new ProcessStartInfo
        {
            FileName = _python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(_cliPath);
        psi.ArgumentList.Add(command);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi);
        if (proc is null) return CliResult.Fail("Failed to start USB-4761 CLI.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(15_000);
        if (proc.ExitCode != 0)
            return CliResult.Fail(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

        try
        {
            var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stdout) ? "{}" : stdout);
            return CliResult.Success(doc);
        }
        catch
        {
            return CliResult.Fail("CLI returned non-JSON output.");
        }
    }

    private HardwareDiscovery Discover()
    {
        var pnp = FindPnpDevice();
        var daq = FindDaqNavi();
        if (pnp is null)
            return HardwareDiscovery.NotFound();

        var (pnpName, pnpId) = pnp.Value;

        if (daq is null)
        {
            return new HardwareDiscovery(
                Status: "DRIVER MISSING",
                Model: pnpName,
                SerialMasked: Mask(pnpId),
                DriverStatus: "DAQNavi not found",
                RelayCount: RelayChannels,
                DiCount: _cfg.DiCount,
                InstanceId: pnpId);
        }

        if (_python is null || !File.Exists(_cliPath))
        {
            return new HardwareDiscovery(
                Status: "FAULT",
                Model: pnpName,
                SerialMasked: Mask(pnpId),
                DriverStatus: "CLI/Python missing",
                RelayCount: RelayChannels,
                DiCount: _cfg.DiCount,
                InstanceId: pnpId);
        }

        // Device present + DAQNavi present → CONNECTED (write still requires TEST MODE).
        return new HardwareDiscovery(
            Status: "CONNECTED",
            Model: string.IsNullOrWhiteSpace(pnpName) ? "Advantech USB-4761" : pnpName,
            SerialMasked: Mask(pnpId),
            DriverStatus: "OK (" + daq + ")",
            RelayCount: RelayChannels,
            DiCount: _cfg.DiCount,
            InstanceId: pnpId);
    }

    private static (string Name, string DeviceId)? FindPnpDevice()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_1809&PID_4761%'");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Advantech USB-4761";
                var id = obj["DeviceID"]?.ToString() ?? "";
                return (name, id);
            }
        }
        catch
        {
            // Fallback: check Get-PnpDevice via registry-less empty.
        }
        return null;
    }

    private static string? FindDaqNavi()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("ADVANTECH_DAQNAVI_PATH"),
            @"C:\Program Files\Advantech\DAQNavi",
            @"C:\Program Files (x86)\Advantech\DAQNavi",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"Documents\cableguard-detector\DAQNavi"),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p!));
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python", "python3", "py" })
        {
            var path = FindOnPath(name + ".exe") ?? FindOnPath(name);
            if (path is not null) return path;
        }
        return null;
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), name);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string Mask(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "—";
        var parts = deviceId.Split('\\');
        var last = parts.Length > 0 ? parts[^1] : deviceId;
        if (last.Length <= 6) return "***";
        return last[..3] + "…" + last[^2];
    }

    private sealed record CliResult(bool Ok, string Message, JsonDocument? Json)
    {
        public static CliResult Success(JsonDocument doc) => new(true, "OK", doc);
        public static CliResult Fail(string msg) => new(false, msg.Trim(), null);
    }
}

public sealed record HardwareDiscovery(
    string Status,
    string Model,
    string SerialMasked,
    string DriverStatus,
    int RelayCount,
    int DiCount,
    string InstanceId)
{
    public static HardwareDiscovery NotFound() => new(
        "NOT FOUND", "—", "—", "—", 0, 0, "");
}
