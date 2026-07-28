namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Pure decisions for MediaMTX start/adopt — unit-tested without launching processes.
/// </summary>
public static class MediaMtxLifecycleLogic
{
    public enum DecisionKind
    {
        NoOpAlreadyManaged,
        AdoptOrphan,
        HealStalePidAndAdopt,
        StartNew,
        RefuseForeignPortOwner,
        RefuseMultipleProcesses,
    }

    public sealed record Decision(DecisionKind Kind, int? PidToAdopt, string Message);

    /// <param name="pidFilePid">PID from mediamtx.pid if parseable; null if missing/stale file content.</param>
    /// <param name="pidFileProcessAlive">Whether pidFilePid is a live process.</param>
    /// <param name="liveMediaMtxPids">All live mediamtx.exe PIDs.</param>
    /// <param name="portOwnerPid">Owning PID of a CableGuard MediaMTX port if occupied.</param>
    /// <param name="portOwnerIsMediaMtx">True when port owner process name is mediamtx.</param>
    public static Decision Decide(
        int? pidFilePid,
        bool pidFileProcessAlive,
        IReadOnlyList<int> liveMediaMtxPids,
        int? portOwnerPid,
        bool portOwnerIsMediaMtx)
    {
        var distinct = liveMediaMtxPids.Distinct().OrderBy(x => x).ToList();
        if (distinct.Count > 1)
        {
            return new Decision(
                DecisionKind.RefuseMultipleProcesses,
                null,
                $"Multiple MediaMTX processes found (PIDs {string.Join(", ", distinct)}). Refusing silent adopt/start.");
        }

        if (pidFilePid is not null && pidFileProcessAlive)
        {
            return new Decision(
                DecisionKind.NoOpAlreadyManaged,
                pidFilePid,
                $"Managed MediaMTX already running (PID {pidFilePid}).");
        }

        if (distinct.Count == 1)
        {
            var live = distinct[0];
            if (pidFilePid is not null && pidFilePid != live)
            {
                return new Decision(
                    DecisionKind.HealStalePidAndAdopt,
                    live,
                    $"Stale PID file had {pidFilePid}; adopting live MediaMTX PID {live}.");
            }

            return new Decision(
                DecisionKind.AdoptOrphan,
                live,
                $"Adopting orphan MediaMTX PID {live}.");
        }

        if (portOwnerPid is not null)
        {
            if (portOwnerIsMediaMtx)
            {
                return new Decision(
                    DecisionKind.AdoptOrphan,
                    portOwnerPid,
                    $"Port owned by MediaMTX PID {portOwnerPid}; adopting.");
            }

            return new Decision(
                DecisionKind.RefuseForeignPortOwner,
                null,
                $"Port owned by foreign PID {portOwnerPid}. Refusing duplicate start.");
        }

        return new Decision(DecisionKind.StartNew, null, "No MediaMTX running; start new process.");
    }

    public static void HealPidFile(string pidFilePath, int pid)
    {
        var dir = Path.GetDirectoryName(pidFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(pidFilePath, pid.ToString());
    }
}
