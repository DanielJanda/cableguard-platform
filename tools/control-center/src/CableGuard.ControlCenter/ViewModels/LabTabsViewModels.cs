using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

public sealed class CalibrationViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly Func<StreamsDocument> _streams;
    private readonly Func<CameraRegistryDocument> _cameras;
    private readonly IComponentController? _mediaMtx;

    private string _profileId = "office-fall-test";
    private string _pointsText = "";
    private string _status = "Načti snímek kamery, pak klikáním do obrazu přidej body polygonu (min. 3).";
    private string _selectedStreamId = "zahradky-horni-stanice";
    private string _detectorType = "fall";
    private string _roiRole = "fall";
    private string _cameraHint = "";
    private string _frameHint = "Snímek zatím nenačten — klikni „Načíst snímek“.";
    private BitmapImage? _frameImage;
    private int _frameWidth = 1280;
    private int _frameHeight = 720;
    private bool _suppressSuggest;
    private bool _grabBusy;

    public CalibrationViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        Func<StreamsDocument> streams,
        Func<CameraRegistryDocument> cameras,
        IComponentController? mediaMtx = null)
    {
        _config = config;
        _logger = logger;
        _streams = streams;
        _cameras = cameras;
        _mediaMtx = mediaMtx;
        Points = new ObservableCollection<RoiPoint>();
        Points.CollectionChanged += (_, _) => PointsChanged?.Invoke(this, EventArgs.Empty);
        ResetCommand = new RelayCommand(() => { Points.Clear(); Rebuild(); });
        UndoPointCommand = new RelayCommand(() =>
        {
            if (Points.Count == 0) return;
            Points.RemoveAt(Points.Count - 1);
            Rebuild();
        });
        FullFrameCommand = new RelayCommand(() =>
        {
            Points.Clear();
            Points.Add(new(0, 0));
            Points.Add(new(FrameWidth, 0));
            Points.Add(new(FrameWidth, FrameHeight));
            Points.Add(new(0, FrameHeight));
            Rebuild();
        });
        SaveCommand = new RelayCommand(Save);
        LoadCommand = new RelayCommand(Load);
        PreviewStreamCommand = new RelayCommand(PreviewStream);
        RefreshSourcesCommand = new RelayCommand(RefreshSources);
        GrabFrameCommand = new AsyncRelayCommand(GrabFrameAsync, () => !_grabBusy);
        EnsureMediaMtxCommand = new AsyncRelayCommand(EnsureMediaMtxAsync);
        CanvasClickCommand = new RelayCommand<Point?>(AddPoint);
        EnsureBarrierTemplates();
        RefreshSources();
        ReloadList();
        UpdateCameraHint();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            () => _ = GrabFrameAsync());
    }

    /// <summary>Raised when polygon points change — MainWindow redraws overlay.</summary>
    public event EventHandler? PointsChanged;

    public ObservableCollection<RoiPoint> Points { get; }
    public ObservableCollection<string> ProfileIds { get; } = new();
    public ObservableCollection<string> StreamIds { get; } = new();
    public IReadOnlyList<string> DetectorTypes { get; } = new[] { "fall", "barrier" };
    public ObservableCollection<string> RoiRoles { get; } = new();

    public string ProfileId
    {
        get => _profileId;
        set { if (SetField(ref _profileId, value)) TryAutoLoad(); }
    }
    public string SelectedStreamId
    {
        get => _selectedStreamId;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!SetField(ref _selectedStreamId, value)) return;
            UpdateCameraHint();
            _ = GrabFrameAsync();
        }
    }
    public string DetectorType
    {
        get => _detectorType;
        set
        {
            if (SetField(ref _detectorType, value))
            {
                RebuildRoiRoles();
                if (!_suppressSuggest) SuggestProfileId();
            }
        }
    }
    public string RoiRole
    {
        get => _roiRole;
        set
        {
            if (SetField(ref _roiRole, value))
            {
                if (!_suppressSuggest) SuggestProfileId();
            }
        }
    }
    public string PointsText { get => _pointsText; private set => SetField(ref _pointsText, value); }
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string CameraHint { get => _cameraHint; private set => SetField(ref _cameraHint, value); }
    public string FrameHint { get => _frameHint; private set => SetField(ref _frameHint, value); }
    public BitmapImage? FrameImage
    {
        get => _frameImage;
        private set
        {
            if (SetField(ref _frameImage, value))
                OnPropertyChanged(nameof(HasFrame));
        }
    }
    public bool HasFrame => FrameImage is not null;
    public int FrameWidth { get => _frameWidth; private set => SetField(ref _frameWidth, value); }
    public int FrameHeight { get => _frameHeight; private set => SetField(ref _frameHeight, value); }

    public RelayCommand ResetCommand { get; }
    public RelayCommand UndoPointCommand { get; }
    public RelayCommand FullFrameCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand PreviewStreamCommand { get; }
    public RelayCommand RefreshSourcesCommand { get; }
    public AsyncRelayCommand GrabFrameCommand { get; }
    public AsyncRelayCommand EnsureMediaMtxCommand { get; }
    public RelayCommand<Point?> CanvasClickCommand { get; }

    public void AddPoint(Point? p)
    {
        if (p is null) return;
        Points.Add(new RoiPoint((int)p.Value.X, (int)p.Value.Y));
        Rebuild();
    }

    public string ResolveMediaMtxPath()
    {
        var stream = _streams().Streams.FirstOrDefault(s => s.StreamId == SelectedStreamId);
        return !string.IsNullOrWhiteSpace(stream?.MediaMtxPath) ? stream!.MediaMtxPath : SelectedStreamId;
    }

    private async Task EnsureMediaMtxAsync()
    {
        if (_mediaMtx is null)
        {
            Status = "MediaMTX controller není k dispozici.";
            return;
        }

        FrameHint = "Kontroluji MediaMTX…";
        var snap = await _mediaMtx.GetStatusAsync();
        if (snap.Status is ComponentStatus.Running or ComponentStatus.Degraded)
        {
            FrameHint = $"MediaMTX už běží ({snap.Detail}). Načítám snímek…";
            await GrabFrameAsync();
            return;
        }

        FrameHint = "Spouštím MediaMTX (stačí pro ROI — detektor/monitor nejsou potřeba)…";
        _logger.Info("[ROI] Ensure MediaMTX start");
        var result = await _mediaMtx.StartAsync();
        if (!result.Success)
        {
            FrameHint = $"MediaMTX start selhal: {result.Message}";
            Status = FrameHint;
            _logger.Error($"[ROI] MediaMTX start failed: {result.Message}");
            return;
        }

        // Brief wait for RTSP to bind.
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(500);
            snap = await _mediaMtx.GetStatusAsync();
            if (snap.Status is ComponentStatus.Running or ComponentStatus.Degraded or ComponentStatus.Fault)
                break;
        }

        FrameHint = $"MediaMTX: {snap.Detail}. Načítám snímek…";
        await GrabFrameAsync();
    }

    private async Task GrabFrameAsync()
    {
        if (_grabBusy) return;
        _grabBusy = true;
        GrabFrameCommand.RaiseCanExecuteChanged();
        var path = ResolveMediaMtxPath();
        FrameHint = $"Načítám snímek z rtsp://127.0.0.1:8554/{path} …";
        Status = FrameHint;
        try
        {
            var result = await StreamFrameGrabber.GrabJpegAsync(path).ConfigureAwait(true);
            if (!result.Ok || result.JpegBytes is null)
            {
                FrameImage = null;
                FrameHint = result.Message +
                            "\n\nROI potřebuje jen MediaMTX (ne celý START PRODUKCE). " +
                            "Klikni „Spustit MediaMTX“ výše, nebo na Přehledu Start u MediaMTX.";
                Status = result.Message;
                _logger.Warn($"[ROI] Frame grab failed: {result.Message}");
                return;
            }

            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(result.JpegBytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
            }
            FrameWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : 1280;
            FrameHeight = bmp.PixelHeight > 0 ? bmp.PixelHeight : 720;
            FrameImage = bmp;
            FrameHint = $"Snímek {FrameWidth}×{FrameHeight} · {path} — klikáním kresli polygon.";
            Status = FrameHint;
            _logger.Info($"[ROI] Frame grabbed {FrameWidth}x{FrameHeight} from {path}");
            PointsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            FrameImage = null;
            FrameHint = ex.Message;
            Status = ex.Message;
            _logger.Error($"[ROI] Frame grab exception: {ex.Message}");
        }
        finally
        {
            _grabBusy = false;
            GrabFrameCommand.RaiseCanExecuteChanged();
        }
    }

    private void Rebuild()
    {
        PointsText = string.Join("\n", Points.Select((pt, i) => $"{i + 1}: {pt.X}, {pt.Y}"));
        Status = Points.Count < 3
            ? $"Bodů: {Points.Count} (potřeba ≥3) · {DetectorType}/{RoiRole} · {FrameWidth}×{FrameHeight}"
            : $"Bodů: {Points.Count} — připraveno k uložení · {DetectorType}/{RoiRole}";
    }

    private void RefreshSources()
    {
        var docs = _streams();
        StreamIds.Clear();
        foreach (var s in docs.Streams) StreamIds.Add(s.StreamId);
        if (StreamIds.Count == 0)
        {
            StreamIds.Add(_config.ProductionStream);
            StreamIds.Add("office-test");
        }
        if (!StreamIds.Contains(SelectedStreamId) && StreamIds.Count > 0)
            SelectedStreamId = StreamIds[0];
        UpdateCameraHint();
        RebuildRoiRoles();
    }

    private void RebuildRoiRoles()
    {
        RoiRoles.Clear();
        if (DetectorType == "barrier")
        {
            RoiRoles.Add("person");
            RoiRoles.Add("safety_bar");
            RoiRoles.Add("exclude");
        }
        else
        {
            RoiRoles.Add("fall");
        }
        if (!RoiRoles.Contains(RoiRole))
            RoiRole = RoiRoles[0];
    }

    private void SuggestProfileId()
    {
        var stream = SelectedStreamId.Replace("zahradky-", "").Replace("-stanice", "");
        ProfileId = DetectorType == "barrier"
            ? $"barrier-{stream}-{RoiRole}"
            : $"fall-{stream}";
    }

    private void UpdateCameraHint()
    {
        var stream = _streams().Streams.FirstOrDefault(s => s.StreamId == SelectedStreamId);
        var cam = stream is null
            ? null
            : _cameras().Cameras.FirstOrDefault(c => c.CameraId == stream.CameraId);
        CameraHint = cam is null
            ? $"Stream „{SelectedStreamId}“ — kamera není namapovaná. Snímek bere lokální MediaMTX RTSP."
            : $"Kamera: {cam.DisplayName} ({cam.Host}) · stream {SelectedStreamId}";
    }

    private void PreviewStream()
    {
        var path = ResolveMediaMtxPath();
        var url = _config.PreviewUrl(path);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        _logger.Info($"[ROI] Preview stream {SelectedStreamId} path={path} → {url}");
    }

    private void Save()
    {
        var profile = new RoiProfile
        {
            Id = ProfileId.Trim(),
            DisplayName = $"{DetectorType}/{RoiRole} @ {SelectedStreamId}",
            StreamId = SelectedStreamId,
            DetectorType = DetectorType,
            RoiRole = RoiRole,
            Points = Points.ToList(),
            SourceWidth = FrameWidth,
            SourceHeight = FrameHeight,
            ActivationState = "saved",
        };
        try
        {
            RoiProfileService.Save(profile, RuntimeConfigPaths.RoiFile(_config, profile.Id));
            _logger.Info($"[ROI] SAVED {profile.Id} type={profile.DetectorType} role={profile.RoiRole} stream={profile.StreamId} pts={profile.Points.Count} {profile.SourceWidth}x{profile.SourceHeight} (not ACTIVE)");
            Status = $"SAVED profil: {profile.Id} ({profile.SourceWidth}×{profile.SourceHeight}) — NENÍ ACTIVE v detektoru, dokud se explicitně neaplikuje/restartuje.";
            ReloadList();
            if (DetectorType == "barrier")
            {
                MessageBox.Show(
                    "ROI zábran uloženo jako SAVED (runtime/config/roi/).\n\n" +
                    "Není ACTIVE v běžícím detektoru.\n\n" +
                    "Produkční zábrany (zahradky_safety/app.py) mají 3 polygony:\n" +
                    "• person (ROI_PERSON)\n• safety_bar (ROI_SAFETY_BAR)\n• exclude (ROI_EXCLUDE_PERSON)\n\n" +
                    "Admin Studio nepíše do app.py / YAML.",
                    "ROI zábrany — SAVED", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Profil „{profile.Id}“ uložen jako SAVED.\n" +
                    $"Rozlišení snímku: {profile.SourceWidth}×{profile.SourceHeight}\n\n" +
                    "ACTIVE ROI v detektoru se nemění, dokud konfiguraci neaplikuješ a detektor nerestartuješ.",
                    "ROI — SAVED", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Uložení ROI", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Load()
    {
        var path = RuntimeConfigPaths.RoiFile(_config, ProfileId.Trim());
        if (!File.Exists(path)) { Status = "Profil neexistuje."; return; }
        var profile = RoiProfileService.Load(path);
        if (profile.IsProduction)
        {
            if (MessageBox.Show("Načítáš PRODUCTION ROI. Pokračovat?", "ROI",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }
        ApplyProfile(profile);
    }

    private void TryAutoLoad()
    {
        if (_suppressSuggest) return;
        var path = RuntimeConfigPaths.RoiFile(_config, ProfileId.Trim());
        if (!File.Exists(path)) return;
        try { ApplyProfile(RoiProfileService.Load(path)); }
        catch { /* ignore bad file while typing id */ }
    }

    private void ApplyProfile(RoiProfile profile)
    {
        _suppressSuggest = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(profile.StreamId))
                SelectedStreamId = profile.StreamId;
            if (!string.IsNullOrWhiteSpace(profile.DetectorType))
                DetectorType = profile.DetectorType;
            if (!string.IsNullOrWhiteSpace(profile.RoiRole))
                RoiRole = profile.RoiRole;
            ProfileId = profile.Id;
            Points.Clear();
            foreach (var p in profile.Points) Points.Add(p);
            Rebuild();
            Status = $"Načteno: {profile.Id}";
        }
        finally
        {
            _suppressSuggest = false;
        }
    }

    private void ReloadList()
    {
        ProfileIds.Clear();
        foreach (var p in RoiProfileService.ListAll(_config.RoiDir))
            ProfileIds.Add(p.Id);
    }

    /// <summary>Seed editable templates mirroring barrier multi-ROI from zahradky_safety/app.py (not live algorithm).</summary>
    private void EnsureBarrierTemplates()
    {
        Directory.CreateDirectory(_config.RoiDir);
        void Seed(string id, string role, (int x, int y)[] pts)
        {
            var path = RuntimeConfigPaths.RoiFile(_config, id);
            if (File.Exists(path)) return;
            var profile = new RoiProfile
            {
                Id = id,
                DisplayName = $"Barrier template {role}",
                StreamId = "zahradky-horni-stanice",
                DetectorType = "barrier",
                RoiRole = role,
                IsProduction = false,
                SourceWidth = 1920,
                SourceHeight = 1080,
                ActivationState = "saved",
                Points = pts.Select(p => new RoiPoint(p.x, p.y)).ToList(),
            };
            try { RoiProfileService.Save(profile, path); }
            catch { /* ignore */ }
        }
        // Values from zahradky_safety/app.py (documentation snapshot for editor templates).
        Seed("barrier-horni-person", "person", new[] { (16, 229), (1780, 495), (1821, 699), (46, 1016) });
        Seed("barrier-horni-safety_bar", "safety_bar", new[] { (3, 62), (1810, 402), (1809, 543), (19, 420) });
        Seed("barrier-horni-exclude", "exclude", new[] { (1115, 268), (1124, 339), (1201, 346), (1202, 284) });
    }
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    public RelayCommand(Action<T?> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        if (parameter is T t) _execute(t);
        else if (parameter is null) _execute(default);
        else if (typeof(T) == typeof(Point?) && parameter is Point p) _execute((T)(object)p);
    }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}


public sealed class NotificationsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly ICredentialStore _credentials;
    private NotificationsDocument _doc = new();
    private string _credStatus = "";
    private string _eventCoreStatus = "NOT AVAILABLE / FEATURE BRANCH (EventCorePublisher lands in Phase 4)";

    public NotificationsViewModel(ControlCenterConfig config, ControlCenterLogger logger, ICredentialStore credentials)
    {
        _config = config; _logger = logger; _credentials = credentials;
        Reload();
        SaveCommand = new RelayCommand(Save);
        TestCommand = new RelayCommand(SendTest);
        ChangeCredCommand = new RelayCommand(ChangeCred);
    }

    public bool TelegramEnabled { get => _doc.TelegramEnabled; set { _doc.TelegramEnabled = value; OnPropertyChanged(); } }
    public string TargetLabel { get => _doc.TelegramTargetLabel; set { _doc.TelegramTargetLabel = value; OnPropertyChanged(); } }
    public bool NotifyFall { get => _doc.NotifyFall; set { _doc.NotifyFall = value; OnPropertyChanged(); } }
    public bool NotifyBarrier { get => _doc.NotifyBarrier; set { _doc.NotifyBarrier = value; OnPropertyChanged(); } }
    public bool NotifyErrors { get => _doc.NotifyTechnicalErrors; set { _doc.NotifyTechnicalErrors = value; OnPropertyChanged(); } }
    public string CredStatus { get => _credStatus; private set => SetField(ref _credStatus, value); }
    public string EventCoreStatus { get => _eventCoreStatus; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand ChangeCredCommand { get; }

    public NotificationsDocument Document => _doc;

    private void Reload()
    {
        _doc = NotificationsService.Load(_config.NotificationsJsonPath);
        var has = _credentials.TryRead(_doc.TelegramCredentialRef, out _, out var pass) && pass.Length > 0;
        CredStatus = has ? "Credential configured ✓" : "Credential NOT configured";
        OnPropertyChanged(nameof(TelegramEnabled));
        OnPropertyChanged(nameof(TargetLabel));
    }

    private void Save()
    {
        NotificationsService.Save(_doc, _config.NotificationsJsonPath);
        _logger.Info("[NOTIFY] Saved notifications.json (no secrets)");
        MessageBox.Show("Uloženo.", "Notifications");
    }

    private void SendTest()
    {
        if (!_credentials.TryRead(_doc.TelegramCredentialRef, out var user, out var token) || token.Length == 0)
        {
            MessageBox.Show("Telegram credential není nastavený.", "Send Test", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // user field may hold chat_id; token is bot token — never log either.
        _logger.Info("[NOTIFY] SEND TEST MESSAGE requested (credential present; token not logged).");
        MessageBox.Show(
            "Test message dispatch is logged. Wire actual Telegram HTTP send in a follow-up once chat_id storage format is finalized.\n\n" +
            $"Target profile: {TargetLabel}\nCredential ref: {_doc.TelegramCredentialRef}\nUsername/chat field length: {user.Length}",
            "Send Test Message");
    }

    private void ChangeCred()
    {
        var dlg = new Views.CredentialsDialog("Telegram", _doc.TelegramCredentialRef, "", false);
        if (dlg.ShowDialog() == true)
        {
            _credentials.Write(_doc.TelegramCredentialRef, dlg.EnteredUsername, dlg.EnteredPassword);
            _logger.Info("[NOTIFY] Telegram credential updated (value not logged).");
            Reload();
        }
    }
}

public sealed class HardwareViewModel : ObservableObject
{
    private readonly ControlCenterLogger _logger;
    private readonly IHardwareAdapter _adapter;
    private bool _testMode;
    private string _status = "";
    private string _deviceLine = "";
    private string _lastOp = "—";

    public HardwareViewModel(ControlCenterLogger logger, IHardwareAdapter adapter)
    {
        _logger = logger; _adapter = adapter;
        _status = adapter.StatusDetail;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        Pulse1Command = new AsyncRelayCommand(() => Pulse(1), () => CanWrite);
        Pulse2Command = new AsyncRelayCommand(() => Pulse(2), () => CanWrite);
        Pulse3Command = new AsyncRelayCommand(() => Pulse(3), () => CanWrite);
        GreenCommand = new AsyncRelayCommand(() => Semaphore("green"), () => CanSemantic);
        RedCommand = new AsyncRelayCommand(() => Semaphore("red"), () => CanSemantic);
        BuzzerCommand = new AsyncRelayCommand(() => Semaphore("buzzer"), () => CanSemantic);
        AllOffCommand = new AsyncRelayCommand(AllOff, () => CanWrite);
        _ = RefreshAsync();
    }

    public bool TestMode
    {
        get => _testMode;
        set
        {
            if (SetField(ref _testMode, value))
            {
                _adapter.IsTestMode = value;
                _logger.Info($"[HW] TEST MODE {(value ? "ENABLED" : "disabled")}");
                if (value)
                {
                    try
                    {
                        // Entering TEST MODE: verify connection and force ALL OFF when available.
                        _ = EnterTestModeAsync();
                    }
                    catch (Exception ex)
                    {
                        Status = ex.Message;
                    }
                }
                RaiseCanExecutes();
            }
        }
    }

    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string DeviceLine { get => _deviceLine; private set => SetField(ref _deviceLine, value); }
    public string LastOp { get => _lastOp; private set => SetField(ref _lastOp, value); }
    public string Banner => "⚠ HARDWARE TEST MODE — manuální zásahy jen s potvrzením, auto-off ≤500 ms, žádná vazba detector→relay";
    public bool CanWrite => TestMode && _adapter.IsAvailable;
    public bool CanSemantic => CanWrite && (_adapter as AdvantechUsb4761Adapter)?.MappingConfigured == true;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand Pulse1Command { get; }
    public AsyncRelayCommand Pulse2Command { get; }
    public AsyncRelayCommand Pulse3Command { get; }
    public AsyncRelayCommand GreenCommand { get; }
    public AsyncRelayCommand RedCommand { get; }
    public AsyncRelayCommand BuzzerCommand { get; }
    public AsyncRelayCommand AllOffCommand { get; }

    private async Task EnterTestModeAsync()
    {
        await _adapter.EnsureConnectedAsync();
        if (_adapter.IsAvailable)
        {
            await _adapter.AllOffAsync();
            LastOp = "TEST MODE → ALL OFF";
        }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await _adapter.EnsureConnectedAsync();
        Status = _adapter.StatusDetail;
        if (_adapter is AdvantechUsb4761Adapter adv)
        {
            var d = adv.Discovery;
            DeviceLine = $"Device: {d.Status} | Model: {d.Model} | Serial: {d.SerialMasked} | " +
                         $"Driver: {d.DriverStatus} | Relays: {d.RelayCount} | DI: {d.DiCount}";
            LastOp = adv.LastOperation;
            if (!string.IsNullOrWhiteSpace(adv.LastError))
                Status = adv.StatusDetail;
        }
        else
        {
            DeviceLine = _adapter.IsAvailable ? "Device: CONNECTED" : "Device: NOT AVAILABLE";
        }

        try
        {
            var di = await _adapter.ReadDigitalInputsAsync();
            if (di.Count > 0)
                DeviceLine += " | DI: " + string.Join(" ", di.Select(kv => $"{kv.Key}={(kv.Value ? 1 : 0)}"));
        }
        catch { /* discovery-only ok */ }

        RaiseCanExecutes();
    }

    private void RaiseCanExecutes()
    {
        Pulse1Command.RaiseCanExecuteChanged();
        Pulse2Command.RaiseCanExecuteChanged();
        Pulse3Command.RaiseCanExecuteChanged();
        GreenCommand.RaiseCanExecuteChanged();
        RedCommand.RaiseCanExecuteChanged();
        BuzzerCommand.RaiseCanExecuteChanged();
        AllOffCommand.RaiseCanExecuteChanged();
    }

    private async Task Pulse(int ch)
    {
        if (!Confirm($"Pulse relay {ch} (max 500 ms)?")) return;
        try
        {
            HardwareSafety.EnsureTestMode(_adapter);
            await _adapter.PulseRelayAsync(ch, HardwareSafety.ClampPulse(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500)));
            LastOp = $"pulse {ch}";
            _logger.Info($"[HW] Pulse relay {ch} OK");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            MessageBox.Show(ex.Message, "Hardware", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task Semaphore(string color)
    {
        if (!CanSemantic)
        {
            MessageBox.Show("Semantic mapping NOT CONFIGURED — nastav green/red/buzzer_channel v hardware.json.",
                "Hardware", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Confirm($"Pulse semaphore {color} (≤500 ms)?")) return;
        try
        {
            HardwareSafety.EnsureTestMode(_adapter);
            await _adapter.SetSemaphoreAsync(color, true);
            LastOp = $"semaphore {color}";
            _logger.Info($"[HW] Semaphore {color} pulse");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            MessageBox.Show(ex.Message, "Hardware", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task AllOff()
    {
        try
        {
            HardwareSafety.EnsureTestMode(_adapter);
            await _adapter.AllOffAsync();
            LastOp = "ALL OFF";
            _logger.Info("[HW] ALL OFF");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            MessageBox.Show(ex.Message, "Hardware", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool Confirm(string msg) =>
        MessageBox.Show(msg + "\n\nHARDWARE TEST MODE", "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
        == MessageBoxResult.OK;
}

public sealed class ScenariosViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly DetectorsViewModel _detectors;
    private readonly NotificationsViewModel _notifications;
    private readonly HardwareViewModel _hardware;
    private ScenariosDocument _doc = new();
    private string _diffText = "";

    public ScenariosViewModel(
        ControlCenterConfig config, ControlCenterLogger logger,
        DetectorsViewModel detectors, NotificationsViewModel notifications, HardwareViewModel hardware)
    {
        _config = config; _logger = logger; _detectors = detectors; _notifications = notifications; _hardware = hardware;
        Reload();
        RunCommand = new AsyncRelayCommand(RunSelected);
        OpenE2eDashboardCommand = new RelayCommand(OpenE2eDashboard);
        OpenStreamPreviewCommand = new RelayCommand(OpenStreamPreview);
    }

    public ObservableCollection<ScenarioDocument> Items { get; } = new();
    public ScenarioDocument? Selected { get; set; }
    public string DiffText { get => _diffText; private set => SetField(ref _diffText, value); }
    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand OpenE2eDashboardCommand { get; }
    public RelayCommand OpenStreamPreviewCommand { get; }

    /// <summary>END-TO-END FALL DASHBOARD — kiosk overlay + WS + ack.</summary>
    public void OpenE2eDashboard()
    {
        var station = TestStationService.Find(
                          TestStationService.Load(_config.TestStationsJsonPath), "office-test")
                      ?? TestStationService.OfficeDefault().Stations[0];
        var path = string.IsNullOrWhiteSpace(station.MonitorPath) ? "/test-lab/office-fall" : station.MonitorPath;
        var url = $"http://{_config.LanHost}:8080{path}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        _logger.Info($"[SCENARIO] END-TO-END FALL DASHBOARD: {url}");
    }

    /// <summary>STREAM PREVIEW — video only for office-test-camera.</summary>
    public void OpenStreamPreview()
    {
        var station = TestStationService.Find(
                          TestStationService.Load(_config.TestStationsJsonPath), "office-test")
                      ?? TestStationService.OfficeDefault().Stations[0];
        var stream = string.IsNullOrWhiteSpace(station.VideoStream) ? "office-test-camera" : station.VideoStream;
        var url = $"http://{_config.LanHost}:8080/test-lab/stream/{Uri.EscapeDataString(stream)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        _logger.Info($"[SCENARIO] STREAM PREVIEW: {url}");
    }

    public void Reload()
    {
        Items.Clear();
        _doc = ScenarioService.Load(_config.ScenariosJsonPath);
        if (_doc.Scenarios.Count == 0) _doc = Defaults();
        foreach (var s in _doc.Scenarios) Items.Add(s);
        Selected = Items.FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        RefreshDiff();
    }

    private static ScenariosDocument Defaults() => new()
    {
        Scenarios =
        {
            new ScenarioDocument
            {
                Id = "office-e2e-fall-test",
                DisplayName = "OFFICE END-TO-END FALL TEST",
                Description = "Kancelář – test pádu: MediaMTX + Event Core + Monitor + fall-office-test (test_mode)",
                StreamId = "office-test-camera",
                DetectorIds = { "fall-office-test" },
                RoiProfile = "office-fall-test",
                DebugOverlay = true,
                Telegram = false,
                EventCore = true,
            },
            new ScenarioDocument
            {
                Id = "office-fall-test", DisplayName = "Office fall detection",
                Description = "Office camera, fall detector, debug ON, Telegram OFF",
                StreamId = "office-test-camera", DetectorIds = { "fall-office-test" },
                RoiProfile = "office-fall-test", DebugOverlay = true,
            },
            new ScenarioDocument
            {
                Id = "zahradky-production", DisplayName = "Zahrádky production",
                Description = "Upper 92, fall+barrier, Telegram ON, debug OFF",
                StreamId = "zahradky-horni-stanice",
                DetectorIds = { "fall-zahradky-upper", "barrier-zahradky-upper" },
                Telegram = true, EventCore = true,
            },
            new ScenarioDocument
            {
                Id = "barrier-hardware-test", DisplayName = "Barrier hardware test",
                Description = "Barrier ON, semaphore TEST MODE, fall OFF",
                StreamId = "zahradky-horni-stanice",
                DetectorIds = { "barrier-zahradky-upper" },
                HardwareTest = true,
            },
        }
    };

    public void RefreshDiff()
    {
        if (Selected is null) { DiffText = ""; return; }
        var running = _detectors.Items.Where(i => i.Status is "RUNNING" or "CONFIGURED").Select(i => i.Instance.Id);
        // Approximate running set — Start will reconcile.
        DiffText = string.Join("\n", ScenarioService.Diff(
            Selected, _notifications.TelegramEnabled, false, _hardware.TestMode, running));
    }

    private async Task RunSelected()
    {
        if (Selected is null) return;
        RefreshDiff();
        var isOfficeE2e = string.Equals(Selected.Id, "office-e2e-fall-test", StringComparison.OrdinalIgnoreCase);
        var title = isOfficeE2e ? "SPUSTIT KANCELÁŘSKÝ TEST PÁDU" : "Scenario";
        if (MessageBox.Show($"RUN SCENARIO: {Selected.DisplayName}?\n\n{DiffText}",
                title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _logger.Info($"[SCENARIO] RUN {Selected.Id}");
        if (Selected.HardwareTest) _hardware.TestMode = true;
        _notifications.TelegramEnabled = Selected.Telegram;

        // Snapshot before any Start/Stop that may reload Items.
        var snapshot = _detectors.Items.Select(r => r.Instance).ToList();
        var wanted = new HashSet<string>(Selected.DetectorIds, StringComparer.OrdinalIgnoreCase);

        if (isOfficeE2e)
        {
            // Office E2E: start only fall-office-test; do not stop production detectors / shared services.
            foreach (var instance in snapshot.Where(i => wanted.Contains(i.Id)))
            {
                if (Selected.EventCore)
                    instance.PublishEventCore = true;
                instance.PublishTelegram = false;
                instance.DebugOverlay = Selected.DebugOverlay || instance.DebugOverlay;
                instance.InputStream = string.IsNullOrWhiteSpace(Selected.StreamId)
                    ? instance.InputStream
                    : Selected.StreamId;
                await _detectors.StartAsync(instance, instance.DebugOverlay, reload: false);
            }
            await _detectors.ReloadAsync();

            var station = TestStationService.Find(
                TestStationService.Load(_config.TestStationsJsonPath), "office-test")
                ?? TestStationService.OfficeDefault().Stations[0];
            // Always open END-TO-END FALL DASHBOARD (not stream preview).
            var url = $"http://{_config.LanHost}:8080/test-lab/office-fall";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[SCENARIO] open E2E dashboard failed: {ex.Message}");
            }

            MessageBox.Show(
                "Kancelářský E2E test spuštěn (best-effort).\n\n" +
                $"1) MediaMTX path: {station.VideoStream}\n" +
                $"2) Detector: {station.FallServiceId}\n" +
                $"3) END-TO-END FALL DASHBOARD: {url}\n" +
                $"4) STREAM PREVIEW (odděleně): http://{_config.LanHost}:8080/test-lab/stream/{station.VideoStream}\n\n" +
                "Zastavení scénáře ukončí pouze fall-office-test — neprodukční MediaMTX/Event Core/Monitor.",
                "OFFICE E2E", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var instance in snapshot)
        {
            var want = wanted.Contains(instance.Id);
            if (want) await _detectors.StartAsync(instance, Selected.DebugOverlay || instance.DebugOverlay, reload: false);
            else await _detectors.StopAsync(instance, reload: false);
        }
        await _detectors.ReloadAsync();
        MessageBox.Show("Scenario applied (best-effort). Check Detectors / Hardware / Notifications.", "Scenario");
    }
}
