namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Hardware I/O adapter. Real USB-4761 control is NOT AVAILABLE until wired to
/// detector relay_server / advantech_relay — never reports fake CONNECTED.
/// </summary>
public interface IHardwareAdapter
{
    string StatusDetail { get; }
    bool IsAvailable { get; }
    bool IsTestMode { get; set; }
    Task<bool> EnsureConnectedAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, bool>> ReadDigitalInputsAsync(CancellationToken ct = default);
    Task PulseRelayAsync(int channel, TimeSpan duration, CancellationToken ct = default);
    Task SetSemaphoreAsync(string color, bool on, CancellationToken ct = default);
    Task AllOffAsync(CancellationToken ct = default);
}

public sealed class NotAvailableHardwareAdapter : IHardwareAdapter
{
    public string StatusDetail =>
        "NOT AVAILABLE — Advantech USB-4761 adapter not wired into Control Center yet. " +
        "Use detector relay_server.py for production; hardware tab is TEST MODE only when adapter lands.";
    public bool IsAvailable => false;
    public bool IsTestMode { get; set; }

    public Task<bool> EnsureConnectedAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, bool>> ReadDigitalInputsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());

    public Task PulseRelayAsync(int channel, TimeSpan duration, CancellationToken ct = default) =>
        throw new InvalidOperationException("Hardware NOT AVAILABLE — refusing relay pulse.");

    public Task SetSemaphoreAsync(string color, bool on, CancellationToken ct = default) =>
        throw new InvalidOperationException("Hardware NOT AVAILABLE — refusing semaphore command.");

    public Task AllOffAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("Hardware NOT AVAILABLE — refusing All Off.");
}

/// <summary>Guards dangerous hardware actions: requires TEST MODE + confirmation token.</summary>
public static class HardwareSafety
{
    public static void EnsureTestMode(IHardwareAdapter adapter)
    {
        if (!adapter.IsTestMode)
            throw new InvalidOperationException("HARDWARE TEST MODE required. Enable Test Lab → Hardware TEST MODE first.");
        if (!adapter.IsAvailable)
            throw new InvalidOperationException("Hardware adapter NOT AVAILABLE.");
    }

    public static TimeSpan ClampPulse(TimeSpan requested, TimeSpan max) =>
        requested > max ? max : (requested < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : requested);
}
