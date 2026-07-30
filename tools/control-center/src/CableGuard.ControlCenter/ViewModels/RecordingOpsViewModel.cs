using System.Diagnostics;
using System.IO;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

public sealed class RecordingOpsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly SelectedCameraSession _session;
    private readonly IMediaMtxApi _api;
    private string _status = "Načítám…";

    public RecordingOpsViewModel(ControlCenterConfig config, SelectedCameraSession session, IMediaMtxApi api)
    {
        _config = config;
        _session = session;
        _api = api;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenCacheFolderCommand = new RelayCommand(OpenCacheFolder);
        _session.Changed += () => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    public string Status { get => _status; private set => SetField(ref _status, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenCacheFolderCommand { get; }

    public async Task RefreshAsync()
    {
        var cam = _session.Selected;
        if (cam is null)
        {
            Status = "Vyberte kameru. Rolling cache je izolovaná per MediaMTX path.";
            return;
        }

        var path = string.IsNullOrWhiteSpace(cam.MediaMtxPath) ? cam.CameraId : cam.MediaMtxPath;
        var recording = await _api.IsPathRecordingEnabledAsync(path);
        var ready = await _api.IsPathReadyAsync(path);
        var dir = Path.Combine(_config.PlatformRoot, "runtime", "recordings", path);
        var files = Directory.Exists(dir)
            ? Directory.GetFiles(dir)
            : Array.Empty<string>();
        long bytes = 0;
        DateTime? oldest = null, newest = null;
        foreach (var f in files)
        {
            var info = new FileInfo(f);
            bytes += info.Length;
            if (oldest is null || info.CreationTime < oldest) oldest = info.CreationTime;
            if (newest is null || info.LastWriteTime > newest) newest = info.LastWriteTime;
        }

        Status =
            $"Camera: {cam.DisplayName}\n" +
            $"Path: {path}\n" +
            $"Stream: {(ready == true ? "READY" : "NOT READY")}\n" +
            $"Recording allowed (registry): {cam.RecordingAllowed}\n" +
            $"MediaMTX record flag: {recording?.ToString() ?? "?"}\n" +
            $"Cache dir: runtime/recordings/{path}\n" +
            $"Segments: {files.Length}\n" +
            $"Cache size: {bytes / 1024.0 / 1024.0:F1} MB\n" +
            $"Oldest: {oldest?.ToString("HH:mm:ss") ?? "—"}\n" +
            $"Newest: {newest?.ToString("HH:mm:ss") ?? "—"}\n" +
            "(Incident clips are separate under runtime/incidents — not managed by recordDeleteAfter.)";
    }

    private void OpenCacheFolder()
    {
        var path = _session.MediaMtxPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        var dir = Path.Combine(_config.PlatformRoot, "runtime", "recordings", path);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }
}
