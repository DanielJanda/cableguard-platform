using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using CableGuard.ControlCenter;

namespace CableGuard.ControlCenter.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly StartAllOrchestrator _orchestrator;
    private readonly DispatcherTimer _refreshTimer;
    private string _systemStatus = "…";
    private string _startAllProgress = "";
    private bool _busy;

    public MainViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        IReadOnlyList<IComponentController> components,
        AdminModeViewModel mode,
        CamerasViewModel cameras,
        StreamsViewModel streams,
        DetectorsViewModel detectors,
        CalibrationViewModel calibration,
        NotificationsViewModel notifications,
        HardwareViewModel hardware,
        ScenariosViewModel scenarios,
        VideoLabViewModel videoLab,
        LogsViewModel logs,
        SettingsViewModel settings)
    {
        _config = config;
        _logger = logger;
        Mode = mode;
        Cameras = cameras;
        Streams = streams;
        Detectors = detectors;
        Calibration = calibration;
        Notifications = notifications;
        Hardware = hardware;
        Scenarios = scenarios;
        VideoLab = videoLab;
        Logs = logs;
        Settings = settings;

        foreach (var component in components)
            Services.Add(new ServiceRowViewModel(component, logger, () => _ = RecalculateSystemStatusAsync()));

        _orchestrator = new StartAllOrchestrator(components, TimeSpan.FromSeconds(config.ReadinessTimeoutSeconds));
        StartAllCommand = new AsyncRelayCommand(StartAllAsync, () => !_busy);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync, () => !_busy);
        OpenDashboardCommand = new RelayCommand(() => OpenUrl(_config.DashboardUrl));
        OpenKioskCommand = new RelayCommand(() => OpenUrl(_config.KioskUrl));
        OpenTestLabCommand = new RelayCommand(() => Mode.TestLabCommand.Execute(null));
        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);
        StartDetectorPreviewCommand = new AsyncRelayCommand(StartDetectorPreviewAsync);
        StartOfficeFallNoDebugCommand = new AsyncRelayCommand(() => StartOfficeFallAsync(debug: false));
        StartOfficeFallDebugCommand = new AsyncRelayCommand(() => StartOfficeFallAsync(debug: true));
        StopOfficeFallCommand = new AsyncRelayCommand(StopOfficeFallAsync);
        RestartOfficeFallCommand = new AsyncRelayCommand(RestartOfficeFallAsync);
        OpenOfficeE2eMonitorCommand = new RelayCommand(() => OpenUrl($"http://{_config.LanHost}:8080/test-lab/office-fall"));
        OpenOfficeStreamPreviewCommand = new RelayCommand(() => OpenUrl($"http://{_config.LanHost}:8080/test-lab/stream/office-test-camera"));

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_busy) return;
            _ = RefreshAllSafeAsync();
        };
        _refreshTimer.Start();
        _ = RefreshAllSafeAsync();
    }

    private async Task RefreshAllSafeAsync()
    {
        try { await RefreshAllAsync(); }
        catch (Exception ex) { _logger.Warn($"Refresh failed: {ex.Message}"); }
    }

    public AdminModeViewModel Mode { get; }
    public ObservableCollection<ServiceRowViewModel> Services { get; } = new();
    public CamerasViewModel Cameras { get; }
    public StreamsViewModel Streams { get; }
    public DetectorsViewModel Detectors { get; }
    public CalibrationViewModel Calibration { get; }
    public NotificationsViewModel Notifications { get; }
    public HardwareViewModel Hardware { get; }
    public ScenariosViewModel Scenarios { get; }
    public VideoLabViewModel VideoLab { get; }
    public LogsViewModel Logs { get; }
    public SettingsViewModel Settings { get; }

    public string SystemStatus { get => _systemStatus; private set => SetField(ref _systemStatus, value); }
    public string StartAllProgress { get => _startAllProgress; private set => SetField(ref _startAllProgress, value); }
    public string BuildStamp => BuildInfo.Summary;
    public string PlatformRootHint => _config.PlatformRoot;

    public AsyncRelayCommand StartAllCommand { get; }
    public AsyncRelayCommand StopAllCommand { get; }
    public RelayCommand OpenDashboardCommand { get; }
    public RelayCommand OpenKioskCommand { get; }
    public RelayCommand OpenTestLabCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand StartDetectorPreviewCommand { get; }
    public AsyncRelayCommand StartOfficeFallNoDebugCommand { get; }
    public AsyncRelayCommand StartOfficeFallDebugCommand { get; }
    public AsyncRelayCommand StopOfficeFallCommand { get; }
    public AsyncRelayCommand RestartOfficeFallCommand { get; }
    public RelayCommand OpenOfficeE2eMonitorCommand { get; }
    public RelayCommand OpenOfficeStreamPreviewCommand { get; }

    public async Task RefreshAllAsync()
    {
        foreach (var row in Services) await row.RefreshAsync();
        Detectors.RefreshStatuses();
        await RecalculateSystemStatusAsync();
    }

    private Task RecalculateSystemStatusAsync()
    {
        var snapshots = Services.Select(s => s.LastSnapshot).Where(s => s is not null).Cast<ComponentSnapshot>().ToList();
        SystemStatus = snapshots.Count == 0 ? "…" : SystemStatusCalculator.Calculate(snapshots) switch
        {
            Core.Models.SystemStatus.Ready => "PŘIPRAVENO",
            Core.Models.SystemStatus.Degraded => "ZHORŠENO",
            Core.Models.SystemStatus.Stopped => "ZASTAVENO",
            _ => "PORUCHA",
        };
        return Task.CompletedTask;
    }

    private async Task StartAllAsync()
    {
        _busy = true;
        StartAllProgress = "";
        var progress = new Progress<string>(line =>
        {
            StartAllProgress += line + Environment.NewLine;
            _logger.Info($"[START ALL] {line}");
        });
        try
        {
            var result = await Task.Run(() => _orchestrator.StartAllAsync(progress));
            if (!result.Success && result.FailedAt is not null)
                MessageBox.Show($"FAILED AT: {result.FailedAt}\n\n{result.Steps.LastOrDefault()?.Message}",
                    "START ALL failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _busy = false; await RefreshAllAsync(); }
    }

    private async Task StopAllAsync()
    {
        if (MessageBox.Show("Zastavit celý CableGuard stack?", "STOP ALL",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _busy = true;
        try
        {
            foreach (var row in Services.Reverse())
            {
                if (!row.Component.IsConfigured) continue;
                await row.Component.StopAsync();
            }
        }
        finally { _busy = false; await RefreshAllAsync(); }
    }

    private async Task StartDetectorPreviewAsync()
    {
        var fall = Detectors.PrimaryFallDetector;
        if (fall is null)
        {
            MessageBox.Show(
                "Není nakonfigurován fall detektor.\nZáložka Detektory → zkontrolujte detectors.json.",
                "Náhled detekce", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await Detectors.OpenDebugAsync(fall);
        await RefreshAllAsync();
    }

    private DetectorInstance? FindOfficeFall() =>
        Detectors.Items.Select(r => r.Instance).FirstOrDefault(i =>
            string.Equals(i.Id, "fall-office-test", StringComparison.OrdinalIgnoreCase));

    private async Task StartOfficeFallAsync(bool debug)
    {
        var office = FindOfficeFall();
        if (office is null)
        {
            MessageBox.Show("Instance fall-office-test není v detectors.json.", "Office fall",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Independent start: test_mode path via scenario flags — no Telegram, no relay auto.
        office.Enabled = true;
        office.DebugOverlay = debug;
        office.PublishEventCore = true;
        Notifications.TelegramEnabled = false;
        _logger.Info($"[GUI] Start fall-office-test debug={debug} telegram=OFF relay_auto=OFF");
        await Detectors.StartAsync(office, debug);
        StartAllProgress += $"fall-office-test started (debug={debug}, test_mode expected, telegram off){Environment.NewLine}";
        await RefreshAllAsync();
    }

    private async Task StopOfficeFallAsync()
    {
        var office = FindOfficeFall();
        if (office is null) return;
        _logger.Info("[GUI] Stop fall-office-test only");
        await Detectors.StopAsync(office);
        await RefreshAllAsync();
    }

    private async Task RestartOfficeFallAsync()
    {
        var office = FindOfficeFall();
        var debug = office?.DebugOverlay ?? false;
        await StopOfficeFallAsync();
        await Task.Delay(1500);
        await StartOfficeFallAsync(debug);
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
