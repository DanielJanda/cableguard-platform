using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

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
        CamerasViewModel cameras,
        LogsViewModel logs,
        SettingsViewModel settings)
    {
        _config = config;
        _logger = logger;
        Cameras = cameras;
        Logs = logs;
        Settings = settings;

        foreach (var component in components)
            Services.Add(new ServiceRowViewModel(component, logger, () => _ = RecalculateSystemStatusAsync()));

        _orchestrator = new StartAllOrchestrator(
            components, TimeSpan.FromSeconds(config.ReadinessTimeoutSeconds));

        StartAllCommand = new AsyncRelayCommand(StartAllAsync, () => !_busy);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync, () => !_busy);
        OpenDashboardCommand = new RelayCommand(() => OpenUrl(_config.DashboardUrl));
        OpenKioskCommand = new RelayCommand(() => OpenUrl(_config.KioskUrl));
        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) => { if (!_busy) await RefreshAllAsync(); };
        _refreshTimer.Start();
        _ = RefreshAllAsync();
    }

    public ObservableCollection<ServiceRowViewModel> Services { get; } = new();
    public CamerasViewModel Cameras { get; }
    public LogsViewModel Logs { get; }
    public SettingsViewModel Settings { get; }

    public string SystemStatus { get => _systemStatus; private set => SetField(ref _systemStatus, value); }
    public string StartAllProgress { get => _startAllProgress; private set => SetField(ref _startAllProgress, value); }

    public AsyncRelayCommand StartAllCommand { get; }
    public AsyncRelayCommand StopAllCommand { get; }
    public RelayCommand OpenDashboardCommand { get; }
    public RelayCommand OpenKioskCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public async Task RefreshAllAsync()
    {
        foreach (var row in Services)
            await row.RefreshAsync();
        await RecalculateSystemStatusAsync();
    }

    private Task RecalculateSystemStatusAsync()
    {
        var snapshots = Services
            .Select(s => s.LastSnapshot)
            .Where(s => s is not null)
            .Cast<ComponentSnapshot>()
            .ToList();
        SystemStatus = snapshots.Count == 0 ? "…" : SystemStatusCalculator.Calculate(snapshots) switch
        {
            Core.Models.SystemStatus.Ready => "READY",
            Core.Models.SystemStatus.Degraded => "DEGRADED",
            Core.Models.SystemStatus.Stopped => "STOPPED",
            _ => "FAULT",
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
                MessageBox.Show(
                    $"FAILED AT: {result.FailedAt}\n\n{result.Steps.LastOrDefault()?.Message}",
                    "START ALL failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            await RefreshAllAsync();
        }
    }

    private async Task StopAllAsync()
    {
        if (MessageBox.Show("Zastavit celý CableGuard stack?", "STOP ALL",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _busy = true;
        try
        {
            // Reverse dependency order: detector → monitor → event core → mediamtx.
            foreach (var row in Services.Reverse())
            {
                if (!row.Component.IsConfigured) continue;
                _logger.Info($"[STOP ALL] Stopping {row.Name}...");
                await row.Component.StopAsync();
            }
        }
        finally
        {
            _busy = false;
            await RefreshAllAsync();
        }
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
