using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

/// <summary>
/// Detection tab — office camera preselected.
/// Primary START opens OpenCV preview (what operators expect in the lab).
/// </summary>
public sealed class DetectionOpsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly SelectedCameraSession _session;
    private readonly DetectorsViewModel _detectors;
    private readonly NotificationsViewModel _notifications;
    private readonly IMediaMtxApi _api;
    private readonly DispatcherTimer _timer;

    private CameraEntry? _selectedCamera;
    private string _summary = "";
    private string _statusLine = "STOPPED";

    public DetectionOpsViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        SelectedCameraSession session,
        DetectorsViewModel detectors,
        NotificationsViewModel notifications,
        IMediaMtxApi api)
    {
        _config = config;
        _logger = logger;
        _session = session;
        _detectors = detectors;
        _notifications = notifications;
        _api = api;

        Cameras = new ObservableCollection<CameraEntry>();
        ReloadCameras();
        EnsureDefaultOfficeCamera();

        // Primary = with OpenCV window (lab). Headless is secondary.
        StartWithPreviewCommand = new AsyncRelayCommand(() => StartAsync(withPreview: true));
        StartHeadlessCommand = new AsyncRelayCommand(() => StartAsync(withPreview: false));
        StopCommand = new AsyncRelayCommand(StopAsync);
        RestartCommand = new AsyncRelayCommand(RestartAsync);

        _session.Changed += () =>
        {
            if (_session.Selected is not null && !ReferenceEquals(_selectedCamera, _session.Selected))
            {
                _selectedCamera = _session.Selected;
                OnPropertyChanged(nameof(SelectedCamera));
            }
            RefreshSummary();
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshSummary();
        _timer.Start();
        RefreshSummary();
    }

    public ObservableCollection<CameraEntry> Cameras { get; }

    public CameraEntry? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (!SetField(ref _selectedCamera, value) || value is null) return;
            _session.Select(value);
            RefreshSummary();
        }
    }

    public string Summary { get => _summary; private set => SetField(ref _summary, value); }
    public string StatusLine { get => _statusLine; private set => SetField(ref _statusLine, value); }

    public AsyncRelayCommand StartWithPreviewCommand { get; }
    public AsyncRelayCommand StartHeadlessCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }

    // Compat aliases used by older bindings / MainViewModel refresh hooks.
    public AsyncRelayCommand StartCommand => StartWithPreviewCommand;
    public AsyncRelayCommand OpenDebugCommand => StartWithPreviewCommand;
    public AsyncRelayCommand CloseDebugCommand => StopCommand;

    public void ReloadCameras()
    {
        var registry = CameraRegistryService.Load(_config.CamerasJsonPath);
        Cameras.Clear();
        foreach (var c in registry.Cameras.Where(c => c.Enabled))
            Cameras.Add(c);
    }

    public void EnsureDefaultOfficeCamera()
    {
        if (_session.Selected is not null)
        {
            SelectedCamera = Cameras.FirstOrDefault(c =>
                string.Equals(c.CameraId, _session.Selected.CameraId, StringComparison.OrdinalIgnoreCase))
                ?? _session.Selected;
            return;
        }

        var office = Cameras.FirstOrDefault(c =>
            string.Equals(c.CameraId, OfficeCameraBootstrap.OfficeCameraId, StringComparison.OrdinalIgnoreCase))
            ?? Cameras.FirstOrDefault(c =>
                string.Equals(c.MediaMtxPath, OfficeCameraBootstrap.OfficePath, StringComparison.OrdinalIgnoreCase));
        if (office is not null)
            SelectedCamera = office;
    }

    public void RefreshSummary()
    {
        var cam = SelectedCamera ?? _session.Selected;
        if (cam is null)
        {
            Summary = "Žádná kamera — doplňte Cameras.";
            StatusLine = "STOPPED";
            return;
        }

        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(cam, docs);
        ForceOfficePyAv(instance, cam);
        var row = _detectors.Items.FirstOrDefault(r => r.Instance.Id == instance.Id);
        row?.Refresh();
        var running = string.Equals(row?.Status, "BĚŽÍ", StringComparison.OrdinalIgnoreCase);
        StatusLine = running
            ? $"BĚŽÍ {row?.Detail} — náhled = samostatné OpenCV okno (hlavní panel Windows)"
            : "STOPPED";
        Summary =
            $"{cam.DisplayName}\n" +
            $"{instance.InputProfile} / {instance.SourceMode} / {instance.InputStream}\n" +
            $"{instance.Model} · {instance.Device}";
    }

    public async Task StartAsync(bool withPreview)
    {
        EnsureDefaultOfficeCamera();
        var cam = SelectedCamera ?? _session.Selected;
        if (cam is null)
        {
            MessageBox.Show("Není k dispozici žádná kamera.", "Detection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var path = string.IsNullOrWhiteSpace(cam.MediaMtxPath) ? cam.CameraId : cam.MediaMtxPath;
        var ready = await _api.IsPathReadyAsync(path);
        if (ready != true)
        {
            MessageBox.Show($"MediaMTX path '{path}' není READY.", "Detection",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(cam, docs);
        ForceOfficePyAv(instance, cam);
        instance.Enabled = true;
        instance.DebugOverlay = withPreview;
        instance.PublishTelegram = false;
        _notifications.TelegramEnabled = false;

        try
        {
            EnsureStream(cam, instance, docs);
            DetectorLaunchBuilder.Save(docs, _config.DetectorsJsonPath,
                StreamsService.Load(_config.StreamsJsonPath).Streams.Select(s => s.StreamId));
        }
        catch (Exception ex)
        {
            _logger.Warn($"[DETECTION] persist: {ex.Message}");
        }

        await _detectors.ReloadAsync();
        var live = _detectors.Items.Select(i => i.Instance).FirstOrDefault(i => i.Id == instance.Id) ?? instance;
        ForceOfficePyAv(live, cam);
        live.DebugOverlay = withPreview;
        live.PublishTelegram = false;
        live.Enabled = true;

        // Restart if already running so we can switch headless ↔ preview cleanly.
        if (_detectors.Items.FirstOrDefault(r => r.Instance.Id == live.Id) is { } existing)
        {
            existing.Refresh();
            if (string.Equals(existing.Status, "BĚŽÍ", StringComparison.OrdinalIgnoreCase))
            {
                await _detectors.StopAsync(live, reload: false);
                await Task.Delay(1200);
            }
        }

        _logger.Info($"[DETECTION] START camera={cam.CameraId} preview={withPreview} profile={live.InputProfile} path={live.InputStream}");
        if (withPreview)
            await _detectors.OpenDebugAsync(live);
        else
            await _detectors.StartAsync(live, debug: false);

        await Task.Delay(2000);
        RefreshSummary();
        if (!StatusLine.StartsWith("BĚŽÍ", StringComparison.OrdinalIgnoreCase))
        {
            var errLog = System.IO.Path.Combine(_config.LogsDir, "detectors", $"{live.Id}.err.log");
            MessageBox.Show($"Detektor se nespustil.\n{errLog}", "Detection",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task StopAsync()
    {
        var cam = SelectedCamera ?? _session.Selected;
        if (cam is null) return;
        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(cam, docs);
        var live = _detectors.Items.Select(i => i.Instance).FirstOrDefault(i => i.Id == instance.Id);
        if (live is not null)
            await _detectors.StopAsync(live);
        RefreshSummary();
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await Task.Delay(1000);
        await StartAsync(withPreview: true);
    }

    public void OpenMonitor()
    {
        var origin = _config.ResolvedPublicOrigin.TrimEnd('/');
        var url = string.Equals(_session.Environment, "test", StringComparison.OrdinalIgnoreCase)
            ? $"{origin}/test-lab/office-fall"
            : _config.DashboardUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void ForceOfficePyAv(DetectorInstance instance, CameraEntry cam)
    {
        if (!string.Equals(instance.Id, "fall-office-test", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(cam.CameraId, OfficeCameraBootstrap.OfficeCameraId, StringComparison.OrdinalIgnoreCase))
            return;

        instance.InputProfile = "pyav_rtsp";
        instance.SourceMode = "mediamtx";
        instance.InputStream = OfficeCameraBootstrap.OfficePath;
        if (string.IsNullOrWhiteSpace(instance.Model))
            instance.Model = "yolo11m-pose.pt";
        if (string.IsNullOrWhiteSpace(instance.Device))
            instance.Device = "cuda:0";
    }

    private void EnsureStream(CameraEntry cam, DetectorInstance instance, DetectorsDocument docs)
    {
        var streams = StreamsService.Load(_config.StreamsJsonPath);
        if (!streams.Streams.Any(s => string.Equals(s.StreamId, instance.InputStream, StringComparison.OrdinalIgnoreCase)))
        {
            streams.Streams.Add(new LogicalStream
            {
                StreamId = instance.InputStream,
                DisplayName = cam.DisplayName,
                MediaMtxPath = instance.InputStream,
                CameraId = cam.CameraId,
                Enabled = true,
                IsProduction = !string.Equals(cam.Environment, "test", StringComparison.OrdinalIgnoreCase),
            });
            StreamsService.Save(streams, _config.StreamsJsonPath, docs.Instances.Select(_ => cam.CameraId).Append(cam.CameraId));
        }
    }
}
