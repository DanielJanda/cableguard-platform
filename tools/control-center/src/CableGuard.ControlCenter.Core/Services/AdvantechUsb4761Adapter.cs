using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Advantech USB-4761 discovery + guarded writes via a Python CLI bridge.
/// CONNECTED only after real SDK load + device open + DI/DO read.
/// No detector→relay automation.
/// </summary>
public sealed class AdvantechUsb4761Adapter : IHardwareAdapter
{
    public const string VendorProductHint = "VID_1809&PID_4761";
    public const int RelayChannels = 8;
    public static readonly TimeSpan MaxPulse = TimeSpan.FromMilliseconds(250);

    private readonly ControlCenterLogger _logger;
    private readonly string _cliPath;
    private readonly string? _python;
    private readonly HardwareDocument _cfg;
    private HardwareDiscovery _discovery = HardwareDiscovery.NotFound();
    private string _lastOperation = "—";
    private string _lastError = "";
    private DateTimeOffset? _lastSuccessfulRefresh;

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
            var refresh = _lastSuccessfulRefresh?.ToString("O") ?? "never";
            return $"{_discovery.Status} | {_discovery.Model} | sdk={_discovery.SdkPath} | arch={_discovery.ProcessArch} | " +
                   $"DI={_discovery.DiCount} DO={_discovery.DoCount} | {map} | last={_lastOperation} | refresh={refresh}" +
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
    public DateTimeOffset? LastSuccessfulRefresh => _lastSuccessfulRefresh;

    public string BuildDiagnosticsText()
    {
        var d = _discovery;
        var sb = new StringBuilder();
        sb.AppendLine("CableGuard USB-4761 diagnostics (no secrets)");
        sb.AppendLine($"status={d.Status}");
        sb.AppendLine($"model={d.Model}");
        sb.AppendLine($"sdk_path={d.SdkPath}");
        sb.AppendLine($"assembly={d.AssemblyName}");
        sb.AppendLine($"process_arch={d.ProcessArch}");
        sb.AppendLine($"os_arch={RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"di_count={d.DiCount}");
        sb.AppendLine($"do_count={d.DoCount}");
        sb.AppendLine($"di={FormatBits(d.DiValues)}");
        sb.AppendLine($"do={FormatBits(d.DoValues)}");
        sb.AppendLine($"driver={d.DriverStatus}");
        sb.AppendLine($"error_code={d.ErrorCode}");
        sb.AppendLine($"last_error={_lastError}");
        sb.AppendLine($"last_op={_lastOperation}");
        sb.AppendLine($"last_ok_refresh={_lastSuccessfulRefresh?.ToString("O") ?? "never"}");
        sb.AppendLine($"mapping_configured={MappingConfigured}");
        sb.AppendLine($"test_mode={IsTestMode}");
        sb.AppendLine("detector_relay_auto=false");
        return sb.ToString();
    }

    public void RefreshDiscovery()
    {
        _discovery = Discover();
        if (_discovery.Status == "CONNECTED")
            _lastSuccessfulRefresh = DateTimeOffset.UtcNow;
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
        proc.WaitForExit(20_000);
        if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            return CliResult.Fail(string.IsNullOrWhiteSpace(stderr) ? $"exit={proc.ExitCode}" : stderr);

        try
        {
            var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stdout) ? "{}" : stdout);
            if (proc.ExitCode != 0)
            {
                var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : stderr;
                return CliResult.Fail(err ?? $"exit={proc.ExitCode}", doc);
            }
            return CliResult.Success(doc);
        }
        catch
        {
            return CliResult.Fail("CLI returned non-JSON output.");
        }
    }

    private HardwareDiscovery Discover()
    {
        var processArch = RuntimeInformation.ProcessArchitecture.ToString();
        var pnp = FindPnpDevice();
        var daq = FindDaqNavi();

        if (pnp is null)
            return HardwareDiscovery.NotFound() with { ProcessArch = processArch };

        var (pnpName, pnpId) = pnp.Value;

        if (daq is null)
        {
            return new HardwareDiscovery(
                Status: "SDK NOT FOUND",
                Model: pnpName,
                SerialMasked: Mask(pnpId),
                DriverStatus: "DAQNavi directory missing",
                RelayCount: RelayChannels,
                DiCount: 0,
                DoCount: 0,
                InstanceId: pnpId,
                SdkPath: "—",
                AssemblyName: "—",
                ProcessArch: processArch,
                DiValues: Array.Empty<bool>(),
                DoValues: Array.Empty<bool>(),
                ErrorCode: "SDK_NOT_FOUND");
        }

        if (_python is null || !File.Exists(_cliPath))
        {
            return new HardwareDiscovery(
                Status: "SDK LOAD ERROR",
                Model: pnpName,
                SerialMasked: Mask(pnpId),
                DriverStatus: "CLI/Python missing — cannot probe BioDaq",
                RelayCount: RelayChannels,
                DiCount: 0,
                DoCount: 0,
                InstanceId: pnpId,
                SdkPath: daq,
                AssemblyName: "—",
                ProcessArch: processArch,
                DiValues: Array.Empty<bool>(),
                DoValues: Array.Empty<bool>(),
                ErrorCode: "CLI_MISSING");
        }

        var probe = RunCli("probe", Array.Empty<string>(), CancellationToken.None);
        if (probe.Json is null)
        {
            _lastError = probe.Message;
            return new HardwareDiscovery(
                Status: "OPEN FAILED",
                Model: pnpName,
                SerialMasked: Mask(pnpId),
                DriverStatus: probe.Message,
                RelayCount: RelayChannels,
                DiCount: 0,
                DoCount: 0,
                InstanceId: pnpId,
                SdkPath: daq,
                AssemblyName: "—",
                ProcessArch: processArch,
                DiValues: Array.Empty<bool>(),
                DoValues: Array.Empty<bool>(),
                ErrorCode: "PROBE_NO_JSON");
        }

        var root = probe.Json.RootElement;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "OPEN FAILED" : "OPEN FAILED";
        var err = root.TryGetProperty("error", out var er) ? er.GetString() ?? "" : "";
        var errCode = root.TryGetProperty("error_code", out var ec) ? ec.GetString() ?? "" : "";
        var sdk = root.TryGetProperty("sdk_path", out var sp) ? sp.GetString() ?? daq : daq;
        var asm = root.TryGetProperty("assembly", out var asmel) ? asmel.GetString() ?? "—" : "—";
        var di = ReadBoolArray(root, "di");
        var dout = ReadBoolArray(root, "do");
        var diCount = root.TryGetProperty("di_count", out var dic) && dic.TryGetInt32(out var dci) ? dci : di.Length;
        var doCount = root.TryGetProperty("do_count", out var doc) && doc.TryGetInt32(out var doi) ? doi : dout.Length;

        if (!string.IsNullOrWhiteSpace(err))
            _lastError = err;
        else if (status == "CONNECTED")
            _lastError = "";

        // Never claim CONNECTED from PnP alone — require probe ok + status CONNECTED.
        if (!probe.Ok || status != "CONNECTED")
        {
            if (status is "CONNECTED" or "UNKNOWN" or null)
                status = string.IsNullOrWhiteSpace(errCode) ? "OPEN FAILED" : MapErrorStatus(errCode, status);
        }

        return new HardwareDiscovery(
            Status: status!,
            Model: string.IsNullOrWhiteSpace(pnpName) ? "Advantech USB-4761" : pnpName,
            SerialMasked: Mask(pnpId),
            DriverStatus: status == "CONNECTED" ? $"OK ({sdk})" : (err.Length > 0 ? err : status!),
            RelayCount: doCount > 0 ? doCount : RelayChannels,
            DiCount: diCount,
            DoCount: doCount,
            InstanceId: pnpId,
            SdkPath: sdk ?? daq,
            AssemblyName: asm ?? "—",
            ProcessArch: processArch,
            DiValues: di,
            DoValues: dout,
            ErrorCode: status == "CONNECTED" ? "" : errCode);
    }

    private static string MapErrorStatus(string code, string? fallback) => code switch
    {
        "SDK_NOT_FOUND" => "SDK NOT FOUND",
        "SDK_LOAD" => "SDK LOAD ERROR",
        "ARCH" => "ARCHITECTURE MISMATCH",
        "DI_READ" or "DO_READ" => "READ FAILED",
        "OPEN_FAILED" => "OPEN FAILED",
        "DRIVER" => "DRIVER ERROR",
        _ => string.IsNullOrWhiteSpace(fallback) ? "OPEN FAILED" : fallback!,
    };

    private static bool[] ReadBoolArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<bool>();
        var list = new List<bool>();
        foreach (var el in arr.EnumerateArray())
            list.Add(el.ValueKind == JsonValueKind.True);
        return list.ToArray();
    }

    private static string FormatBits(IReadOnlyList<bool> bits)
    {
        if (bits.Count == 0) return "—";
        return string.Join("", bits.Select(b => b ? "1" : "0"));
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
            // WMI unavailable
        }
        return null;
    }

    public static string? FindDaqNavi()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("ADVANTECH_DAQNAVI_PATH"),
            @"C:\Advantech\DAQNavi",
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
        public static CliResult Fail(string msg, JsonDocument? doc = null) => new(false, msg.Trim(), doc);
    }
}

public sealed record HardwareDiscovery(
    string Status,
    string Model,
    string SerialMasked,
    string DriverStatus,
    int RelayCount,
    int DiCount,
    int DoCount,
    string InstanceId,
    string SdkPath,
    string AssemblyName,
    string ProcessArch,
    IReadOnlyList<bool> DiValues,
    IReadOnlyList<bool> DoValues,
    string ErrorCode)
{
    public static HardwareDiscovery NotFound() => new(
        "NOT FOUND", "—", "—", "—", 0, 0, 0, "", "—", "—",
        RuntimeInformation.ProcessArchitecture.ToString(),
        Array.Empty<bool>(), Array.Empty<bool>(), "NOT_FOUND");
}
