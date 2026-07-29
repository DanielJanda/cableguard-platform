using System.Windows.Media;
using System.Windows.Threading;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

/// <summary>Always-visible operations status strip (text + color + icon glyph).</summary>
public sealed class OperationsStatusBarViewModel : ObservableObject
{
    private readonly SelectedCameraSession _session;
    private readonly Func<IReadOnlyList<ServiceRowViewModel>> _services;
    private readonly Func<DetectorsViewModel> _detectors;
    private readonly IMediaMtxApi _api;
    private readonly DispatcherTimer _timer;

    private string _environmentText = "PRODUCTION";
    private Brush _environmentBrush = Brushes.Gray;
    private string _cameraText = "Kamera: (žádná)";
    private string _backendText = "Backend: —";
    private string _detectorText = "● Detector: —";
    private Brush _detectorBrush = Brushes.Gray;
    private string _mediaMtxText = "● MediaMTX: —";
    private Brush _mediaMtxBrush = Brushes.Gray;
    private string _recordingText = "● Recording: —";
    private Brush _recordingBrush = Brushes.Gray;
    private string _eventCoreText = "● Event Core: —";
    private Brush _eventCoreBrush = Brushes.Gray;

    public OperationsStatusBarViewModel(
        SelectedCameraSession session,
        Func<IReadOnlyList<ServiceRowViewModel>> services,
        Func<DetectorsViewModel> detectors,
        IMediaMtxApi api)
    {
        _session = session;
        _services = services;
        _detectors = detectors;
        _api = api;
        _session.Changed += () => _ = RefreshAsync();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public string EnvironmentText { get => _environmentText; private set => SetField(ref _environmentText, value); }
    public Brush EnvironmentBrush { get => _environmentBrush; private set => SetField(ref _environmentBrush, value); }
    public string CameraText { get => _cameraText; private set => SetField(ref _cameraText, value); }
    public string BackendText { get => _backendText; private set => SetField(ref _backendText, value); }
    public string DetectorText { get => _detectorText; private set => SetField(ref _detectorText, value); }
    public Brush DetectorBrush { get => _detectorBrush; private set => SetField(ref _detectorBrush, value); }
    public string MediaMtxText { get => _mediaMtxText; private set => SetField(ref _mediaMtxText, value); }
    public Brush MediaMtxBrush { get => _mediaMtxBrush; private set => SetField(ref _mediaMtxBrush, value); }
    public string RecordingText { get => _recordingText; private set => SetField(ref _recordingText, value); }
    public Brush RecordingBrush { get => _recordingBrush; private set => SetField(ref _recordingBrush, value); }
    public string EventCoreText { get => _eventCoreText; private set => SetField(ref _eventCoreText, value); }
    public Brush EventCoreBrush { get => _eventCoreBrush; private set => SetField(ref _eventCoreBrush, value); }

    public async Task RefreshAsync()
    {
        var sel = _session.Selected;
        var isTest = string.Equals(_session.Environment, "test", StringComparison.OrdinalIgnoreCase);
        EnvironmentText = isTest ? "◆ TEST MODE" : "● PRODUCTION";
        EnvironmentBrush = isTest
            ? new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5))
            : new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));

        CameraText = sel is null
            ? "Kamera: (žádná)"
            : $"Kamera: {sel.DisplayName}";
        BackendText = sel is null ? "Backend: —" : $"Backend: {_session.Backend} / {_session.SourceMode}";

        var services = _services();
        ApplyService("MediaMTX", services, (t, b) => { MediaMtxText = t; MediaMtxBrush = b; });
        ApplyService("Event Core", services, (t, b) => { EventCoreText = t; EventCoreBrush = b; }, optionalGreyWhenStopped: true);

        // Detector for selected camera (or primary fall)
        var detVm = _detectors();
        DetectorInstance? instance = null;
        if (sel is not null)
        {
            var path = string.IsNullOrWhiteSpace(sel.MediaMtxPath) ? sel.CameraId : sel.MediaMtxPath;
            instance = detVm.Items.Select(i => i.Instance)
                .FirstOrDefault(i => i.DetectorType == "fall" &&
                                     string.Equals(i.InputStream, path, StringComparison.OrdinalIgnoreCase));
        }
        instance ??= detVm.PrimaryFallDetector;

        if (instance is null)
        {
            DetectorText = "○ Detector: není nakonfigurován";
            DetectorBrush = Brushes.Gray;
        }
        else
        {
            var row = detVm.Items.FirstOrDefault(r => r.Instance.Id == instance.Id);
            var running = string.Equals(row?.Status, "BĚŽÍ", StringComparison.OrdinalIgnoreCase);
            DetectorText = running
                ? $"● Detector: RUNNING ({instance.InputProfile}/{instance.SourceMode})"
                : "○ Detector: STOPPED";
            DetectorBrush = running
                ? new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A))
                : new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
        }

        // Recording — only for selected path when recording_allowed
        if (sel is null || !sel.RecordingAllowed || string.IsNullOrWhiteSpace(_session.MediaMtxPath))
        {
            RecordingText = "○ Recording: není požadováno";
            RecordingBrush = Brushes.Gray;
        }
        else
        {
            try
            {
                var on = await _api.IsPathRecordingEnabledAsync(_session.MediaMtxPath);
                RecordingText = on switch
                {
                    true => "● Recording: ON",
                    false => "○ Recording: OFF",
                    _ => "● Recording: ?",
                };
                RecordingBrush = on switch
                {
                    true => new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),
                    false => new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D)),
                    _ => new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D)),
                };
            }
            catch
            {
                RecordingText = "● Recording: ?";
                RecordingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D));
            }
        }
    }

    private static void ApplyService(
        string containsName,
        IReadOnlyList<ServiceRowViewModel> services,
        Action<string, Brush> assign,
        bool optionalGreyWhenStopped = false)
    {
        var row = services.FirstOrDefault(s =>
            s.Name.Contains(containsName, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            assign($"○ {containsName}: —", Brushes.Gray);
            return;
        }

        var st = row.Status ?? "";
        var ready = st is "BĚŽÍ" or "RUNNING" or "ŽIVĚ" or "LIVE" or "READY" or "PŘIPRAVENO";
        var fault = st is "PORUCHA" or "FAULT" or "OFFLINE";
        var degraded = st is "ZHORŠENO";
        if (ready)
            assign($"● {containsName}: READY", new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)));
        else if (degraded)
            assign($"● {containsName}: DEGRADED", new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D)));
        else if (fault)
            assign($"● {containsName}: FAILED", new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)));
        else if (optionalGreyWhenStopped)
            assign($"○ {containsName}: STOPPED", Brushes.Gray);
        else
            assign($"● {containsName}: STOPPED", new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)));
    }
}
