using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Starts the whole stack in dependency order (MediaMTX → Event Core → Monitor → Detector),
/// waiting for health-based readiness after each step. Stops at the first failure.
/// </summary>
public sealed class StartAllOrchestrator
{
    private readonly IReadOnlyList<IComponentController> _components;
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _pollInterval;

    public StartAllOrchestrator(
        IReadOnlyList<IComponentController> componentsInStartOrder,
        TimeSpan readinessTimeout,
        TimeSpan? pollInterval = null)
    {
        _components = componentsInStartOrder;
        _readinessTimeout = readinessTimeout;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public async Task<StartAllResult> StartAllAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var steps = new List<StartStepResult>();

        foreach (var component in _components)
        {
            if (!component.IsConfigured)
            {
                progress?.Report($"{component.DisplayName}: NOT CONFIGURED — skipped.");
                steps.Add(new StartStepResult(component.Id, true, "Skipped (not configured)."));
                continue;
            }

            // Duplicate protection: never start what is already healthy.
            var current = await component.GetStatusAsync(ct);
            if (current.Status == ComponentStatus.Running)
            {
                progress?.Report($"{component.DisplayName}: already running — skipped.");
                steps.Add(new StartStepResult(component.Id, true, "Already running."));
                continue;
            }

            progress?.Report($"Starting {component.DisplayName}...");
            var startResult = await component.StartAsync(ct);
            if (!startResult.Success)
            {
                progress?.Report($"FAILED AT: {component.DisplayName} — {startResult.Message}");
                steps.Add(startResult);
                return new StartAllResult(false, component.Id, steps);
            }

            var ready = await WaitForReadyAsync(component, progress, ct);
            if (!ready)
            {
                var msg = $"Readiness timeout after {_readinessTimeout.TotalSeconds:0}s.";
                progress?.Report($"FAILED AT: {component.DisplayName} — {msg}");
                steps.Add(new StartStepResult(component.Id, false, msg));
                return new StartAllResult(false, component.Id, steps);
            }

            progress?.Report($"{component.DisplayName} READY.");
            steps.Add(new StartStepResult(component.Id, true, "Started and ready."));
        }

        progress?.Report("SYSTEM READY.");
        return new StartAllResult(true, null, steps);
    }

    private async Task<bool> WaitForReadyAsync(IComponentController component, IProgress<string>? progress, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _readinessTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = await component.GetStatusAsync(ct);
            if (snapshot.Status == ComponentStatus.Running) return true;
            if (snapshot.Status == ComponentStatus.Fault)
                progress?.Report($"{component.DisplayName}: {snapshot.Detail}");
            await Task.Delay(_pollInterval, ct);
        }
        return false;
    }
}
