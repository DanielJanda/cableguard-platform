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
    private readonly CameraRuntimeApplyService _applyService;

    private string _registryStatus = "";
    private string _switchProgress = "";
    private CameraRegistryDocument _registry = new();

    public CamerasViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        IMediaMtxApi api,
        ICredentialStore credentials,
        StreamSwitchService switchService,
        CameraRuntimeApplyService applyService)
    {
        _config = config;
        _logger = logger;
        _api = api;
        _credentials = credentials;
        _switchService = switchService;
        _applyService = applyService;
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
            Host = "10.6.1.63",
            RtspPort = 554,
            Profile = "Streaming/Channels/102",
            Transport = "tcp",
            CredentialRef = "CableGuard.Camera.new",
            MediaMtxPath = "office-test-camera",
            Enabled = true,
            RuntimeState = "configured_not_ready",
        };
        EditCamera(cam, isNew: true);
    }

    public void EditCamera(CameraEntry camera, bool isNew = false)
    {
        var dlg = new Views.CameraEditDialog(camera);
        if (dlg.ShowDialog() != true) return;
        var edited = dlg.Result;
        edited.Enabled = camera.Enabled;
        edited.RuntimeState = camera.RuntimeState;
        edited.LastApplyMessage = camera.LastApplyMessage;
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
            if (string.IsNullOrWhiteSpace(edited.MediaMtxPath))
                edited.MediaMtxPath = CameraRuntimeApplyService.SuggestPath(edited);
            _registry.Cameras.Add(edited);
            Persist();
            var applyNow = MessageBox.Show(
                "Kamera uložena do registry (CONFIGURED / NOT READY).\n\nAplikovat na MediaMTX teď? (RTSP test → path → READY)",
                "Apply MediaMTX", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (applyNow == MessageBoxResult.Yes)
                _ = ApplyToMediaMtxAsync(edited);
            else
                _ = ReloadAsync();
            return;
        }

        var previous = CloneCamera(camera);
        var idx = _registry.Cameras.FindIndex(c => c.CameraId == camera.CameraId);
        if (idx >= 0) _registry.Cameras[idx] = edited;
        Persist();

        var apply = MessageBox.Show(
            "Uložit změny a aplikovat na MediaMTX?\n(Ano = validace + RTSP + path; Ne = jen registry)",
            "Edit camera", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (apply == MessageBoxResult.Cancel)
        {
            if (idx >= 0) _registry.Cameras[idx] = previous;
            Persist();
            _ = ReloadAsync();
            return;
        }
        if (apply == MessageBoxResult.Yes)
            _ = ApplyEditWithRollbackAsync(edited, previous);
        else
            _ = ReloadAsync();
    }

    public async Task ApplyToMediaMtxAsync(CameraEntry camera)
    {
        SwitchProgress = "";
        var progress = new Progress<string>(line =>
        {
            SwitchProgress += line + Environment.NewLine;
        });

        CameraApplyResult result;
        try
        {
            result = await _applyService.ApplyAsync(camera, progress);
        }
        catch (Exception ex)
        {
            result = new CameraApplyResult
            {
                Success = false,
                State = CameraRuntimeState.Fault,
                Message = ex.Message,
            };
        }

        _applyService.UpdateCameraRuntimeState(camera, result);
        var idx = _registry.Cameras.FindIndex(c => c.CameraId == camera.CameraId);
        if (idx >= 0) _registry.Cameras[idx] = camera;
        Persist();

        MessageBox.Show(result.Message,
            result.Success ? (result.NonPersistent ? "READY (non-persistent)" : "READY") : "NOT READY",
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await ReloadAsync();
    }

    private async Task ApplyEditWithRollbackAsync(CameraEntry edited, CameraEntry previous)
    {
        string? previousSource = null;
        var path = CameraRuntimeApplyService.SuggestPath(previous);
        try
        {
            previousSource = await _api.GetConfiguredSourceAsync(path);
        }
        catch { /* ignore */ }

        var result = await _applyService.ApplyAsync(edited);
        if (result.Success)
        {
            _applyService.UpdateCameraRuntimeState(edited, result);
            ReplaceInRegistry(edited);
            Persist();
            MessageBox.Show(result.Message, "Edit OK", MessageBoxButton.OK, MessageBoxImage.Information);
            await ReloadAsync();
            return;
        }

        // Rollback registry + MediaMTX source when possible.
        ReplaceInRegistry(previous);
        Persist();
        if (!string.IsNullOrWhiteSpace(previousSource) && !string.IsNullOrWhiteSpace(previous.MediaMtxPath))
        {
            await _api.PatchPathSourceAsync(previous.MediaMtxPath, previousSource);
            _persisterRollback(previous);
        }
        previous.RuntimeState = "configured_not_ready";
        previous.LastApplyMessage = "Edit failed — rolled back. " + result.Message;
        ReplaceInRegistry(previous);
        Persist();
        MessageBox.Show("Edit selhal, změny vráceny.\n\n" + result.Message, "Rollback",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        await ReloadAsync();
    }

    private void _persisterRollback(CameraEntry previous)
    {
        // Best-effort YAML restore of previous source is handled by re-apply path via API;
        // registry rollback is the operator-facing guarantee.
        _ = previous;
    }

    public async Task DeleteCameraAsync(CameraEntry camera)
    {
        var hits = CollectDependencies(camera);
        var blocking = hits.Where(h =>
            h.Kind is "production_stream" or "detector" or "roi" or "scenario" or "stream_mapping").ToList();
        if (blocking.Count > 0)
        {
            var detail = string.Join("\n", blocking.Select(h => $"- {h.Kind}: {h.Id} ({h.Detail})"));
            MessageBox.Show(
                $"Kamera se používá — smazání zablokováno:\n\n{detail}\n\nNejdřív odeber závislosti, nebo použij Disable.",
                "Delete blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Smazat kameru '{camera.DisplayName}' ({camera.Host})?\n" +
            $"MediaMTX path: {camera.MediaMtxPath}\n\n" +
            "Credential v Windows Credential Manager zůstane (smaž samostatně).",
            "Delete camera", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        // Remove owned non-production streams for this camera.
        try
        {
            var streams = StreamsService.Load(_config.StreamsJsonPath);
            streams.Streams.RemoveAll(s =>
                s.CameraId == camera.CameraId && !s.IsProduction);
            var known = _registry.Cameras.Where(c => c.CameraId != camera.CameraId).Select(c => c.CameraId);
            StreamsService.Save(streams, _config.StreamsJsonPath, known);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[CAMERAS] streams cleanup: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(camera.MediaMtxPath))
        {
            var (ok, msg) = await _applyService.RemovePathAsync(camera.MediaMtxPath);
            _logger.Info($"[CAMERAS] Remove path: {msg}");
            if (!ok)
                MessageBox.Show(msg, "MediaMTX path", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _registry.Cameras.RemoveAll(c => c.CameraId == camera.CameraId);
        Persist();
        await ReloadAsync();
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

        string rtspResult = "RTSP not tested";
        if (_credentials.TryRead(camera.CredentialRef, out var user, out var pass) && pass.Length > 0)
        {
            var source = CameraRuntimeApplyService.BuildRtspSource(camera, user, pass);
            var probe = await RtspProbe.ProbeAsync(source);
            rtspResult = probe.Ok
                ? $"RTSP OK {probe.Codec ?? "?"} {probe.Resolution ?? ""}"
                : $"RTSP FAIL: {probe.Message}";
        }
        else
        {
            rtspResult = "RTSP skipped (credential missing)";
        }

        return $"{tcpResult}; {rtspResult}; {pathResult}";
    }

    public void ToggleEnabled(CameraEntry camera)
    {
        camera.Enabled = !camera.Enabled;
        if (!camera.Enabled)
            camera.RuntimeState = "configured_not_ready";
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

    private IReadOnlyList<CameraDependencyHit> CollectDependencies(CameraEntry camera)
    {
        var streams = StreamsService.Load(_config.StreamsJsonPath);
        var detectors = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath).Instances;
        var rois = RoiProfileService.ListAll(_config.RoiDir);
        var scenarios = ScenarioService.Load(_config.ScenariosJsonPath).Scenarios;
        return CameraRuntimeApplyService.FindDependencies(camera, _registry, streams, detectors, rois, scenarios);
    }

    private void ReplaceInRegistry(CameraEntry cam)
    {
        var idx = _registry.Cameras.FindIndex(c => c.CameraId == cam.CameraId);
        if (idx >= 0) _registry.Cameras[idx] = cam;
    }

    private static CameraEntry CloneCamera(CameraEntry c) => new()
    {
        CameraId = c.CameraId,
        DisplayName = c.DisplayName,
        SiteId = c.SiteId,
        StationId = c.StationId,
        Host = c.Host,
        RtspPort = c.RtspPort,
        Profile = c.Profile,
        Transport = c.Transport,
        Enabled = c.Enabled,
        CredentialRef = c.CredentialRef,
        MediaMtxPath = c.MediaMtxPath,
        RuntimeState = c.RuntimeState,
        LastApplyMessage = c.LastApplyMessage,
    };
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
        ApplyCommand = new AsyncRelayCommand(() => _parent.ApplyToMediaMtxAsync(Camera));
        DeleteCommand = new AsyncRelayCommand(() => _parent.DeleteCameraAsync(Camera));
    }

    public CameraEntry Camera { get; }
    public bool IsPrimary { get; }
    public string Title => IsPrimary ? $"{Camera.DisplayName}  ★ PRIMÁRNÍ" : Camera.DisplayName;
    public string Subtitle =>
        $"IP: {Camera.Host}:{Camera.RtspPort}  profile: {Camera.Profile}  transport: {Camera.Transport}  " +
        $"path: {Camera.MediaMtxPath}  state: {Camera.RuntimeState}  {(Camera.Enabled ? "" : "(disabled)")}";
    public string EnableLabel => Camera.Enabled ? "Disable" : "Enable";
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string TestResult { get => _testResult; private set => SetField(ref _testResult, value); }

    public RelayCommand PreviewCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public RelayCommand ToggleEnabledCommand { get; }
    public AsyncRelayCommand SetPrimaryCommand { get; }
    public RelayCommand EditCredentialsCommand { get; }
    public RelayCommand EditCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    public async Task RefreshAsync(IMediaMtxApi api)
    {
        if (!Camera.Enabled) { Status = "VYPNUTO"; return; }
        var path = string.IsNullOrWhiteSpace(Camera.MediaMtxPath) ? Camera.CameraId : Camera.MediaMtxPath;
        var ready = await api.IsPathReadyAsync(path);
        Status = ready switch
        {
            true => "READY",
            false => string.Equals(Camera.RuntimeState, "ready", StringComparison.OrdinalIgnoreCase)
                ? "OFFLINE"
                : "NOT READY",
            null => "MediaMTX?",
        };
    }

    private void EditCredentials()
    {
        var (username, hasPassword) = _parent.ReadCredentialInfo(Camera);
        var dialog = new Views.CredentialsDialog(Camera.DisplayName, Camera.CredentialRef, username, hasPassword);
        if (dialog.ShowDialog() == true)
            _parent.SaveCredentials(Camera, dialog.EnteredUsername, dialog.EnteredPassword);
    }
}
