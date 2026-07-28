using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Advantech USB-4761 via native Automation.BDaq4 (reflection).
/// CONNECTED only after SDK load + device open + DI/DO read.
/// No Python dependency for Admin Studio. No detector→relay automation.
/// </summary>
public sealed class AdvantechUsb4761Adapter : IHardwareAdapter
{
    public const string VendorProductHint = "VID_1809&PID_4761";
    public const int RelayChannels = 8;
    public static readonly TimeSpan MaxPulse = TimeSpan.FromMilliseconds(250);

    private readonly ControlCenterLogger _logger;
    private readonly HardwareDocument _cfg;
    private HardwareDiscovery _discovery = HardwareDiscovery.NotFound();
    private string _lastOperation = "—";
    private string _lastError = "";
    private DateTimeOffset? _lastSuccessfulRefresh;

    private AdvantechUsb4761Adapter(ControlCenterLogger logger, HardwareDocument cfg)
    {
        _logger = logger;
        _cfg = cfg;
        RefreshDiscovery();
    }

    public static IHardwareAdapter Create(ControlCenterConfig config, ControlCenterLogger logger)
    {
        var cfgPath = RuntimeConfigPaths.Hardware(config);
        var cfg = File.Exists(cfgPath) ? HardwareConfigService.Load(cfgPath) : new HardwareDocument();
        return new AdvantechUsb4761Adapter(logger, cfg);
    }

    public string StatusDetail
    {
        get
        {
            var map = MappingConfigured
                ? $"mapping green={_cfg.GreenChannel} red={_cfg.RedChannel} buzzer={_cfg.BuzzerChannel}" +
                  $" physical={(MappingPhysicallyConfirmed ? "CONFIRMED" : "HISTORICAL_ONLY")}"
                : "semantic mapping NOT CONFIGURED (Green/Red/Buzzer disabled)";
            var refresh = _lastSuccessfulRefresh?.ToLocalTime().ToString("HH:mm:ss") ?? "never";
            return $"{_discovery.Status} | {_discovery.Model} | asm={_discovery.AssemblyName} | arch={_discovery.ProcessArch} | " +
                   $"DI={_discovery.DiCount}[{FormatBits(_discovery.DiValues)}] DO={_discovery.DoCount}[{FormatBits(_discovery.DoValues)}] | " +
                   $"{map} | last={_lastOperation} | refresh={refresh}" +
                   (string.IsNullOrWhiteSpace(_discovery.ErrorCode) ? "" : $" | code={_discovery.ErrorCode}") +
                   (string.IsNullOrWhiteSpace(_lastError) ? "" : $" | err={_lastError}");
        }
    }

    public bool IsAvailable => _discovery.Status is "CONNECTED";
    public bool IsTestMode { get; set; }
    public bool MappingConfigured =>
        _cfg.GreenChannel is > 0 && _cfg.RedChannel is > 0 && _cfg.BuzzerChannel is > 0;
    /// <summary>Semantic writes require historical channels AND explicit physical confirmation flag.</summary>
    public bool MappingPhysicallyConfirmed => MappingConfigured && _cfg.MappingPhysicallyConfirmed;
    public HardwareDiscovery Discovery => _discovery;
    public string LastOperation => _lastOperation;
    public string LastError => _lastError;
    public DateTimeOffset? LastSuccessfulRefresh => _lastSuccessfulRefresh;

    public string BuildDiagnosticsText()
    {
        var d = _discovery;
        var sb = new StringBuilder();
        sb.AppendLine("CableGuard USB-4761 diagnostics (no secrets)");
        sb.AppendLine($"backend=native-Automation.BDaq4");
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
        sb.AppendLine($"mapping_physically_confirmed={MappingPhysicallyConfirmed}");
        sb.AppendLine($"test_mode={IsTestMode}");
        sb.AppendLine("detector_relay_auto=false");
        return sb.ToString();
    }

    public void RefreshDiscovery()
    {
        _discovery = Discover();
        if (_discovery.Status == "CONNECTED")
            _lastSuccessfulRefresh = DateTimeOffset.UtcNow;
        _logger.Info($"[HW] Discovery: {_discovery.Status} code={_discovery.ErrorCode} asm={_discovery.AssemblyName}");
    }

    public Task<bool> EnsureConnectedAsync(CancellationToken ct = default)
    {
        RefreshDiscovery();
        return Task.FromResult(IsAvailable);
    }

    public Task<IReadOnlyDictionary<string, bool>> ReadDigitalInputsAsync(CancellationToken ct = default)
    {
        var map = new Dictionary<string, bool>();
        var i = 0;
        foreach (var v in _discovery.DiValues)
        {
            map[$"DI{i}"] = v;
            i++;
        }
        return Task.FromResult<IReadOnlyDictionary<string, bool>>(map);
    }

    public async Task PulseRelayAsync(int channel, TimeSpan duration, CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        if (channel is < 1 or > RelayChannels)
            throw new InvalidOperationException($"Channel {channel} out of range 1..{RelayChannels}.");
        var clamped = HardwareSafety.ClampPulse(duration, MaxPulse);
        using var session = BioDaqNativeSession.Open(_discovery.SdkPath is "—" or "" ? null : _discovery.SdkPath);
        session.AllOff();
        session.WriteChannel(channel, true);
        try
        {
            await Task.Delay(clamped, ct).ConfigureAwait(false);
        }
        finally
        {
            session.WriteChannel(channel, false);
            session.AllOff();
        }
        var after = session.ReadDo();
        _lastOperation = $"pulse ch={channel} {(int)clamped.TotalMilliseconds}ms readback={FormatBits(after)}";
        _lastError = "";
        _logger.Info($"[HW] {_lastOperation}");
        RefreshDiscovery();
    }

    public async Task SetSemaphoreAsync(string color, bool on, CancellationToken ct = default)
    {
        HardwareSafety.EnsureTestMode(this);
        if (!MappingPhysicallyConfirmed)
            throw new InvalidOperationException(
                "Semantic mapping is HISTORICAL only — set mapping_physically_confirmed=true after live wiring check.");

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
        using var session = BioDaqNativeSession.Open(_discovery.SdkPath is "—" or "" ? null : _discovery.SdkPath);
        session.AllOff();
        _lastOperation = "all-off ok=True";
        _lastError = "";
        _logger.Info($"[HW] {_lastOperation}");
        RefreshDiscovery();
        return Task.CompletedTask;
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
            return Fail(pnpName, pnpId, "SDK NOT FOUND", "SDK_NOT_FOUND", "DAQNavi directory missing", processArch, "—", "—");
        }

        if (processArch is not "X64" and not "Arm64")
        {
            return Fail(pnpName, pnpId, "ARCHITECTURE MISMATCH", "ARCHITECTURE_MISMATCH",
                $"Process arch {processArch} — need x64 WPF for BDaq4", processArch, daq, "—");
        }

        try
        {
            using var session = BioDaqNativeSession.Open(daq);
            var di = session.ReadDi();
            var dout = session.ReadDo();
            _lastError = "";
            return new HardwareDiscovery(
                Status: "CONNECTED",
                Model: string.IsNullOrWhiteSpace(pnpName) ? "Advantech USB-4761" : pnpName,
                SerialMasked: Mask(pnpId),
                DriverStatus: $"OK native {session.AssemblyName} ({session.DeviceDesc})",
                RelayCount: dout.Length > 0 ? dout.Length : RelayChannels,
                DiCount: di.Length,
                DoCount: dout.Length,
                InstanceId: pnpId,
                SdkPath: session.SdkPath,
                AssemblyName: session.AssemblyName,
                ProcessArch: processArch,
                DiValues: di,
                DoValues: dout,
                ErrorCode: "");
        }
        catch (BioDaqException ex)
        {
            _lastError = ex.Message;
            var status = ex.ErrorCode switch
            {
                "SDK_NOT_FOUND" => "SDK NOT FOUND",
                "SDK_LOAD_ERROR" => "SDK LOAD ERROR",
                "ARCHITECTURE_MISMATCH" => "ARCHITECTURE MISMATCH",
                "READ_FAILED" => "READ FAILED",
                "OPEN_FAILED" => "OPEN FAILED",
                _ => "DRIVER ERROR",
            };
            return Fail(pnpName, pnpId, status, ex.ErrorCode, ex.Message, processArch, daq, "—");
        }
        catch (BadImageFormatException ex)
        {
            _lastError = ex.Message;
            return Fail(pnpName, pnpId, "ARCHITECTURE MISMATCH", "ARCHITECTURE_MISMATCH", ex.Message, processArch, daq, "—");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return Fail(pnpName, pnpId, "DRIVER ERROR", "DRIVER_ERROR", ex.Message, processArch, daq, "—");
        }
    }

    private static HardwareDiscovery Fail(
        string model, string id, string status, string code, string detail,
        string arch, string sdk, string asm) =>
        new(status, model, Mask(id), detail, RelayChannels, 0, 0, id, sdk, asm, arch,
            Array.Empty<bool>(), Array.Empty<bool>(), code);

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

    private static string Mask(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "—";
        var parts = deviceId.Split('\\');
        var last = parts.Length > 0 ? parts[^1] : deviceId;
        if (last.Length <= 6) return "***";
        return last[..3] + "…" + last[^2];
    }

    private static string FormatBits(IReadOnlyList<bool> bits) =>
        bits.Count == 0 ? "—" : string.Join("", bits.Select(b => b ? "1" : "0"));
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
