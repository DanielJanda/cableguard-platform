using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Session selection shared across Cameras / Detection / status bar.
/// Never stores credentials or full RTSP URLs.
/// </summary>
public sealed class SelectedCameraSession
{
    public event Action? Changed;

    public CameraEntry? Selected { get; private set; }

    public string CameraId => Selected?.CameraId ?? "";
    public string DisplayName => Selected?.DisplayName ?? "(žádná)";
    public string Environment =>
        string.IsNullOrWhiteSpace(Selected?.Environment) ? "production" : Selected!.Environment;
    public string MediaMtxPath =>
        string.IsNullOrWhiteSpace(Selected?.MediaMtxPath) ? "" : Selected!.MediaMtxPath;
    public string Backend =>
        string.IsNullOrWhiteSpace(Selected?.PreferredBackend)
            ? (Selected?.DetectorInputProfile ?? "pyav_rtsp")
            : Selected!.PreferredBackend;
    public string SourceMode =>
        string.IsNullOrWhiteSpace(Selected?.SourceMode) ? "mediamtx" : Selected!.SourceMode;

    public void Select(CameraEntry? camera)
    {
        Selected = camera;
        Changed?.Invoke();
    }

    public bool IsSelected(string cameraId) =>
        Selected is not null &&
        string.Equals(Selected.CameraId, cameraId, StringComparison.OrdinalIgnoreCase);
}
