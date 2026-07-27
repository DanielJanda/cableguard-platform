namespace CableGuard.ControlCenter.Core.Models;

public sealed record StartStepResult(ComponentId Id, bool Success, string Message);

public sealed record StartAllResult(bool Success, ComponentId? FailedAt, IReadOnlyList<StartStepResult> Steps);

public sealed record SwitchResult(
    bool Success,
    string Message,
    bool RolledBack = false,
    bool RollbackSucceeded = false);
