using System.Reflection;
using System.Runtime.InteropServices;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Native Automation.BDaq4 session via reflection (no Python).
/// Prefer Automation.BDaq4.dll under DAQNavi; never uses DemoDevice.
/// </summary>
public sealed class BioDaqNativeSession : IDisposable
{
    public const int RelayChannels = 8;
    public const int DiChannels = 8;

    private readonly object _doCtrl;
    private readonly object? _diCtrl;
    private readonly MethodInfo _doWriteBit;
    private readonly MethodInfo _doReadBit;
    private readonly MethodInfo? _diReadBit;
    private readonly MethodInfo _doDispose;
    private readonly MethodInfo? _diDispose;
    private bool _disposed;

    public string SdkPath { get; }
    public string AssemblyName { get; }
    public string DeviceDesc { get; }
    public string ProcessArch { get; } = RuntimeInformation.ProcessArchitecture.ToString();

    private BioDaqNativeSession(
        object doCtrl,
        object? diCtrl,
        MethodInfo doWriteBit,
        MethodInfo doReadBit,
        MethodInfo? diReadBit,
        MethodInfo doDispose,
        MethodInfo? diDispose,
        string sdkPath,
        string assemblyName,
        string deviceDesc)
    {
        _doCtrl = doCtrl;
        _diCtrl = diCtrl;
        _doWriteBit = doWriteBit;
        _doReadBit = doReadBit;
        _diReadBit = diReadBit;
        _doDispose = doDispose;
        _diDispose = diDispose;
        SdkPath = sdkPath;
        AssemblyName = assemblyName;
        DeviceDesc = deviceDesc;
    }

    public static BioDaqNativeSession Open(string? daqNaviBase = null)
    {
        var basePath = daqNaviBase ?? AdvantechUsb4761Adapter.FindDaqNavi()
            ?? throw new BioDaqException("SDK_NOT_FOUND", "DAQNavi directory missing");

        var (asmPath, asmName) = FindAssembly(basePath);
        EnsureNativePath(basePath);

        Assembly asm;
        try
        {
            asm = Assembly.LoadFrom(asmPath);
        }
        catch (Exception ex)
        {
            throw new BioDaqException("SDK_LOAD_ERROR", $"Failed to load {asmName}: {ex.Message}");
        }

        var doType = asm.GetType("Automation.BDaq.InstantDoCtrl", throwOnError: false)
            ?? throw new BioDaqException("SDK_LOAD_ERROR", "InstantDoCtrl type missing");
        var diType = asm.GetType("Automation.BDaq.InstantDiCtrl", throwOnError: false);
        var infoType = asm.GetType("Automation.BDaq.DeviceInformation", throwOnError: false)
            ?? throw new BioDaqException("SDK_LOAD_ERROR", "DeviceInformation type missing");

        var doWrite = FindMethod(doType, "WriteBit")
            ?? throw new BioDaqException("SDK_LOAD_ERROR", "WriteBit missing on InstantDoCtrl");
        var doRead = FindMethod(doType, "ReadBit")
            ?? throw new BioDaqException("SDK_LOAD_ERROR", "ReadBit missing on InstantDoCtrl");
        var doDispose = doType.GetMethod("Dispose", Type.EmptyTypes)
            ?? throw new BioDaqException("SDK_LOAD_ERROR", "Dispose missing on InstantDoCtrl");

        MethodInfo? diRead = null;
        MethodInfo? diDispose = null;
        if (diType is not null)
        {
            diRead = FindMethod(diType, "ReadBit");
            diDispose = diType.GetMethod("Dispose", Type.EmptyTypes);
        }

        var prop = doType.GetProperty("SelectedDevice")
            ?? throw new BioDaqException("SDK_LOAD_ERROR", "SelectedDevice missing");

        object doCtrl;
        string usedDesc;
        try
        {
            (doCtrl, usedDesc) = OpenCtrl(doType, infoType, prop);
        }
        catch (BioDaqException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BioDaqException("OPEN_FAILED", $"DO open failed: {ex.Message}");
        }

        object? diCtrl = null;
        if (diType is not null)
        {
            try
            {
                var diProp = diType.GetProperty("SelectedDevice")
                    ?? throw new BioDaqException("SDK_LOAD_ERROR", "DI SelectedDevice missing");
                (diCtrl, _) = OpenCtrl(diType, infoType, diProp);
            }
            catch (Exception ex)
            {
                TryDispose(doCtrl, doDispose);
                throw new BioDaqException("OPEN_FAILED", $"DI open failed: {ex.Message}");
            }
        }

        return new BioDaqNativeSession(
            doCtrl, diCtrl, doWrite, doRead, diRead, doDispose, diDispose,
            basePath, asmName, usedDesc);
    }

    public bool[] ReadDo()
    {
        var values = new bool[RelayChannels];
        for (var i = 0; i < RelayChannels; i++)
        {
            var port = i / 4;
            var bit = i % 4;
            try
            {
                values[i] = InvokeReadBit(_doCtrl, _doReadBit, port, bit);
            }
            catch (Exception ex)
            {
                throw new BioDaqException("READ_FAILED", $"DO read ch{i + 1}: {Unwrap(ex)}");
            }
        }
        return values;
    }

    public bool[] ReadDi()
    {
        if (_diCtrl is null || _diReadBit is null)
            return Array.Empty<bool>();

        var values = new bool[DiChannels];
        for (var i = 0; i < DiChannels; i++)
        {
            var port = i / 8;
            var bit = i % 8;
            try
            {
                values[i] = InvokeReadBit(_diCtrl, _diReadBit, port, bit);
            }
            catch (Exception ex)
            {
                throw new BioDaqException("READ_FAILED", $"DI read ch{i}: {Unwrap(ex)}");
            }
        }
        return values;
    }

    public void WriteChannel(int channel1Based, bool on)
    {
        if (channel1Based is < 1 or > RelayChannels)
            throw new BioDaqException("WRITE_FAILED", $"channel out of range: {channel1Based}");
        var idx = channel1Based - 1;
        var port = idx / 4;
        var bit = idx % 4;
        byte val = on ? (byte)1 : (byte)0;
        try
        {
            InvokeWriteBit(_doCtrl, _doWriteBit, port, bit, val);
        }
        catch (Exception ex)
        {
            throw new BioDaqException("WRITE_FAILED", $"WriteBit ch{channel1Based}: {Unwrap(ex)}");
        }
    }

    public void AllOff()
    {
        for (var ch = 1; ch <= RelayChannels; ch++)
            WriteChannel(ch, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TryDispose(_diCtrl, _diDispose);
        TryDispose(_doCtrl, _doDispose);
    }

    private static (object ctrl, string desc) OpenCtrl(Type ctrlType, Type infoType, PropertyInfo selectedDevice)
    {
        Exception? last = null;
        foreach (var desc in new[] { "USB-4761,BID#0", "USB-4761" })
        {
            try
            {
                var ctrl = Activator.CreateInstance(ctrlType)
                    ?? throw new BioDaqException("OPEN_FAILED", "CreateInstance returned null");
                object info;
                try
                {
                    info = Activator.CreateInstance(infoType, desc)
                        ?? throw new BioDaqException("OPEN_FAILED", "DeviceInformation null");
                }
                catch
                {
                    info = Activator.CreateInstance(infoType)
                        ?? throw new BioDaqException("OPEN_FAILED", "DeviceInformation ctor failed");
                    var nameProp = infoType.GetProperty("Description") ?? infoType.GetProperty("DeviceName");
                    nameProp?.SetValue(info, desc);
                }
                selectedDevice.SetValue(ctrl, info);
                return (ctrl, desc);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new BioDaqException("OPEN_FAILED", $"USB-4761 DO/DI not found: {Unwrap(last)}");
    }

    private static (string Path, string Name) FindAssembly(string basePath)
    {
        foreach (var ver in new[] { "4.0.0.0", "1.0.0.0" })
        {
            foreach (var name in new[] { "Automation.BDaq4", "Automation.BDaq" })
            {
                var dll = Path.Combine(basePath, "Automation.BDaq", ver, name + ".dll");
                if (File.Exists(dll)) return (dll, name);
            }
        }
        foreach (var name in new[] { "Automation.BDaq4", "Automation.BDaq" })
        {
            var dll = Path.Combine(basePath, name + ".dll");
            if (File.Exists(dll)) return (dll, name);
        }
        throw new BioDaqException("SDK_NOT_FOUND", "Automation.BDaq4.dll not found under DAQNavi");
    }

    private static void EnsureNativePath(string basePath)
    {
        var amd64 = Path.Combine(basePath, "Driver", "USB4761", "amd64");
        var driver = Path.Combine(basePath, "Driver");
        var prepend = Directory.Exists(amd64) ? amd64 : (Directory.Exists(driver) ? driver : null);
        if (prepend is null) return;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Contains(prepend, StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", prepend + Path.PathSeparator + path);
    }

    private static MethodInfo? FindMethod(Type type, string name) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == name);

    private static bool InvokeReadBit(object ctrl, MethodInfo method, int port, int bit)
    {
        var ps = method.GetParameters();
        // Common BDaq: ErrorCode ReadBit(int port, int bit, out byte data)
        if (ps.Length == 3 && ps[2].ParameterType.IsByRef)
        {
            var args = new object?[] { port, bit, (byte)0 };
            method.Invoke(ctrl, args);
            return ToBool(args[2]);
        }
        // Fallback: byte/bool ReadBit(int, int)
        var raw = method.Invoke(ctrl, new object[] { port, bit });
        return ToBool(raw);
    }

    private static void InvokeWriteBit(object ctrl, MethodInfo method, int port, int bit, byte val)
    {
        var ps = method.GetParameters();
        if (ps.Length >= 3)
            method.Invoke(ctrl, new object[] { port, bit, val });
        else
            method.Invoke(ctrl, new object[] { port, bit, val != 0 });
    }

    private static void TryDispose(object? ctrl, MethodInfo? dispose)
    {
        if (ctrl is null || dispose is null) return;
        try { dispose.Invoke(ctrl, null); } catch { /* ignore */ }
    }

    private static bool ToBool(object? raw) =>
        raw switch
        {
            bool b => b,
            byte by => by != 0,
            int i => i != 0,
            _ => Convert.ToInt32(raw) != 0,
        };

    private static string Unwrap(Exception? ex)
    {
        if (ex is null) return "unknown";
        return ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;
    }
}

public sealed class BioDaqException : Exception
{
    public string ErrorCode { get; }
    public BioDaqException(string code, string message) : base(message) => ErrorCode = code;
}
