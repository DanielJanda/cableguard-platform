using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

/// <summary>Scriptable fake component for orchestrator tests.</summary>
internal sealed class FakeComponent : IComponentController
{
    private readonly Queue<ComponentStatus> _statusSequence;

    public FakeComponent(ComponentId id, bool configured = true, params ComponentStatus[] statusSequence)
    {
        Id = id;
        IsConfigured = configured;
        _statusSequence = new Queue<ComponentStatus>(statusSequence);
    }

    public ComponentId Id { get; }
    public string DisplayName => Id.ToString();
    public bool IsConfigured { get; }
    public string LogFilePath => "";
    public bool StartFails { get; init; }
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }

    public Task<ComponentSnapshot> GetStatusAsync(CancellationToken ct = default)
    {
        var status = _statusSequence.Count > 1 ? _statusSequence.Dequeue() : _statusSequence.Peek();
        return Task.FromResult(new ComponentSnapshot(Id, status, ""));
    }

    public Task<StartStepResult> StartAsync(CancellationToken ct = default)
    {
        StartCalls++;
        return Task.FromResult(new StartStepResult(Id, !StartFails, StartFails ? "start script failed" : "ok"));
    }

    public Task<StartStepResult> StopAsync(CancellationToken ct = default)
    {
        StopCalls++;
        return Task.FromResult(new StartStepResult(Id, true, "stopped"));
    }
}

public class StartAllOrchestratorTests
{
    private static StartAllOrchestrator Orchestrator(params IComponentController[] components) =>
        new(components, readinessTimeout: TimeSpan.FromMilliseconds(300), pollInterval: TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task StartsComponentsInOrder_AndReportsReady()
    {
        var mtx = new FakeComponent(ComponentId.MediaMtx, true, ComponentStatus.Stopped, ComponentStatus.Running);
        var core = new FakeComponent(ComponentId.EventCore, true, ComponentStatus.Stopped, ComponentStatus.Running);
        var monitor = new FakeComponent(ComponentId.Monitor, true, ComponentStatus.Stopped, ComponentStatus.Running);

        var progress = new List<string>();
        var result = await Orchestrator(mtx, core, monitor)
            .StartAllAsync(new Progress<string>(progress.Add));
        await Task.Delay(50); // let Progress<T> posts flush

        Assert.True(result.Success);
        Assert.Null(result.FailedAt);
        Assert.Equal(1, mtx.StartCalls);
        Assert.Equal(1, core.StartCalls);
        Assert.Equal(1, monitor.StartCalls);
        Assert.Contains(progress, p => p.Contains("SYSTEM READY"));
    }

    [Fact]
    public async Task FailedStart_StopsSequence_AndReportsFailedAt()
    {
        var mtx = new FakeComponent(ComponentId.MediaMtx, true, ComponentStatus.Stopped, ComponentStatus.Running);
        var core = new FakeComponent(ComponentId.EventCore, true, ComponentStatus.Stopped) { StartFails = true };
        var monitor = new FakeComponent(ComponentId.Monitor, true, ComponentStatus.Stopped, ComponentStatus.Running);

        var result = await Orchestrator(mtx, core, monitor).StartAllAsync();

        Assert.False(result.Success);
        Assert.Equal(ComponentId.EventCore, result.FailedAt);
        Assert.Equal(0, monitor.StartCalls); // must not continue blindly
    }

    [Fact]
    public async Task ReadinessTimeout_FailsComponent_AndStopsSequence()
    {
        // Component starts but never becomes Running.
        var mtx = new FakeComponent(ComponentId.MediaMtx, true, ComponentStatus.Stopped, ComponentStatus.Starting);
        var core = new FakeComponent(ComponentId.EventCore, true, ComponentStatus.Stopped, ComponentStatus.Running);

        var result = await Orchestrator(mtx, core).StartAllAsync();

        Assert.False(result.Success);
        Assert.Equal(ComponentId.MediaMtx, result.FailedAt);
        Assert.Contains("timeout", result.Steps.Last().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, core.StartCalls);
    }

    [Fact]
    public async Task AlreadyRunningComponent_IsNotStartedAgain()
    {
        // Duplicate process protection.
        var mtx = new FakeComponent(ComponentId.MediaMtx, true, ComponentStatus.Running);
        var core = new FakeComponent(ComponentId.EventCore, true, ComponentStatus.Stopped, ComponentStatus.Running);

        var result = await Orchestrator(mtx, core).StartAllAsync();

        Assert.True(result.Success);
        Assert.Equal(0, mtx.StartCalls);
        Assert.Equal(1, core.StartCalls);
    }

    [Fact]
    public async Task NotConfiguredComponent_IsSkipped_WithoutFailingTheSequence()
    {
        var mtx = new FakeComponent(ComponentId.MediaMtx, true, ComponentStatus.Stopped, ComponentStatus.Running);
        var detector = new FakeComponent(ComponentId.Detector, configured: false, ComponentStatus.Stopped);

        var result = await Orchestrator(mtx, detector).StartAllAsync();

        Assert.True(result.Success);
        Assert.Equal(0, detector.StartCalls);
    }
}
