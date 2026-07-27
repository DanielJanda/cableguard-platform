using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Switches the physical camera behind a logical stream:
/// validate → PATCH MediaMTX Control API (127.0.0.1) → wait path READY → verify WHEP →
/// persist to gitignored local yml + registry. On failure the previous source is restored via the API.
/// Messages never contain RTSP URLs or credentials.
/// </summary>
public sealed class StreamSwitchService
{
    private readonly IMediaMtxApi _api;
    private readonly IMediaMtxConfigPersister _persister;
    private readonly IHttpProber _prober;
    private readonly string _whepBase;
    private readonly TimeSpan _readyTimeout;
    private readonly TimeSpan _pollInterval;

    public StreamSwitchService(
        IMediaMtxApi api,
        IMediaMtxConfigPersister persister,
        IHttpProber prober,
        string whepBase,
        TimeSpan? readyTimeout = null,
        TimeSpan? pollInterval = null)
    {
        _api = api;
        _persister = persister;
        _prober = prober;
        _whepBase = whepBase.TrimEnd('/');
        _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(25);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public async Task<SwitchResult> SwitchPrimaryAsync(
        CameraRegistryDocument registry,
        string logicalStream,
        string newCameraId,
        Action<CameraRegistryDocument>? saveRegistry = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var newCamera = registry.Cameras.FirstOrDefault(c => c.CameraId == newCameraId);
        if (newCamera is null)
            return new SwitchResult(false, $"Unknown camera '{newCameraId}'.");
        if (!newCamera.Enabled)
            return new SwitchResult(false, $"Camera '{newCameraId}' is disabled.");

        progress?.Report($"Validating camera '{newCamera.DisplayName}'...");

        // The new camera's own MediaMTX path is the authoritative, already-working source definition.
        var newSource = await _api.GetConfiguredSourceAsync(newCamera.MediaMtxPath, ct);
        if (string.IsNullOrWhiteSpace(newSource))
            return new SwitchResult(false,
                $"Cannot resolve source for camera '{newCameraId}' (MediaMTX path '{newCamera.MediaMtxPath}' not configured).");

        var newCameraReady = await _api.IsPathReadyAsync(newCamera.MediaMtxPath, ct);
        if (newCameraReady != true)
            return new SwitchResult(false,
                $"Camera path '{newCamera.MediaMtxPath}' is not READY — refusing to switch production stream to a dead source.");

        var oldSource = await _api.GetConfiguredSourceAsync(logicalStream, ct);
        if (string.IsNullOrWhiteSpace(oldSource))
            return new SwitchResult(false, $"Cannot read current source of '{logicalStream}' for rollback safety.");

        progress?.Report($"Switching '{logicalStream}' to camera '{newCamera.DisplayName}'...");
        var patched = await _api.PatchPathSourceAsync(logicalStream, newSource, ct);
        if (!patched)
            return new SwitchResult(false, "MediaMTX Control API rejected the path patch. No changes applied.");

        var ready = await WaitReadyAndWhepAsync(logicalStream, progress, ct);
        if (!ready)
        {
            progress?.Report("New stream did not become READY — rolling back...");
            var rolledBack = await _api.PatchPathSourceAsync(logicalStream, oldSource, ct);
            var restoreOk = rolledBack && await WaitReadyAndWhepAsync(logicalStream, progress, ct);
            return new SwitchResult(false,
                restoreOk
                    ? "Switch failed; previous camera restored and verified READY."
                    : "Switch failed; rollback attempted but the stream did not verify READY — check MediaMTX logs.",
                RolledBack: true, RollbackSucceeded: restoreOk);
        }

        if (!_persister.PersistPathSource(logicalStream, newSource, out var persistMessage))
        {
            // Runtime switch is live, but a restart would revert. Report honestly, do not roll back a working stream.
            return new SwitchResult(true,
                $"Stream switched and READY, but persisting to local config failed: {persistMessage} " +
                "The switch will revert on next MediaMTX restart.");
        }

        var mapping = registry.StreamMappings.FirstOrDefault(m => m.LogicalStream == logicalStream);
        if (mapping is null)
        {
            mapping = new StreamMapping { LogicalStream = logicalStream };
            registry.StreamMappings.Add(mapping);
        }
        mapping.PrimaryCameraId = newCameraId;
        saveRegistry?.Invoke(registry);

        progress?.Report($"'{logicalStream}' now served by '{newCamera.DisplayName}'.");
        return new SwitchResult(true, $"Primary camera for '{logicalStream}' switched to '{newCamera.DisplayName}'.");
    }

    private async Task<bool> WaitReadyAndWhepAsync(string pathName, IProgress<string>? progress, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _readyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (await _api.IsPathReadyAsync(pathName, ct) == true)
            {
                var whepStatus = await _prober.OptionsStatusCodeAsync($"{_whepBase}/{pathName}/whep", ct);
                if (whepStatus is >= 200 and < 300)
                {
                    progress?.Report($"Path '{pathName}' READY, WHEP verified.");
                    return true;
                }
            }
            await Task.Delay(_pollInterval, ct);
        }
        return false;
    }
}
