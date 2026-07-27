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
        AddCommand = new RelayCommand(AddCamera);
        _ = ReloadAsync();
    }

    public ObservableCollection<CameraRowViewModel> Cameras { get; } = new();
    public string RegistryStatus { get => _registryStatus; private set => SetField(ref _registryStatus, value); }
    public string SwitchProgress { get => _switchProgress; private set => SetField(ref _switchProgress, value); }
    public AsyncRelayCommand ReloadCommand { get; }
    public RelayCommand AddCommand { get; }
    public CameraRegistryDocument Registry => _registry;

    public async Task ReloadAsync()
    {
        Cameras.Clear();
        try { _registry = CameraRegistryService.Load(_config.CamerasJsonPath); }
        catch (InvalidOperationException ex) { RegistryStatus = ex.Message; return; }

        if (_registry.Cameras.Count == 0)
        {
            RegistryStatus = $"Registry prázdná — Add Camera nebo zkopíruj config/cameras.example.json → {_config.CamerasJsonPath}";
            return;
        }

        var primary = CameraRegistryService.ResolvePrimaryCamera(_registry, _config.ProductionStream);
        foreach (var camera in _registry.Cameras)
            Cameras.Add(new CameraRowViewModel(camera, camera.CameraId == primary?.CameraId, this));

        RegistryStatus = $"{_registry.Cameras.Count} kamer, produkční stream '{_config.ProductionStream}' → {primary?.DisplayName ?? "?"}";
        foreach (var row in Cameras)
            await row.RefreshAsync(_api);
    }

    public void Persist()
    {
        CameraRegistryService.Save(_registry, _config.CamerasJsonPath);
        _logger.Info("[CAMERAS] Saved cameras.json");
    }

    private void AddCamera()
    {
        var cam = new CameraEntry
        {
            CameraId = $"camera-{DateTime.Now:HHmmss}",
            DisplayName = "Nová kamera",
            SiteId = "office",
            StationId = "test",
            Host = "10.6.1.123",
            RtspPort = 554,
            Profile = "Streaming/Channels/102",
            Transport = "tcp",
            CredentialRef = "CableGuard.Camera.new",
            MediaMtxPath = "office-test",
            Enabled = true,
        };
        EditCamera(cam, isNew: true);
    }

    public void EditCamera(CameraEntry camera, bool isNew = false)
    {
        var dlg = new Views.CameraEditDialog(camera);
        if (dlg.ShowDialog() != true) return;
        var edited = dlg.Result;
        var errors = CameraRegistryService.ValidateCamera(edited);
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (isNew)
        {
            if (_registry.Cameras.Any(c => c.CameraId == edited.CameraId))
            {
                MessageBox.Show("camera_id už existuje.");
                return;
            }
            _registry.Cameras.Add(edited);
        }
        else
        {
            var idx = _registry.Cameras.FindIndex(c => c.CameraId == camera.CameraId);
            if (idx >= 0) _registry.Cameras[idx] = edited;
        }
        Persist();
        _ = ReloadAsync();
    }

    public void Preview(CameraEntry camera)
    {
        var path = string.IsNullOrWhiteSpace(camera.MediaMtxPath) ? camera.CameraId : camera.MediaMtxPath;
        Process.Start(new ProcessStartInfo(_config.PreviewUrl(path)) { UseShellExecute = true });
    }

    public async Task<string> TestConnectionAsync(CameraEntry camera)
    {
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
        var path = string.IsNullOrWhiteSpace(camera.MediaMtxPath) ? camera.CameraId : camera.MediaMtxPath;
        var ready = await _api.IsPathReadyAsync(path);
        var pathResult = ready switch
        {
            true => $"path '{path}' READY",
            false => $"path '{path}' NOT ready",
            null => "MediaMTX API unreachable",
        };
        return $"{tcpResult}; {pathResult}";
    }

    public void ToggleEnabled(CameraEntry camera)
    {
        camera.Enabled = !camera.Enabled;
        Persist();
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
            MessageBox.Show("Tato kamera už je primární.", "Set as primary");
            return;
        }

        var confirm = MessageBox.Show(
            $"Set as primary source for:\n{_config.ProductionStream}\n\n" +
            $"Current: {current?.DisplayName ?? "?"} ({current?.Host ?? "?"})\n" +
            $"Switch to: {camera.DisplayName} ({camera.Host})\n\n" +
            "Detector config se nemění.",
            "Potvrdit přepnutí", MessageBoxButton.OKCancel, MessageBoxImage.Question);
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

        MessageBox.Show(result.Message,
            result.Success ? "OK" : (result.RolledBack ? "Rollback" : "Failed"),
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
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
        EditCommand = new RelayCommand(() => _parent.EditCamera(Camera));
    }

    public CameraEntry Camera { get; }
    public bool IsPrimary { get; }
    public string Title => IsPrimary ? $"{Camera.DisplayName}  ★ PRIMARY" : Camera.DisplayName;
    public string Subtitle =>
        $"IP: {Camera.Host}:{Camera.RtspPort}  profile: {Camera.Profile}  transport: {Camera.Transport}  path: {Camera.MediaMtxPath}  {(Camera.Enabled ? "" : "(disabled)")}";
    public string EnableLabel => Camera.Enabled ? "Disable" : "Enable";
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string TestResult { get => _testResult; private set => SetField(ref _testResult, value); }

    public RelayCommand PreviewCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public RelayCommand ToggleEnabledCommand { get; }
    public AsyncRelayCommand SetPrimaryCommand { get; }
    public RelayCommand EditCredentialsCommand { get; }
    public RelayCommand EditCommand { get; }

    public async Task RefreshAsync(IMediaMtxApi api)
    {
        if (!Camera.Enabled) { Status = "DISABLED"; return; }
        var path = string.IsNullOrWhiteSpace(Camera.MediaMtxPath) ? Camera.CameraId : Camera.MediaMtxPath;
        var ready = await api.IsPathReadyAsync(path);
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
