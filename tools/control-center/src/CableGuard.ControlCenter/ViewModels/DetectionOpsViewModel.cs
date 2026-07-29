using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

/// <summary>Detection tab — operates on SelectedCameraSession.</summary>
public sealed class DetectionOpsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly SelectedCameraSession _session;
    private readonly DetectorsViewModel _detectors;
    private readonly NotificationsViewModel _notifications;
    private readonly IMediaMtxApi _api;
    private readonly DispatcherTimer _timer;

    private string _summary = "Vyberte kameru na záložce Cameras.";
    private string _healthDetail = "";
    private string _lastLaunchSummary = "";

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
        StartCommand = new AsyncRelayCommand(() => StartAsync(debug: false));
        StopCommand = new AsyncRelayCommand(StopAsync);
        RestartCommand = new AsyncRelayCommand(RestartAsync);
        OpenDebugCommand = new AsyncRelayCommand(() => StartAsync(debug: true));
        CloseDebugCommand = new AsyncRelayCommand(StopAsync);
        _session.Changed += RefreshSummary;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => RefreshSummary();
        _timer.Start();
        RefreshSummary();
    }

    public string Summary { get => _summary; private set => SetField(ref _summary, value); }
    public string HealthDetail { get => _healthDetail; private set => SetField(ref _healthDetail, value); }
    public string LastLaunchSummary { get => _lastLaunchSummary; private set => SetField(ref _lastLaunchSummary, value); }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand OpenDebugCommand { get; }
    public AsyncRelayCommand CloseDebugCommand { get; }

    public void RefreshSummary()
    {
        var cam = _session.Selected;
        if (cam is null)
        {
            Summary = "Vyberte kameru na záložce Cameras.";
            HealthDetail = "";
            return;
        }

        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(cam, docs);
        var row = _detectors.Items.FirstOrDefault(r => r.Instance.Id == instance.Id);
        row?.Refresh();
        var running = string.Equals(row?.Status, "BĚŽÍ", StringComparison.OrdinalIgnoreCase);
        var pid = row?.Detail ?? "";

        Summary =
            $"Selected: {cam.DisplayName} ({cam.CameraId})\n" +
            $"Environment: {cam.Environment.ToUpperInvariant()}\n" +
            $"Backend: {instance.InputProfile} · source_mode={instance.SourceMode}\n" +
            $"MediaMTX path: {instance.InputStream}\n" +
            $"Model: {instance.Model} · Device: {instance.Device}\n" +
            $"State: {(running ? "RUNNING" : "STOPPED")} {pid}";

        // Pull Overview detector detail when available (video_input health).
        HealthDetail = row?.Detail ?? "";
        foreach (var svc in Enumerable.Empty<string>()) { _ = svc; }
    }

    public async Task StartAsync(bool debug)
    {
        var cam = _session.Selected;
        if (cam is null)
        {
            MessageBox.Show("Nejdřív vyberte kameru (SELECT).", "Detection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var path = string.IsNullOrWhiteSpace(cam.MediaMtxPath) ? cam.CameraId : cam.MediaMtxPath;
        var ready = await _api.IsPathReadyAsync(path);
        if (ready != true)
        {
            MessageBox.Show(
                $"MediaMTX path '{path}' není READY.\nNejdřív ověřte stream (TEST CONNECTION / Apply MTX).",
                "Detection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(cam, docs);
        var summary = CameraDetectionLauncher.BuildSafeSummary(cam, instance);
        var text = CameraDetectionLauncher.FormatOperatorSummary(summary);
        if (text.Contains("rtsp://", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Interní chyba: souhrn obsahuje citlivá data.", "Security", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var confirm = MessageBox.Show(
            "Spustit detekci?\n\n" + text + (debug ? "\n\nDEBUG OVERLAY = ON (servisní režim)" : "\n\nProdukční běh bez debug okna."),
            "START DETECTION", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        // Persist resolved instance so Detectors tab stays in sync.
        try
        {
            var known = StreamsService.Load(_config.StreamsJsonPath).Streams.Select(s => s.StreamId);
            // Ensure stream id exists for validation
            if (!known.Contains(instance.InputStream, StringComparer.OrdinalIgnoreCase))
            {
                var streams = StreamsService.Load(_config.StreamsJsonPath);
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
            DetectorLaunchBuilder.Save(docs, _config.DetectorsJsonPath,
                StreamsService.Load(_config.StreamsJsonPath).Streams.Select(s => s.StreamId));
        }
        catch (Exception ex)
        {
            _logger.Warn($"[DETECTION] persist instance: {ex.Message}");
        }

        instance.DebugOverlay = debug;
        if (string.Equals(cam.Environment, "test", StringComparison.OrdinalIgnoreCase))
        {
            instance.PublishTelegram = false;
            _notifications.TelegramEnabled = false;
        }

        await _detectors.ReloadAsync();
        var refreshed = _detectors.Items.Select(i => i.Instance)
            .FirstOrDefault(i => i.Id == instance.Id) ?? instance;
        refreshed.DebugOverlay = debug;
        _logger.Info($"[DETECTION] START camera={cam.CameraId} profile={refreshed.InputProfile} source={refreshed.SourceMode} path={refreshed.InputStream} debug={debug}");
        await _detectors.StartAsync(refreshed, debug);
        LastLaunchSummary = text;
        RefreshSummary();
    }

    public async Task StopAsync()
    {
        var cam = _session.Selected;
        if (cam is null) return;
        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(cam, docs);
        var row = _detectors.Items.Select(i => i.Instance).FirstOrDefault(i => i.Id == instance.Id);
        if (row is not null)
            await _detectors.StopAsync(row);
        RefreshSummary();
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await Task.Delay(1500);
        await StartAsync(debug: false);
    }

    public void OpenMonitor()
    {
        var origin = _config.ResolvedPublicOrigin.TrimEnd('/');
        var url = string.Equals(_session.Environment, "test", StringComparison.OrdinalIgnoreCase)
            ? $"{origin}/test-lab/office-fall"
            : _config.DashboardUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
