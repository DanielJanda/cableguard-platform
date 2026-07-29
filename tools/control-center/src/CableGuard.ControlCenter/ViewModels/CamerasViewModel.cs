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
    private readonly SelectedCameraSession _session;
    private readonly DetectorsViewModel _detectors;
    private readonly NotificationsViewModel _notifications;

    private string _registryStatus = "";
    private string _switchProgress = "";
    private CameraRegistryDocument _registry = new();

    public CamerasViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        IMediaMtxApi api,
        ICredentialStore credentials,
        StreamSwitchService switchService,
        CameraRuntimeApplyService applyService,
        SelectedCameraSession session,
        DetectorsViewModel detectors,
        NotificationsViewModel notifications)
    {
        _config = config;
        _logger = logger;
        _api = api;
        _credentials = credentials;
        _switchService = switchService;
        _applyService = applyService;
        _session = session;
        _detectors = detectors;
        _notifications = notifications;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        AddCommand = new RelayCommand(AddCamera);
        _ = ReloadAsync();
    }

    public ObservableCollection<CameraRowViewModel> Cameras { get; } = new();
    public string RegistryStatus { get => _registryStatus; private set => SetField(ref _registryStatus, value); }
    public string SwitchProgress { get => _switchProgress; private set => SetField(ref _switchProgress, value); }
    public string SelectedCameraLabel =>
        _session.Selected is null ? "Vybraná: (žádná)" : $"Vybraná: {_session.Selected.DisplayName} ({_session.Selected.CameraId})";
    public AsyncRelayCommand ReloadCommand { get; }
    public RelayCommand AddCommand { get; }
    public CameraRegistryDocument Registry => _registry;
    public SelectedCameraSession Session => _session;

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

        // Auto-select office-63 when nothing selected.
        if (_session.Selected is null)
        {
            var office = _registry.Cameras.FirstOrDefault(c =>
                string.Equals(c.CameraId, OfficeCameraBootstrap.OfficeCameraId, StringComparison.OrdinalIgnoreCase));
            if (office is not null) _session.Select(office);
        }

        RegistryStatus = $"{_registry.Cameras.Count} kamer · {SelectedCameraLabel}";
        OnPropertyChanged(nameof(SelectedCameraLabel));
        foreach (var row in Cameras)
            await row.RefreshAsync(_api);
    }

    public void SelectCamera(CameraEntry camera)
    {
        _session.Select(camera);
        OnPropertyChanged(nameof(SelectedCameraLabel));
        RegistryStatus = $"{_registry.Cameras.Count} kamer · {SelectedCameraLabel}";
        foreach (var row in Cameras)
            row.NotifySelectionChanged();
        _logger.Info($"[CAMERAS] Selected {camera.CameraId} path={camera.MediaMtxPath} backend={camera.PreferredBackend}");
    }

    public async Task StartDetectionAsync(CameraEntry camera, bool debug = false)
    {
        SelectCamera(camera);
        var path = string.IsNullOrWhiteSpace(camera.MediaMtxPath) ? camera.CameraId : camera.MediaMtxPath;
        var ready = await _api.IsPathReadyAsync(path);
        if (ready != true)
        {
            MessageBox.Show($"MediaMTX path '{path}' není READY.", "START DETECTION",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(camera, docs);

        try
        {
            var streams = StreamsService.Load(_config.StreamsJsonPath);
            if (!streams.Streams.Any(s => string.Equals(s.StreamId, instance.InputStream, StringComparison.OrdinalIgnoreCase)))
            {
                streams.Streams.Add(new LogicalStream
                {
                    StreamId = instance.InputStream,
                    DisplayName = camera.DisplayName,
                    MediaMtxPath = instance.InputStream,
                    CameraId = camera.CameraId,
                    Enabled = true,
                    IsProduction = !string.Equals(camera.Environment, "test", StringComparison.OrdinalIgnoreCase),
                });
            }
            StreamsService.Save(streams, _config.StreamsJsonPath, _registry.Cameras.Select(c => c.CameraId));
            DetectorLaunchBuilder.Save(docs, _config.DetectorsJsonPath, streams.Streams.Select(s => s.StreamId));
        }
        catch (Exception ex)
        {
            _logger.Warn($"[CAMERAS] persist detector: {ex.Message}");
        }

        if (string.Equals(camera.Environment, "test", StringComparison.OrdinalIgnoreCase))
        {
            instance.PublishTelegram = false;
            _notifications.TelegramEnabled = false;
        }
        instance.DebugOverlay = debug;
        if (string.Equals(camera.CameraId, OfficeCameraBootstrap.OfficeCameraId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.Id, "fall-office-test", StringComparison.OrdinalIgnoreCase))
        {
            instance.InputProfile = "pyav_rtsp";
            instance.SourceMode = "mediamtx";
            instance.InputStream = OfficeCameraBootstrap.OfficePath;
        }

        await _detectors.ReloadAsync();
        var live = _detectors.Items.Select(i => i.Instance).FirstOrDefault(i => i.Id == instance.Id) ?? instance;
        live.DebugOverlay = debug;
        if (string.Equals(live.Id, "fall-office-test", StringComparison.OrdinalIgnoreCase))
        {
            live.InputProfile = "pyav_rtsp";
            live.SourceMode = "mediamtx";
            live.InputStream = OfficeCameraBootstrap.OfficePath;
        }
        _logger.Info($"[CAMERAS] START DETECTION camera={camera.CameraId} profile={live.InputProfile} source={live.SourceMode} path={live.InputStream}");
        await _detectors.StartAsync(live, debug);
        await Task.Delay(2000);
        await ReloadAsync();
        var row = _detectors.Items.FirstOrDefault(i => i.Instance.Id == live.Id);
        row?.Refresh();
        if (row is not null && !string.Equals(row.Status, "BĚŽÍ", StringComparison.OrdinalIgnoreCase))
        {
            var errLog = System.IO.Path.Combine(_config.LogsDir, "detectors", $"{live.Id}.err.log");
            MessageBox.Show(
                $"Detektor se nespustil.\n{errLog}",
                "START DETECTION",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public async Task StopDetectionAsync(CameraEntry camera)
    {
        var docs = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        var instance = CameraDetectionLauncher.ResolveOrCreateInstance(camera, docs);
        var live = _detectors.Items.Select(i => i.Instance).FirstOrDefault(i => i.Id == instance.Id);
        if (live is not null) await _detectors.StopAsync(live);
        await ReloadAsync();
    }

    public async Task RestartDetectionAsync(CameraEntry camera)
    {
        await StopDetectionAsync(camera);
        await Task.Delay(1500);
        await StartDetectionAsync(camera, debug: false);
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

    public void OpenInMonitor(CameraEntry camera)
    {
        // STREAM PREVIEW — never open E2E dashboard from camera row.
        var path = string.IsNullOrWhiteSpace(camera.MediaMtxPath) ? camera.CameraId : camera.MediaMtxPath;
        var url = $"http://{_config.LanHost}:8080/test-lab/stream/{Uri.EscapeDataString(path)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        _logger.Info($"[CAMERAS] STREAM PREVIEW: {url}");
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

    public string GetDetectorStateLabel(CameraEntry camera)
    {
        var path = string.IsNullOrWhiteSpace(camera.MediaMtxPath) ? camera.CameraId : camera.MediaMtxPath;
        var row = _detectors.Items.FirstOrDefault(r =>
            r.Instance.DetectorType == "fall" &&
            string.Equals(r.Instance.InputStream, path, StringComparison.OrdinalIgnoreCase));
        if (row is null) return "DET STOPPED";
        row.Refresh();
        return string.Equals(row.Status, "BĚŽÍ", StringComparison.OrdinalIgnoreCase) ? "DET RUNNING" : "DET STOPPED";
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
        Environment = c.Environment,
        DetectorInputProfile = c.DetectorInputProfile,
        PreferredBackend = c.PreferredBackend,
        SourceMode = c.SourceMode,
        RecordingAllowed = c.RecordingAllowed,
        EventGenerationAllowed = c.EventGenerationAllowed,
    };
}

public sealed class CameraRowViewModel : ObservableObject
{
    private readonly CamerasViewModel _parent;
    private string _status = "…";
    private string _testResult = "";
    private string _detectorState = "…";
    private string _recordingState = "…";
    private bool _isSelected;

    public CameraRowViewModel(CameraEntry camera, bool isPrimary, CamerasViewModel parent)
    {
        Camera = camera;
        IsPrimary = isPrimary;
        _parent = parent;
        _isSelected = parent.Session.IsSelected(camera.CameraId);
        PreviewCommand = new RelayCommand(() => _parent.Preview(Camera));
        OpenInMonitorCommand = new RelayCommand(
            () => _parent.OpenInMonitor(Camera),
            () => string.Equals(Status, "READY", StringComparison.OrdinalIgnoreCase));
        TestCommand = new AsyncRelayCommand(async () => TestResult = await _parent.TestConnectionAsync(Camera));
        ToggleEnabledCommand = new RelayCommand(() => _parent.ToggleEnabled(Camera));
        SetPrimaryCommand = new AsyncRelayCommand(() => _parent.SetAsPrimaryAsync(Camera), () => Camera.Enabled && !IsPrimary);
        EditCredentialsCommand = new RelayCommand(EditCredentials);
        EditCommand = new RelayCommand(() => _parent.EditCamera(Camera));
        ApplyCommand = new AsyncRelayCommand(() => _parent.ApplyToMediaMtxAsync(Camera));
        DeleteCommand = new AsyncRelayCommand(() => _parent.DeleteCameraAsync(Camera));
        SelectCommand = new RelayCommand(() => _parent.SelectCamera(Camera));
        StartDetectionCommand = new AsyncRelayCommand(() => _parent.StartDetectionAsync(Camera));
        StopDetectionCommand = new AsyncRelayCommand(() => _parent.StopDetectionAsync(Camera));
        RestartDetectionCommand = new AsyncRelayCommand(() => _parent.RestartDetectionAsync(Camera));
    }

    public CameraEntry Camera { get; }
    public bool IsPrimary { get; }
    public bool IsSelected { get => _isSelected; private set => SetField(ref _isSelected, value); }
    public string EnvBadge => string.Equals(Camera.Environment, "test", StringComparison.OrdinalIgnoreCase) ? "TEST" : "PRODUCTION";
    public string Title =>
        (IsSelected ? "▶ " : "") +
        (IsPrimary ? $"{Camera.DisplayName}  ★ PRIMÁRNÍ" : Camera.DisplayName);
    public string Subtitle =>
        $"{EnvBadge} · path: {Camera.MediaMtxPath} · backend: {Camera.PreferredBackend}/{Camera.SourceMode} · " +
        $"host: {Camera.Host} · state: {Camera.RuntimeState}" +
        (Camera.Enabled ? "" : " (disabled)");
    public string EnableLabel => Camera.Enabled ? "Disable" : "Enable";
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string DetectorState { get => _detectorState; private set => SetField(ref _detectorState, value); }
    public string RecordingState { get => _recordingState; private set => SetField(ref _recordingState, value); }
    public string TestResult { get => _testResult; private set => SetField(ref _testResult, value); }

    public RelayCommand PreviewCommand { get; }
    public RelayCommand OpenInMonitorCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public RelayCommand ToggleEnabledCommand { get; }
    public AsyncRelayCommand SetPrimaryCommand { get; }
    public RelayCommand EditCredentialsCommand { get; }
    public RelayCommand EditCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand SelectCommand { get; }
    public AsyncRelayCommand StartDetectionCommand { get; }
    public AsyncRelayCommand StopDetectionCommand { get; }
    public AsyncRelayCommand RestartDetectionCommand { get; }

    public void NotifySelectionChanged()
    {
        IsSelected = _parent.Session.IsSelected(Camera.CameraId);
        OnPropertyChanged(nameof(Title));
    }

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
        var rec = await api.IsPathRecordingEnabledAsync(path);
        RecordingState = rec switch { true => "REC ON", false => "REC OFF", _ => "REC ?" };
        DetectorState = _parent.GetDetectorStateLabel(Camera);
        OpenInMonitorCommand.RaiseCanExecuteChanged();
    }

    private void EditCredentials()
    {
        var (username, hasPassword) = _parent.ReadCredentialInfo(Camera);
        var dialog = new Views.CredentialsDialog(Camera.DisplayName, Camera.CredentialRef, username, hasPassword);
        if (dialog.ShowDialog() == true)
            _parent.SaveCredentials(Camera, dialog.EnteredUsername, dialog.EnteredPassword);
    }
}
