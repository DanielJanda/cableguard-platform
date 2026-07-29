using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>Aggregates component snapshots into the SYSTEM STATUS shown on the Overview tab.</summary>
public static class SystemStatusCalculator
{
    public static SystemStatus Calculate(IReadOnlyList<ComponentSnapshot> snapshots)
    {
        var relevant = snapshots.Where(s => s.Status != ComponentStatus.NotConfigured).ToList();
        if (relevant.Count == 0) return SystemStatus.Stopped;

        if (relevant.Any(s => s.Status == ComponentStatus.Fault)) return SystemStatus.Fault;
        if (relevant.All(s => s.Status == ComponentStatus.Running)) return SystemStatus.Ready;
        if (relevant.All(s => s.Status is ComponentStatus.Stopped or ComponentStatus.Unknown)) return SystemStatus.Stopped;
        return SystemStatus.Degraded;
    }
}

/// <summary>
/// Pure status evaluators — health is decided from probe results, never from process existence alone.
/// </summary>
public static class StatusEvaluators
{
    public static ComponentStatus EvaluateMediaMtx(ProbeResults p)
    {
        // Functional RUNNING requires service + process + Control API + paths.
        // WHEP is reported in Detail but is not the Control API probe.
        if (!p.ProcessAlive) return ComponentStatus.Stopped;
        if (p.ServiceRunning == false) return ComponentStatus.Degraded;  // process without owning service
        if (p.ControlApiReady != true) return ComponentStatus.Fault;     // process up but Control API dead
        if (p.PathReady != true) return ComponentStatus.Degraded;        // API up, expected paths not ready
        return ComponentStatus.Running;
    }

    public static ComponentStatus EvaluateEventCore(ProbeResults p)
    {
        if (p.HttpHealthy == true) return ComponentStatus.Running;
        if (p.ProcessAlive) return ComponentStatus.Fault;                // process up but /health failing
        return ComponentStatus.Stopped;
    }

    public static ComponentStatus EvaluateMonitor(ProbeResults p)
    {
        if (p.HttpHealthy == true) return ComponentStatus.Running;
        if (p.ProcessAlive) return ComponentStatus.Fault;
        return ComponentStatus.Stopped;
    }

    public static ComponentStatus EvaluateDetector(ProbeResults p)
    {
        // Process required. When Event Core exposes video_input, DeepHealthAvailable=true
        // and HttpHealthy reflects connection_state / readiness.
        if (!p.ProcessAlive) return ComponentStatus.Stopped;
        if (p.DeepHealthAvailable == true && p.HttpHealthy == false) return ComponentStatus.Degraded;
        return ComponentStatus.Running;
    }
}
