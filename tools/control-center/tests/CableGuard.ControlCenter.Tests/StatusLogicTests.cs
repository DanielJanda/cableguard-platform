using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public class StatusLogicTests
{
    private static ComponentSnapshot Snap(ComponentId id, ComponentStatus status) => new(id, status, "");

    [Fact]
    public void AllRunning_IsReady()
    {
        var status = SystemStatusCalculator.Calculate(new[]
        {
            Snap(ComponentId.MediaMtx, ComponentStatus.Running),
            Snap(ComponentId.EventCore, ComponentStatus.Running),
            Snap(ComponentId.Monitor, ComponentStatus.Running),
        });
        Assert.Equal(SystemStatus.Ready, status);
    }

    [Fact]
    public void AllStopped_IsStopped()
    {
        var status = SystemStatusCalculator.Calculate(new[]
        {
            Snap(ComponentId.MediaMtx, ComponentStatus.Stopped),
            Snap(ComponentId.EventCore, ComponentStatus.Stopped),
        });
        Assert.Equal(SystemStatus.Stopped, status);
    }

    [Fact]
    public void MixedRunningStopped_IsDegraded()
    {
        var status = SystemStatusCalculator.Calculate(new[]
        {
            Snap(ComponentId.MediaMtx, ComponentStatus.Running),
            Snap(ComponentId.EventCore, ComponentStatus.Stopped),
        });
        Assert.Equal(SystemStatus.Degraded, status);
    }

    [Fact]
    public void AnyFault_IsFault()
    {
        var status = SystemStatusCalculator.Calculate(new[]
        {
            Snap(ComponentId.MediaMtx, ComponentStatus.Running),
            Snap(ComponentId.EventCore, ComponentStatus.Fault),
        });
        Assert.Equal(SystemStatus.Fault, status);
    }

    [Fact]
    public void NotConfiguredComponents_AreExcludedFromAggregate()
    {
        var status = SystemStatusCalculator.Calculate(new[]
        {
            Snap(ComponentId.MediaMtx, ComponentStatus.Running),
            Snap(ComponentId.EventCore, ComponentStatus.Running),
            Snap(ComponentId.Monitor, ComponentStatus.Running),
            Snap(ComponentId.Detector, ComponentStatus.NotConfigured),
        });
        Assert.Equal(SystemStatus.Ready, status);
    }

    // --- service status detection: health-based, not process existence ---

    [Fact]
    public void MediaMtx_ProcessUpButWhepDown_IsFault_NotRunning()
    {
        var status = StatusEvaluators.EvaluateMediaMtx(new ProbeResults(ProcessAlive: true, WhepReachable: false));
        Assert.Equal(ComponentStatus.Fault, status);
    }

    [Fact]
    public void MediaMtx_WhepUpButPathNotReady_IsDegraded()
    {
        var status = StatusEvaluators.EvaluateMediaMtx(new ProbeResults(true, WhepReachable: true, PathReady: false));
        Assert.Equal(ComponentStatus.Degraded, status);
    }

    [Fact]
    public void MediaMtx_AllProbesHealthy_IsRunning()
    {
        var status = StatusEvaluators.EvaluateMediaMtx(new ProbeResults(true, WhepReachable: true, PathReady: true));
        Assert.Equal(ComponentStatus.Running, status);
    }

    [Fact]
    public void EventCore_ProcessUpButHealthFailing_IsFault()
    {
        var status = StatusEvaluators.EvaluateEventCore(new ProbeResults(true, HttpHealthy: false));
        Assert.Equal(ComponentStatus.Fault, status);
    }

    [Fact]
    public void EventCore_NoProcess_IsStopped()
    {
        var status = StatusEvaluators.EvaluateEventCore(new ProbeResults(false, HttpHealthy: false));
        Assert.Equal(ComponentStatus.Stopped, status);
    }

    [Fact]
    public void Monitor_HttpOk_IsRunning()
    {
        var status = StatusEvaluators.EvaluateMonitor(new ProbeResults(true, HttpHealthy: true));
        Assert.Equal(ComponentStatus.Running, status);
    }

    [Fact]
    public void Detector_ProcessOnly_IsRunning_DeepHealthNotAvailable()
    {
        var status = StatusEvaluators.EvaluateDetector(new ProbeResults(true, DeepHealthAvailable: false));
        Assert.Equal(ComponentStatus.Running, status);
    }
}
