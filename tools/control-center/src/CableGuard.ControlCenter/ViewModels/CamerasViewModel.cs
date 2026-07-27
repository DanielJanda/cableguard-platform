using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

public sealed class CamerasViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly IMediaMtxApi _api;
    private readonly ICredentialStore _credentials;
    private readonly StreamSwitchService _switchService;

    private string _registryStatus = "";
    private string _switchProgress = "";
    private CameraRegistryDocument _registry = new();

    public CamerasViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        IMediaMtxApi api,
        ICredentialStore credentials,
        StreamSwitchService switchService)
    {
        _config = config;
        _logger = logger;
        _api = api;
        _credentials = credentials;
        _switchService = switchService;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        _ = ReloadAsync();
    }

    public ObservableCollection<CameraRowViewModel> Cameras { get; } = new();
    public string RegistryStatus { get => _registryStatus; private set => SetField(ref _registryStatus, value); }
    public string SwitchProgress { get => _switchProgress; private set => SetField(ref _switchProgress, value); }
    public AsyncRelayCommand ReloadCommand { get; }

    public async Task ReloadAsync()
    {
        Cameras.Clear();
        try
        {
            _registry = CameraRegistryService.Load(_config.CamerasJsonPath);
        }
        catch (InvalidOperationException ex)
        {
            RegistryStatus = ex.Message;
            return;
        }

        if (_registry.Cameras.Count == 0)
        {
            RegistryStatus = $"Registry je prázdná — vyplň {_config.CamerasJsonPath} (viz config/cameras.example.json).";
            return;
        }

        var primary = CameraRegistryService.ResolvePrimaryCamera(_registry, _config.ProductionStream);
        foreach (var camera in _registry.Cameras)
            Cameras.Add(new CameraRowViewModel(camera, camera.CameraId == primary?.CameraId, this));

        RegistryStatus = $"{_registry.Cameras.Count} kamer, produkční stream '{_config.ProductionStream}' → {primary?.DisplayName ?? "?"}";
        foreach (var row in Cameras)
            await row.RefreshAsync(_api);
    }

    public void Preview(CameraEntry camera) =>
        Process.Start(new ProcessStartInfo(_config.PreviewUrl(camera.MediaMtxPath)) { UseShellExecute = true });

    public async Task<string> TestConnectionAsync(CameraEntry camera)
    {
        // Honest MVP connectivity test: TCP reach of the RTSP port + MediaMTX path readiness.
        string tcpResult;
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(camera.Host, camera.RtspPort);
            tcpResult = await Task.WhenAny(connect, Task.Delay(3000)) == connect && client.Connected
                ? $"TCP {camera.Host}:{camera.RtspPort} OK"
                : $"TCP {camera.Host}:{camera.RtspPort} timeout";
        }
        catch (SocketException ex)
        {
            tcpResult = $"TCP {camera.Host}:{camera.RtspPort} failed ({ex.SocketErrorCode})";
        }
        var ready = await _api.IsPathReadyAsync(camera.MediaMtxPath);
        var pathResult = ready switch
        {
            true => $"path '{camera.MediaMtxPath}' READY",
            false => $"path '{camera.MediaMtxPath}' NOT ready",
            null => "MediaMTX API unreachable",
        };
        return $"{tcpResult}; {pathResult}";
    }

    public void ToggleEnabled(CameraEntry camera)
    {
        camera.Enabled = !camera.Enabled;
        CameraRegistryService.Save(_registry, _config.CamerasJsonPath);
        _logger.Info($"Camera {camera.CameraId} enabled={camera.Enabled}");
        _ = ReloadAsync();
    }

    public (string Username, bool HasPassword) ReadCredentialInfo(CameraEntry camera)
    {
        var found = _credentials.TryRead(camera.CredentialRef, out var user, out var pass);
        return (found ? user : "", found && pass.Length > 0);
    }

    public bool SaveCredentials(CameraEntry camera, string username, string password)
    {
        var ok = _credentials.Write(camera.CredentialRef, username, password);
        _logger.Info(ok
            ? $"Credentials updated for {camera.CameraId} (ref {camera.CredentialRef})."
            : $"Credential write FAILED for {camera.CameraId}.");
        return ok;
    }

    public async Task SetAsPrimaryAsync(CameraEntry camera)
    {
        var current = CameraRegistryService.ResolvePrimaryCamera(_registry, _config.ProductionStream);
        if (current?.CameraId == camera.CameraId)
        {
            MessageBox.Show("Tato kamera už je primární.", "Set as primary", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Set as primary source for:\n{_config.ProductionStream}\n\n" +
            $"Current: {current?.DisplayName ?? "?"} ({current?.Host ?? "?"})\n" +
            $"Switch to: {camera.DisplayName} ({camera.Host})\n\n" +
            "MediaMTX path se přepne za běhu; při selhání proběhne automatický rollback.\n" +
            "Detector konfigurace se nemění.",
            "Potvrdit přepnutí primární kamery",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        SwitchProgress = "";
        var progress = new Progress<string>(line =>
        {
            SwitchProgress += line + Environment.NewLine;
            _logger.Info($"[SWITCH] {line}");
        });

        var result = await _switchService.SwitchPrimaryAsync(
            _registry, _config.ProductionStream, camera.CameraId,
            saveRegistry: r => CameraRegistryService.Save(r, _config.CamerasJsonPath),
            progress: progress);

        var icon = result.Success ? MessageBoxImage.Information : MessageBoxImage.Error;
        var title = result.Success ? "Přepnutí úspěšné" : (result.RolledBack ? "Přepnutí selhalo — rollback" : "Přepnutí selhalo");
        MessageBox.Show(result.Message, title, MessageBoxButton.OK, icon);
        await ReloadAsync();
    }
}

public sealed class CameraRowViewModel : ObservableObject
{
    private readonly CamerasViewModel _parent;
    private string _status = "…";
    private string _testResult = "";

    public CameraRowViewModel(CameraEntry camera, bool isPrimary, CamerasViewModel parent)
    {
        Camera = camera;
        IsPrimary = isPrimary;
        _parent = parent;
        PreviewCommand = new RelayCommand(() => _parent.Preview(Camera));
        TestCommand = new AsyncRelayCommand(async () => TestResult = await _parent.TestConnectionAsync(Camera));
        ToggleEnabledCommand = new RelayCommand(() => _parent.ToggleEnabled(Camera));
        SetPrimaryCommand = new AsyncRelayCommand(() => _parent.SetAsPrimaryAsync(Camera), () => Camera.Enabled && !IsPrimary);
        EditCredentialsCommand = new RelayCommand(EditCredentials);
    }

    public CameraEntry Camera { get; }
    public bool IsPrimary { get; }
    public string Title => IsPrimary ? $"{Camera.DisplayName}  ★ PRIMARY" : Camera.DisplayName;
    public string Subtitle => $"IP: {Camera.Host}:{Camera.RtspPort}   MediaMTX path: {Camera.MediaMtxPath}   {(Camera.Enabled ? "" : "(disabled)")}";
    public string EnableLabel => Camera.Enabled ? "Disable" : "Enable";
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string TestResult { get => _testResult; private set => SetField(ref _testResult, value); }

    public RelayCommand PreviewCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public RelayCommand ToggleEnabledCommand { get; }
    public AsyncRelayCommand SetPrimaryCommand { get; }
    public RelayCommand EditCredentialsCommand { get; }

    public async Task RefreshAsync(IMediaMtxApi api)
    {
        if (!Camera.Enabled) { Status = "DISABLED"; return; }
        var ready = await api.IsPathReadyAsync(Camera.MediaMtxPath);
        Status = ready switch { true => "LIVE", false => "OFFLINE", null => "MediaMTX?" };
    }

    private void EditCredentials()
    {
        var (username, hasPassword) = _parent.ReadCredentialInfo(Camera);
        var dialog = new Views.CredentialsDialog(Camera.DisplayName, Camera.CredentialRef, username, hasPassword);
        if (dialog.ShowDialog() == true)
            _parent.SaveCredentials(Camera, dialog.EnteredUsername, dialog.EnteredPassword);
    }
}
