using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

public sealed class AdminModeViewModel : ObservableObject
{
    private AdminMode _mode = AdminMode.Operations;
    public AdminMode Mode
    {
        get => _mode;
        set
        {
            if (SetField(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsOperations));
                OnPropertyChanged(nameof(IsTestLab));
                OnPropertyChanged(nameof(ModeBanner));
                OnPropertyChanged(nameof(ModeBannerBrush));
            }
        }
    }
    public bool IsOperations => Mode == AdminMode.Operations;
    public bool IsTestLab => Mode == AdminMode.TestLab;
    public string ModeBanner => IsTestLab
        ? "⚠ TESTOVACÍ REŽIM – změny mohou ovlivnit běžící systém"
        : "PROVOZ – běžná správa služeb";
    public Brush ModeBannerBrush => IsTestLab
        ? new SolidColorBrush(Color.FromRgb(0x8B, 0x5A, 0x00))
        : new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x2F));

    public RelayCommand OperationsCommand { get; }
    public RelayCommand TestLabCommand { get; }

    public AdminModeViewModel()
    {
        OperationsCommand = new RelayCommand(() => Mode = AdminMode.Operations);
        TestLabCommand = new RelayCommand(() =>
        {
            if (MessageBox.Show(
                    "Vstoupit do testovacího režimu?\n\nZměny kamer, streamů, detektorů, ROI a hardwaru mohou ovlivnit běžící systém.",
                    "Testovací laboratoř", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                Mode = AdminMode.TestLab;
        });
    }
}

public sealed class StreamsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly IMediaMtxApi _api;
    private readonly StreamSwitchService _switchService;
    private readonly Func<CameraRegistryDocument> _cameras;
    private StreamsDocument _doc = new();
    private string _status = "";

    public StreamsViewModel(
        ControlCenterConfig config, ControlCenterLogger logger, IMediaMtxApi api,
        StreamSwitchService switchService, Func<CameraRegistryDocument> cameras)
    {
        _config = config; _logger = logger; _api = api; _switchService = switchService; _cameras = cameras;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        AddCommand = new RelayCommand(AddStream);
        _ = ReloadAsync();
    }

    public ObservableCollection<StreamRowViewModel> Items { get; } = new();
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public AsyncRelayCommand ReloadCommand { get; }
    public RelayCommand AddCommand { get; }

    public async Task ReloadAsync()
    {
        Items.Clear();
        var cams = _cameras();
        _doc = StreamsService.Load(_config.StreamsJsonPath);
        _doc = StreamsService.MigrateFromLegacy(cams, _doc);
        foreach (var s in _doc.Streams)
            Items.Add(new StreamRowViewModel(s, this));
        Status = $"{_doc.Streams.Count} logical streams";
        foreach (var row in Items) await row.RefreshAsync();
    }

    public void Persist()
    {
        var camIds = _cameras().Cameras.Select(c => c.CameraId);
        StreamsService.Save(_doc, _config.StreamsJsonPath, camIds);
        _logger.Info("[STREAMS] Saved streams.json");
    }

    private void AddStream()
    {
        var cams = _cameras().Cameras;
        if (cams.Count == 0) { MessageBox.Show("Nejdřív přidej kameru."); return; }
        var id = $"stream-{DateTime.Now:HHmmss}";
        var s = new LogicalStream
        {
            StreamId = id, DisplayName = id, MediaMtxPath = id,
            CameraId = cams[0].CameraId, Enabled = true,
        };
        _doc.Streams.Add(s);
        Persist();
        _ = ReloadAsync();
    }

    public async Task ApplyMappingAsync(LogicalStream stream, string newCameraId)
    {
        var confirm = MessageBox.Show(
            $"Přemapovat stream '{stream.StreamId}' → kamera '{newCameraId}'?\n" +
            (stream.IsProduction ? "⚠ PRODUCTION STREAM\n" : "") +
            "Při selhání proběhne rollback.",
            "Apply stream mapping", MessageBoxButton.OKCancel,
            stream.IsProduction ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        // Reuse StreamSwitchService against camera registry mapping for production path.
        var registry = _cameras();
        var result = await _switchService.SwitchPrimaryAsync(
            registry, stream.MediaMtxPath, newCameraId,
            saveRegistry: r =>
            {
                CameraRegistryService.Save(r, _config.CamerasJsonPath);
                stream.CameraId = newCameraId;
                Persist();
            });
        MessageBox.Show(result.Message, result.Success ? "OK" : "Failed",
            MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        await ReloadAsync();
    }

    public void Preview(LogicalStream s) =>
        Process.Start(new ProcessStartInfo(_config.PreviewUrl(s.MediaMtxPath)) { UseShellExecute = true });

    public async Task RefreshReadyAsync(LogicalStream s, Action<string> setStatus)
    {
        var ready = await _api.IsPathReadyAsync(s.MediaMtxPath);
        setStatus(ready switch { true => "ŽIVĚ", false => "OFFLINE", null => "API?" });
    }
}

public sealed class StreamRowViewModel : ObservableObject
{
    private readonly StreamsViewModel _parent;
    private string _live = "…";
    public StreamRowViewModel(LogicalStream stream, StreamsViewModel parent)
    {
        Stream = stream; _parent = parent;
        PreviewCommand = new RelayCommand(() => _parent.Preview(Stream));
        ApplyCommand = new AsyncRelayCommand(() => _parent.ApplyMappingAsync(Stream, Stream.CameraId));
    }
    public LogicalStream Stream { get; }
    public string Title => Stream.IsProduction ? $"{Stream.DisplayName} ★ PRODUKCE" : Stream.DisplayName;
    public string Subtitle => $"cesta: {Stream.MediaMtxPath}  →  kamera: {Stream.CameraId}";
    public string Live { get => _live; private set => SetField(ref _live, value); }
    public RelayCommand PreviewCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public Task RefreshAsync() => _parent.RefreshReadyAsync(Stream, s => Live = s);
}

public sealed class DetectorsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly DetectorProcessManager _manager;
    private readonly Func<StreamsDocument> _streams;
    private readonly Func<NotificationsDocument> _notifications;
    private DetectorsDocument _doc = new();
    private string _status = "";
    private string _algorithmNote = FallAlgorithmInfo.Note;

    public DetectorsViewModel(
        ControlCenterConfig config, ControlCenterLogger logger, DetectorProcessManager manager,
        Func<StreamsDocument> streams, Func<NotificationsDocument> notifications)
    {
        _config = config; _logger = logger; _manager = manager; _streams = streams; _notifications = notifications;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        _ = ReloadAsync();
    }

    public ObservableCollection<DetectorRowViewModel> Items { get; } = new();
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string AlgorithmNote { get => _algorithmNote; }
    public AsyncRelayCommand ReloadCommand { get; }

    public async Task ReloadAsync()
    {
        Items.Clear();
        _doc = DetectorLaunchBuilder.Load(_config.DetectorsJsonPath);
        if (_doc.Instances.Count == 0)
        {
            _doc = DefaultDetectors();
            try { Persist(); } catch { /* streams may be empty on first boot */ }
        }
        foreach (var i in _doc.Instances)
            Items.Add(new DetectorRowViewModel(i, this, _manager));
        Status = $"{_doc.Instances.Count} detector instances";
        foreach (var row in Items) row.Refresh();
        await Task.CompletedTask;
    }

    private static DetectorsDocument DefaultDetectors() => new()
    {
        Instances =
        {
            new DetectorInstance
            {
                Id = "fall-zahradky-upper", DisplayName = "Fall Detector – Zahrádky Upper",
                DetectorType = "fall", InputStream = "zahradky-horni-stanice",
                Model = "yolo11m-pose.pt", RoiProfile = "zahradky-upper-production",
                ScriptRelative = "apps/zahradky_horni_pad.py", ProcessHint = "zahradky_horni_pad",
                PublishJsonl = true,
            },
            new DetectorInstance
            {
                Id = "barrier-zahradky-upper", DisplayName = "Barrier Detector – Zahrádky Upper",
                DetectorType = "barrier", InputStream = "zahradky-horni-stanice",
                Model = "barrier_best_m.pt", RoiProfile = "barrier-upper-production",
                ScriptRelative = "zahradky_safety/app.py", ProcessHint = "zahradky_safety",
            },
            new DetectorInstance
            {
                Id = "fall-office-test", DisplayName = "Fall Detector – Office test",
                DetectorType = "fall", InputStream = "office-test",
                Model = "yolo11m-pose.pt", RoiProfile = "office-fall-test",
                ScriptRelative = "apps/zahradky_horni_pad.py", ProcessHint = "zahradky_horni_pad",
                DebugOverlay = true, Enabled = false,
            },
        }
    };

    public void Persist()
    {
        var streamIds = _streams().Streams.Select(s => s.StreamId).Concat(new[] { "zahradky-horni-stanice", "office-test" });
        DetectorLaunchBuilder.Save(_doc, _config.DetectorsJsonPath, streamIds);
    }

    public async Task StartAsync(DetectorInstance instance, bool debug, bool reload = true)
    {
        var (ok, msg) = await _manager.StartAsync(instance, _notifications(), debug);
        _logger.Info($"[DETECTOR] Start {instance.Id}: {msg}");
        if (!ok) MessageBox.Show(msg, "Start detektoru", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (reload) await ReloadAsync();
        else Items.FirstOrDefault(r => r.Instance.Id == instance.Id)?.Refresh();
    }

    public Task StopAsync(DetectorInstance instance, bool reload = true)
    {
        var (ok, msg) = _manager.Stop(instance);
        _logger.Info($"[DETECTOR] Stop {instance.Id}: {msg}");
        if (!ok) MessageBox.Show(msg, "Stop detektoru", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (reload) return ReloadAsync();
        Items.FirstOrDefault(r => r.Instance.Id == instance.Id)?.Refresh();
        return Task.CompletedTask;
    }

    /// <summary>Stop if needed, then start with OpenCV debug overlay window (náhled detekce).</summary>
    public async Task OpenDebugAsync(DetectorInstance instance)
    {
        if (_manager.FindPid(instance) is not null)
        {
            _logger.Info($"[DETECTOR] Restarting {instance.Id} with debug overlay window");
            await StopAsync(instance, reload: false);
            await Task.Delay(1200);
        }
        var (ok, msg) = await _manager.StartAsync(instance, _notifications(), forceDebug: true);
        _logger.Info($"[DETECTOR] Debug start {instance.Id}: {msg}");
        Items.FirstOrDefault(r => r.Instance.Id == instance.Id)?.Refresh();
        if (!ok)
        {
            MessageBox.Show(msg, "Náhled detekce", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(
            "Detektor běží s náhledem.\n\n" +
            "Mělo se otevřít okno OpenCV:\n„Zahradky horni pad [debug overlay]“\n\n" +
            "Pokud ho nevidíte, podívejte se na hlavní panel Windows — často je za Admin Studio.\n\n" +
            "Poznámka: Monitor/kiosk ukazují čisté video bez AI překryvu.",
            "Náhled detekce", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void OpenDebug(DetectorInstance instance) => _ = OpenDebugAsync(instance);

    public void RefreshStatuses()
    {
        foreach (var row in Items.ToList()) row.Refresh();
    }

    public DetectorInstance? PrimaryFallDetector =>
        Items.Select(i => i.Instance)
            .FirstOrDefault(i => i.DetectorType == "fall" && i.Enabled)
        ?? Items.Select(i => i.Instance).FirstOrDefault(i => i.DetectorType == "fall");
}

public sealed class DetectorRowViewModel : ObservableObject
{
    private readonly DetectorsViewModel _parent;
    private readonly DetectorProcessManager _manager;
    private string _status = "…";
    public DetectorRowViewModel(DetectorInstance instance, DetectorsViewModel parent, DetectorProcessManager manager)
    {
        Instance = instance; _parent = parent; _manager = manager;
        StartCommand = new AsyncRelayCommand(() => _parent.StartAsync(Instance, Instance.DebugOverlay));
        StopCommand = new AsyncRelayCommand(() => _parent.StopAsync(Instance));
        DebugCommand = new AsyncRelayCommand(() => _parent.OpenDebugAsync(Instance));
    }
    public DetectorInstance Instance { get; }
    public string Title => Instance.DisplayName;
    public string Subtitle =>
        $"{(Instance.DetectorType == "fall" ? "pád" : "bariéra")} · stream {Instance.InputStream} · model {Instance.Model} · ROI {Instance.RoiProfile}";
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand DebugCommand { get; }
    public void Refresh()
    {
        if (!Instance.Enabled) { Status = "VYPNUTO"; Detail = ""; return; }
        var pid = _manager.FindPid(Instance);
        Status = pid is not null ? "BĚŽÍ" : "ZASTAVENO";
        Detail = pid is not null ? $"PID {pid}" : "bez okna = START; s náhledem = Náhled videa";
    }

    private string _detail = "";
    public string Detail { get => _detail; private set => SetField(ref _detail, value); }
}
