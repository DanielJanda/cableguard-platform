using System.Diagnostics;
using System.IO;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

/// <summary>One row on the Services / Overview tab.</summary>
public sealed class ServiceRowViewModel : ObservableObject
{
    private readonly IComponentController _component;
    private readonly ControlCenterLogger _logger;
    private readonly Action _refreshAll;

    private string _status = "…";
    private string _detail = "";

    public ServiceRowViewModel(IComponentController component, ControlCenterLogger logger, Action refreshAll)
    {
        _component = component;
        _logger = logger;
        _refreshAll = refreshAll;
        StartCommand = new AsyncRelayCommand(StartAsync, () => _component.IsConfigured);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _component.IsConfigured);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => _component.IsConfigured);
        LogsCommand = new RelayCommand(OpenLogFolder);
    }

    public IComponentController Component => _component;
    public string Name => _component.DisplayName;
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string Detail { get => _detail; private set => SetField(ref _detail, value); }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public RelayCommand LogsCommand { get; }

    public ComponentSnapshot? LastSnapshot { get; private set; }

    public async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _component.GetStatusAsync();
            LastSnapshot = snapshot;
            Status = StatusLabel(snapshot.Status);
            Detail = snapshot.Detail;
        }
        catch (Exception ex)
        {
            Status = "FAULT";
            Detail = $"Status check failed: {ex.Message}";
        }
    }

    private static string StatusLabel(ComponentStatus status) => status switch
    {
        ComponentStatus.Running => "RUNNING",
        ComponentStatus.Stopped => "STOPPED",
        ComponentStatus.Starting => "STARTING",
        ComponentStatus.Degraded => "DEGRADED",
        ComponentStatus.Fault => "FAULT",
        ComponentStatus.NotConfigured => "NOT CONFIGURED",
        _ => "UNKNOWN",
    };

    private async Task StartAsync()
    {
        _logger.Info($"[GUI] Start {_component.DisplayName}");
        var result = await _component.StartAsync();
        if (!result.Success) _logger.Error($"{_component.DisplayName} start failed: {result.Message}");
        await RefreshAsync();
        _refreshAll();
    }

    private async Task StopAsync()
    {
        _logger.Info($"[GUI] Stop {_component.DisplayName}");
        var result = await _component.StopAsync();
        if (!result.Success) _logger.Error($"{_component.DisplayName} stop failed: {result.Message}");
        await RefreshAsync();
        _refreshAll();
    }

    private async Task RestartAsync()
    {
        await StopAsync();
        await Task.Delay(1500);
        await StartAsync();
    }

    private void OpenLogFolder()
    {
        var dir = Path.GetDirectoryName(_component.LogFilePath);
        if (dir is not null && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }
}
