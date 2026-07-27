using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Threading;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

public sealed record LogSource(string Name, string FilePath);

public sealed class LogsViewModel : ObservableObject
{
    private readonly LogTailer _tailer = new();
    private readonly DispatcherTimer _timer;
    private readonly List<string> _allLines = new();

    private LogSource? _selectedSource;
    private string _filterText = "";
    private bool _errorsOnly;
    private bool _liveTail = true;
    private string _visibleText = "";

    public LogsViewModel(ControlCenterConfig config)
    {
        Sources = new ObservableCollection<LogSource>
        {
            new("Control Center", Path.Combine(config.LogsDir, "control-center.log")),
            new("MediaMTX", Path.Combine(config.PlatformRoot, "runtime", "mediamtx", "mediamtx.out.log")),
            new("MediaMTX (err)", Path.Combine(config.PlatformRoot, "runtime", "mediamtx", "mediamtx.err.log")),
            new("Event Core", Path.Combine(config.PlatformRoot, "runtime", "event-core", "event-core.err.log")),
            new("Monitor", Path.Combine(config.MonitorRoot, "runtime", "monitor.out.log")),
            new("Monitor (err)", Path.Combine(config.MonitorRoot, "runtime", "monitor.err.log")),
            new("Detector", Path.Combine(config.DetectorRoot, "runtime", "detector.out.log")),
        };
        _selectedSource = Sources[0];

        ClearViewCommand = new RelayCommand(() => { _allLines.Clear(); RebuildVisible(); });
        OpenFolderCommand = new RelayCommand(OpenFolder);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { if (_liveTail) Poll(); };
        _timer.Start();
        Poll();
    }

    public ObservableCollection<LogSource> Sources { get; }

    public LogSource? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetField(ref _selectedSource, value))
            {
                _tailer.Reset();
                _allLines.Clear();
                Poll();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set { if (SetField(ref _filterText, value)) RebuildVisible(); }
    }

    public bool ErrorsOnly
    {
        get => _errorsOnly;
        set { if (SetField(ref _errorsOnly, value)) RebuildVisible(); }
    }

    public bool LiveTail { get => _liveTail; set => SetField(ref _liveTail, value); }
    public string VisibleText { get => _visibleText; private set => SetField(ref _visibleText, value); }

    public RelayCommand ClearViewCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    private void Poll()
    {
        if (_selectedSource is null) return;
        var newLines = _tailer.ReadNewLines(_selectedSource.FilePath);
        if (newLines.Count == 0 && _allLines.Count > 0) return;
        _allLines.AddRange(newLines);
        if (_allLines.Count > 5000)
            _allLines.RemoveRange(0, _allLines.Count - 5000);
        RebuildVisible();
    }

    private static readonly string[] ErrorMarkers = { "ERROR", "ERR", "FAIL", "EXCEPTION", "TRACEBACK", "FATAL" };

    private void RebuildVisible()
    {
        IEnumerable<string> lines = _allLines;
        if (_errorsOnly)
            lines = lines.Where(l => ErrorMarkers.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(_filterText))
            lines = lines.Where(l => l.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        foreach (var line in lines) sb.AppendLine(line);
        VisibleText = sb.Length > 0
            ? sb.ToString()
            : (_selectedSource is not null && !File.Exists(_selectedSource.FilePath)
                ? $"(log file does not exist yet: {_selectedSource.FilePath})"
                : "");
    }

    private void OpenFolder()
    {
        var dir = _selectedSource is null ? null : Path.GetDirectoryName(_selectedSource.FilePath);
        if (dir is not null && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private string _saveResult = "";

    public SettingsViewModel(ControlCenterConfig config)
    {
        _config = config;
        SaveCommand = new RelayCommand(Save);
    }

    public string PlatformRoot { get => _config.PlatformRoot; set { _config.PlatformRoot = value; OnPropertyChanged(); } }
    public string MonitorRoot { get => _config.MonitorRoot; set { _config.MonitorRoot = value; OnPropertyChanged(); } }
    public string DetectorRoot { get => _config.DetectorRoot; set { _config.DetectorRoot = value; OnPropertyChanged(); } }
    public string LanHost { get => _config.LanHost; set { _config.LanHost = value; OnPropertyChanged(); } }
    public string ProductionStream { get => _config.ProductionStream; set { _config.ProductionStream = value; OnPropertyChanged(); } }
    public string DetectorStartCommand { get => _config.DetectorStartCommand; set { _config.DetectorStartCommand = value; OnPropertyChanged(); } }
    public string SaveResult { get => _saveResult; private set => SetField(ref _saveResult, value); }

    public RelayCommand SaveCommand { get; }

    private void Save()
    {
        try
        {
            _config.Save();
            SaveResult = $"Uloženo do {_config.ConfigJsonPath}. Změny se plně projeví po restartu Control Center.";
        }
        catch (Exception ex)
        {
            SaveResult = $"Uložení selhalo: {ex.Message}";
        }
    }
}
