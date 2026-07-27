using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

public sealed class CalibrationViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private string _profileId = "office-fall-test";
    private string _pointsText = "";
    private string _status = "Klikáním do plátna přidávej body polygonu (min. 3).";

    public CalibrationViewModel(ControlCenterConfig config, ControlCenterLogger logger)
    {
        _config = config; _logger = logger;
        Points = new ObservableCollection<RoiPoint>();
        ResetCommand = new RelayCommand(() => { Points.Clear(); Rebuild(); });
        FullFrameCommand = new RelayCommand(() =>
        {
            Points.Clear();
            Points.Add(new(0, 0)); Points.Add(new(1280, 0)); Points.Add(new(1280, 720)); Points.Add(new(0, 720));
            Rebuild();
        });
        SaveCommand = new RelayCommand(Save);
        LoadCommand = new RelayCommand(Load);
        CanvasClickCommand = new RelayCommand<Point?>(AddPoint);
        ReloadList();
    }

    public ObservableCollection<RoiPoint> Points { get; }
    public ObservableCollection<string> ProfileIds { get; } = new();
    public string ProfileId { get => _profileId; set => SetField(ref _profileId, value); }
    public string PointsText { get => _pointsText; private set => SetField(ref _pointsText, value); }
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public RelayCommand ResetCommand { get; }
    public RelayCommand FullFrameCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand<Point?> CanvasClickCommand { get; }

    public void AddPoint(Point? p)
    {
        if (p is null) return;
        Points.Add(new RoiPoint((int)p.Value.X, (int)p.Value.Y));
        Rebuild();
    }

    private void Rebuild()
    {
        PointsText = string.Join("\n", Points.Select((pt, i) => $"{i + 1}: {pt.X}, {pt.Y}"));
        Status = Points.Count < 3 ? $"Bodů: {Points.Count} (potřeba ≥3)" : $"Bodů: {Points.Count} — ready to save";
    }

    private void Save()
    {
        var profile = new RoiProfile
        {
            Id = ProfileId.Trim(),
            DisplayName = ProfileId.Trim(),
            DetectorType = "fall",
            Points = Points.ToList(),
        };
        try
        {
            RoiProfileService.Save(profile, RuntimeConfigPaths.RoiFile(_config, profile.Id));
            _logger.Info($"[ROI] Saved profile {profile.Id} ({profile.Points.Count} points)");
            Status = $"Uloženo: {profile.Id}";
            ReloadList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ROI Save", MessageBoxButton.OK, MessageBoxImage.Error);
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
        Points.Clear();
        foreach (var p in profile.Points) Points.Add(p);
        Rebuild();
        Status = $"Načteno: {profile.Id}";
    }

    private void ReloadList()
    {
        ProfileIds.Clear();
        foreach (var p in RoiProfileService.ListAll(_config.RoiDir))
            ProfileIds.Add(p.Id);
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

    public HardwareViewModel(ControlCenterLogger logger, IHardwareAdapter adapter)
    {
        _logger = logger; _adapter = adapter;
        _status = adapter.StatusDetail;
        Pulse1Command = new AsyncRelayCommand(() => Pulse(1));
        Pulse2Command = new AsyncRelayCommand(() => Pulse(2));
        Pulse3Command = new AsyncRelayCommand(() => Pulse(3));
        GreenCommand = new AsyncRelayCommand(() => Semaphore("green"));
        RedCommand = new AsyncRelayCommand(() => Semaphore("red"));
        BuzzerCommand = new AsyncRelayCommand(() => Semaphore("buzzer"));
        AllOffCommand = new AsyncRelayCommand(AllOff);
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
            }
        }
    }
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string Banner => "⚠ HARDWARE TEST MODE — manuální zásahy jen s potvrzením, auto-off, žádná vazba detector→relay";
    public AsyncRelayCommand Pulse1Command { get; }
    public AsyncRelayCommand Pulse2Command { get; }
    public AsyncRelayCommand Pulse3Command { get; }
    public AsyncRelayCommand GreenCommand { get; }
    public AsyncRelayCommand RedCommand { get; }
    public AsyncRelayCommand BuzzerCommand { get; }
    public AsyncRelayCommand AllOffCommand { get; }

    private async Task Pulse(int ch)
    {
        if (!Confirm($"Pulse relay {ch} (max 2 s)?")) return;
        try
        {
            HardwareSafety.EnsureTestMode(_adapter);
            await _adapter.PulseRelayAsync(ch, HardwareSafety.ClampPulse(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)));
            _logger.Info($"[HW] Pulse relay {ch} OK");
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            MessageBox.Show(ex.Message, "Hardware", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task Semaphore(string color)
    {
        if (!Confirm($"Set semaphore {color}?")) return;
        try
        {
            HardwareSafety.EnsureTestMode(_adapter);
            await _adapter.SetSemaphoreAsync(color, true);
            _logger.Info($"[HW] Semaphore {color} ON");
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
            _logger.Info("[HW] ALL OFF");
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
    }

    public ObservableCollection<ScenarioDocument> Items { get; } = new();
    public ScenarioDocument? Selected { get; set; }
    public string DiffText { get => _diffText; private set => SetField(ref _diffText, value); }
    public AsyncRelayCommand RunCommand { get; }

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
                Id = "office-fall-test", DisplayName = "Office fall detection",
                Description = "Office camera, fall detector, debug ON, Telegram OFF",
                StreamId = "office-test", DetectorIds = { "fall-office-test" },
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
        if (MessageBox.Show($"RUN SCENARIO: {Selected.DisplayName}?\n\n{DiffText}",
                "Scenario", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _logger.Info($"[SCENARIO] RUN {Selected.Id}");
        if (Selected.HardwareTest) _hardware.TestMode = true;
        _notifications.TelegramEnabled = Selected.Telegram;
        foreach (var row in _detectors.Items)
        {
            var want = Selected.DetectorIds.Contains(row.Instance.Id, StringComparer.OrdinalIgnoreCase);
            if (want) await _detectors.StartAsync(row.Instance, Selected.DebugOverlay || row.Instance.DebugOverlay);
            else await _detectors.StopAsync(row.Instance);
        }
        MessageBox.Show("Scenario applied (best-effort). Check Detectors / Hardware / Notifications.", "Scenario");
    }
}
