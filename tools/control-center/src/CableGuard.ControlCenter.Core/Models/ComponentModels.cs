namespace CableGuard.ControlCenter.Core.Models;

public enum ComponentId
{
    MediaMtx,
    EventCore,
    Monitor,
    Detector,
}

public enum ComponentStatus
{
    Unknown,
    Stopped,
    Starting,
    Running,
    Degraded,
    Fault,
    NotConfigured,
}

public enum SystemStatus
{
    Ready,
    Degraded,
    Stopped,
    Fault,
}

/// <summary>Point-in-time health snapshot of one component.</summary>
public sealed record ComponentSnapshot(
    ComponentId Id,
    ComponentStatus Status,
    string Detail,
    int? ProcessId = null)
{
    public static ComponentSnapshot NotConfigured(ComponentId id, string detail) =>
        new(id, ComponentStatus.NotConfigured, detail);
}

/// <summary>Raw probe results used by the pure status evaluators.</summary>
public sealed record ProbeResults(
    bool ProcessAlive,
    bool? HttpHealthy = null,
    bool? WhepReachable = null,
    bool? PathReady = null,
    bool? DeepHealthAvailable = null);
