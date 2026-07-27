using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

public sealed class VideoLabViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly VideoLabCollector _collector;
    private readonly Func<StreamsDocument> _streams;
    private readonly Func<CameraRegistryDocument> _cameras;
    private readonly IReadOnlyList<IComponentController> _components;
    private readonly DispatcherTimer _timer;

    private string _metricsStatus = "MediaMTX metrics: checking…";
    private string _selectedStreamId = "zahradky-horni-stanice";
    private string _liveSummary = "";
    private string _g2gDisplay = "GLASS-TO-GLASS LATENCY: NOT MEASURED";
    private string _detectorFreshness = "";
    private string _qualificationResult = "";
    private string _soakStatus = "";
    private string _manualLatencyText = "";
    private string _profileAudit = "";
    private string _resourceText = "NOT AVAILABLE — click Refresh resources";
    private string _abReport = "";
    private bool _soakRunning;

    public VideoLabViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        VideoLabCollector collector,
        Func<StreamsDocument> streams,
        Func<CameraRegistryDocument> cameras,
        IReadOnlyList<IComponentController> components)
    {
        _config = config;
        _logger = logger;
        _collector = collector;
        _streams = streams;
        _cameras = cameras;
        _components = components;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenProbeCommand = new RelayCommand(OpenProbe);
        OpenLatencyPatternCommand = new RelayCommand(OpenLatencyPattern);
        RecordManualLatencyCommand = new RelayCommand(RecordManualLatency);
        EnableMetricsCommand = new RelayCommand(EnableMetrics);
        RunQualificationCommand = new AsyncRelayCommand(RunQualificationAsync);
        StartSoak1MinCommand = new AsyncRelayCommand(() => RunSoakAsync(TimeSpan.FromMinutes(1)));
        StartSoak15MinCommand = new AsyncRelayCommand(() => RunSoakAsync(TimeSpan.FromMinutes(15)));
        StartSoak30MinCommand = new AsyncRelayCommand(() => RunSoakAsync(TimeSpan.FromMinutes(30)));
        StartSoak1hCommand = new AsyncRelayCommand(() => RunSoakAsync(TimeSpan.FromHours(1)));
        StartSoak4hCommand = new AsyncRelayCommand(() => RunSoakAsync(TimeSpan.FromHours(4)));
        InjectRestartMediaMtxCommand = new AsyncRelayCommand(() => InjectAsync(ComponentId.MediaMtx));
        InjectRestartDetectorCommand = new AsyncRelayCommand(() => InjectAsync(ComponentId.Detector));
        InjectRestartEventCoreCommand = new AsyncRelayCommand(() => InjectAsync(ComponentId.EventCore));
        InjectRestartMonitorCommand = new AsyncRelayCommand(() => InjectAsync(ComponentId.Monitor));
        AuditCameraCommand = new AsyncRelayCommand(AuditCameraAsync);
        RefreshResourcesCommand = new RelayCommand(RefreshResources);
        RunAbBenchmarkCommand = new AsyncRelayCommand(RunAbBenchmarkAsync);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => { if (!_soakRunning) await RefreshAsync(); };
        _timer.Start();
        StartProbeListener();
        _ = RefreshAsync();
    }

    public ObservableCollection<string> StreamIds { get; } = new();
    public string SelectedStreamId
    {
        get => _selectedStreamId;
        set { if (SetField(ref _selectedStreamId, value)) _ = RefreshAsync(); }
    }
    public string MetricsStatus { get => _metricsStatus; private set => SetField(ref _metricsStatus, value); }
    public string LiveSummary { get => _liveSummary; private set => SetField(ref _liveSummary, value); }
    public string G2GDisplay { get => _g2gDisplay; private set => SetField(ref _g2gDisplay, value); }
    public string DetectorFreshnessText { get => _detectorFreshness; private set => SetField(ref _detectorFreshness, value); }
    public string QualificationResult { get => _qualificationResult; private set => SetField(ref _qualificationResult, value); }
    public string SoakStatus { get => _soakStatus; private set => SetField(ref _soakStatus, value); }
    public string ManualLatencyText { get => _manualLatencyText; set => SetField(ref _manualLatencyText, value); }
    public string ProfileAudit { get => _profileAudit; private set => SetField(ref _profileAudit, value); }
    public string ResourceText { get => _resourceText; private set => SetField(ref _resourceText, value); }
    public string AbReport { get => _abReport; private set => SetField(ref _abReport, value); }
    public string AutomatedLatencyStatus { get; } =
        "EXPERIMENTAL — automated Gray-code/marker decode not validated; results MUST NOT be shown as authoritative. Use Manual Latency Test.";

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenProbeCommand { get; }
    public RelayCommand OpenLatencyPatternCommand { get; }
    public RelayCommand RecordManualLatencyCommand { get; }
    public RelayCommand EnableMetricsCommand { get; }
    public AsyncRelayCommand RunQualificationCommand { get; }
    public AsyncRelayCommand StartSoak1MinCommand { get; }
    public AsyncRelayCommand StartSoak15MinCommand { get; }
    public AsyncRelayCommand StartSoak30MinCommand { get; }
    public AsyncRelayCommand StartSoak1hCommand { get; }
    public AsyncRelayCommand StartSoak4hCommand { get; }
    public AsyncRelayCommand InjectRestartMediaMtxCommand { get; }
    public AsyncRelayCommand InjectRestartDetectorCommand { get; }
    public AsyncRelayCommand InjectRestartEventCoreCommand { get; }
    public AsyncRelayCommand InjectRestartMonitorCommand { get; }
    public AsyncRelayCommand AuditCameraCommand { get; }
    public RelayCommand RefreshResourcesCommand { get; }
    public AsyncRelayCommand RunAbBenchmarkCommand { get; }

    public StreamLiveMetrics? LastMetrics { get; private set; }
    public BrowserProbeStats? LastProbe { get; private set; }

    public async Task RefreshAsync()
    {
        var streams = _streams();
        StreamIds.Clear();
        foreach (var s in streams.Streams) StreamIds.Add(s.StreamId);
        if (StreamIds.Count == 0) StreamIds.Add(_config.ProductionStream);
        if (!StreamIds.Contains(SelectedStreamId) && StreamIds.Count > 0)
            SelectedStreamId = StreamIds[0];

        TryLoadProbeFile();
        var stream = streams.Streams.FirstOrDefault(s => s.StreamId == SelectedStreamId)
                     ?? new LogicalStream { StreamId = SelectedStreamId, MediaMtxPath = SelectedStreamId, CameraId = "" };
        var metrics = await _collector.CollectAsync(stream, stream.CameraId);
        if (LastProbe is not null && LastProbe.StreamId == SelectedStreamId)
            _collector.ApplyBrowserProbe(metrics, LastProbe);
        LastMetrics = metrics;

        LiveSummary =
            $"VIDEO HEALTH: {metrics.Health}\n{metrics.HealthDetail}\n\n" +
            $"TRANSPORT HEALTH\n" +
            $"  path ready: {metrics.PathReady}\n" +
            $"  ICE: {metrics.IceState}\n" +
            $"  received FPS: {metrics.ReceivedFps}\n" +
            $"  bitrate: {metrics.BitrateKbps}\n" +
            $"  frames received: {metrics.FramesReceived}\n" +
            $"  packet loss: {metrics.PacketLoss}\n" +
            $"  jitter: {metrics.JitterMs}\n" +
            $"  RTT: {metrics.RttMs}\n" +
            $"  freezes: {metrics.FreezeCount}\n" +
            $"  reconnects: {metrics.ReconnectCount}\n" +
            $"  since last frame: {metrics.SecondsSinceLastFrame}\n" +
            $"  MediaMTX bytes rx/tx: {metrics.MediaMtxBytesReceived} / {metrics.MediaMtxBytesSent}\n" +
            $"  readers: {metrics.MediaMtxReaders}\n\n" +
            $"CAMERA PROFILE\n" +
            $"  camera: {metrics.SourceCameraId}\n" +
            $"  codec: {(string.IsNullOrEmpty(metrics.Codec.Note) ? metrics.Codec.ToString() : metrics.Codec.Note)}\n" +
            $"  resolution: {metrics.ResolutionWidth} x {metrics.ResolutionHeight}\n\n" +
            $"GLASS-TO-GLASS\n  {metrics.GlassToGlassLatencyMs}\n\n" +
            $"DETECTOR FRESHNESS (≠ G2G)\n  see panel below";

        G2GDisplay = metrics.GlassToGlassLatencyMs.Kind == MeasurementKind.NotMeasured
            ? "GLASS-TO-GLASS LATENCY: NOT MEASURED"
            : $"GLASS-TO-GLASS LATENCY: {metrics.GlassToGlassLatencyMs} (MANUAL)";

        var fresh = DetectorFreshnessProvider.Get("fall-zahradky-upper");
        DetectorFreshnessText =
            $"{fresh.Detail}\ninput FPS: {fresh.InputFps}\ninference FPS: {fresh.InferenceFps}\nqueue age: {fresh.QueueAgeMs}\nbacklog: {fresh.BacklogDetected}";

        var (ok, body, detail) = await new MediaMtxMetricsClient(new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            .TryGetMetricsAsync();
        MetricsStatus = ok
            ? $"MediaMTX metrics OK ({MediaMtxMetricsParser.ListMetricNames(body!).Count} metric names) @ 127.0.0.1:9998"
            : $"MediaMTX metrics: {detail}";
    }

    private void OpenProbe()
    {
        var html = Path.Combine(_config.PlatformRoot, "tools", "control-center", "assets", "video-lab-probe.html");
        if (!File.Exists(html))
        {
            MessageBox.Show("Probe HTML missing: " + html);
            return;
        }
        var url = $"file:///{html.Replace('\\', '/')}?path={Uri.EscapeDataString(SelectedStreamId)}&port=18990";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        _logger.Info($"[VIDEO LAB] Opened WHEP probe for {SelectedStreamId}");
    }

    private void OpenLatencyPattern()
    {
        var html = Path.Combine(_config.PlatformRoot, "tools", "control-center", "assets", "latency-pattern.html");
        if (!File.Exists(html))
        {
            MessageBox.Show("Latency pattern HTML missing.");
            return;
        }
        Process.Start(new ProcessStartInfo($"file:///{html.Replace('\\', '/')}") { UseShellExecute = true });
        _logger.Info("[VIDEO LAB] Opened manual latency pattern (fullscreen recommended)");
        MessageBox.Show(
            "1) Put the pattern on a display in the camera's view.\n" +
            "2) Watch the received stream (Preview / probe).\n" +
            "3) Enter measured latency (ms) below and click Record Manual Latency.\n\n" +
            "This is a MANUAL MEASUREMENT. OCR is not used as sole source.",
            "Manual Latency Test");
    }

    private void RecordManualLatency()
    {
        if (!double.TryParse(ManualLatencyText.Trim(), out var ms) || ms < 0 || ms > 60_000)
        {
            MessageBox.Show("Enter latency in milliseconds (0–60000).");
            return;
        }
        _collector.RecordManualLatency(SelectedStreamId, ms, "manual visual comparison");
        _logger.Info($"[VIDEO LAB] Manual G2G sample {ms} ms for {SelectedStreamId}");
        _ = RefreshAsync();
    }

    private void EnableMetrics()
    {
        if (!MediaMtxMetricsEnabler.EnsureLocalhostMetrics(_config.MediaMtxLocalYml, out var msg))
        {
            MessageBox.Show(msg);
            return;
        }
        MessageBox.Show(msg + "\n\nRestart MediaMTX from Services/Overview for metrics to bind on 127.0.0.1:9998.");
        _logger.Info("[VIDEO LAB] " + msg);
    }

    private async Task RunQualificationAsync()
    {
        await RefreshAsync();
        var m = LastMetrics ?? new StreamLiveMetrics();
        var cams = _cameras();
        var cam = cams.Cameras.FirstOrDefault(c => c.CameraId == m.SourceCameraId);
        var fp = ConfigFingerprint.Compute(ConfigFingerprint.BuildQualificationPayload(
            m.SourceCameraId, cam?.Profile ?? "", m.Codec.Note, null, null, null,
            m.MediaMtxPath, cam?.Transport ?? "tcp", "fall-zahradky-upper", null, null, null));
        var g2g = _collector.GlassToGlassSamples.LastOrDefault(s => s.StreamId == SelectedStreamId && s.IsAuthoritative);
        var report = QualificationEngine.Evaluate(
            m, _collector.Thresholds, g2g is not null, g2g?.LatencyMs, fp);

        var dir = VideoLabReportWriter.CreateRunDirectory(_config.PlatformRoot, report.TestId);
        VideoLabReportWriter.WriteJson(Path.Combine(dir, "summary.json"), new
        {
            report.TestId,
            verdict = report.VerdictLabel,
            reasons = report.Reasons,
            fingerprint = report.ConfigFingerprint,
            glass_to_glass = g2g is null ? "NOT MEASURED" : $"{g2g.LatencyMs} ms MANUAL",
            thresholds_label = _collector.Thresholds.Label,
            stream = SelectedStreamId,
            mediamtx_version = "1.11.3",
        });
        VideoLabReportWriter.WriteJson(Path.Combine(dir, "metadata.json"), new
        {
            started = DateTime.UtcNow,
            platform_root = "cableguard-platform",
            note = "Secrets excluded. ENGINEERING / PROVISIONAL — not safety certified.",
        });

        QualificationResult = $"{report.VerdictLabel}\n" + string.Join("\n", report.Reasons) +
                              $"\nFingerprint: {fp[..16]}…\nReport: {dir}";
        _logger.Info($"[VIDEO LAB] Qualification {report.VerdictLabel} → {dir}");
    }

    private async Task RunSoakAsync(TimeSpan duration)
    {
        if (_soakRunning) return;
        _soakRunning = true;
        var testId = $"soak-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var dir = VideoLabReportWriter.CreateRunDirectory(_config.PlatformRoot, testId);
        var csv = Path.Combine(dir, "samples.csv");
        VideoLabReportWriter.AppendCsv(csv, "utc,fps,bitrate_kbps,bytes_rx,health", "");
        var fpsSamples = new List<double>();
        var bitrateSamples = new List<double>();
        var end = DateTime.UtcNow + duration;
        SoakStatus = $"Soak running ({duration.TotalMinutes:0} min) → {dir}";
        try
        {
            while (DateTime.UtcNow < end)
            {
                await RefreshAsync();
                var m = LastMetrics;
                var fps = m?.ReceivedFps.Kind == MeasurementKind.Measured ? m.ReceivedFps.Value : null;
                var br = m?.BitrateKbps.Kind == MeasurementKind.Measured ? m.BitrateKbps.Value : null;
                if (fps is not null) fpsSamples.Add(fps.Value);
                if (br is not null) bitrateSamples.Add(br.Value);
                VideoLabReportWriter.AppendCsv(csv,
                    "utc,fps,bitrate_kbps,bytes_rx,health",
                    $"{DateTime.UtcNow:o},{fps},{br},{m?.MediaMtxBytesReceived.Value},{m?.Health}");
                await Task.Delay(5000);
            }
            var summary = new
            {
                testId,
                duration_min = duration.TotalMinutes,
                fps = SoakStatisticsCalculator.Compute("received_fps", fpsSamples, "browser probe if available"),
                bitrate = SoakStatisticsCalculator.Compute("bitrate_kbps", bitrateSamples, "MediaMTX delta"),
                latency_drift = "NOT COMPUTED — no authoritative G2G time series",
            };
            VideoLabReportWriter.WriteJson(Path.Combine(dir, "summary.json"), summary);
            SoakStatus = $"Soak complete. FPS samples={fpsSamples.Count}. See {dir}";
        }
        finally { _soakRunning = false; }
    }

    private async Task InjectAsync(ComponentId id)
    {
        if (MessageBox.Show(
                $"FAILURE INJECTION (TEST MODE)\n\nRestart {id}?\nThis will disrupt the live system.",
                "Failure injection", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        var c = _components.FirstOrDefault(x => x.Id == id);
        if (c is null) return;
        _logger.Info($"[VIDEO LAB][FAULT] Restart {id}");
        var t0 = DateTime.UtcNow;
        await c.StopAsync();
        await Task.Delay(1500);
        await c.StartAsync();
        var dt = (DateTime.UtcNow - t0).TotalSeconds;
        MessageBox.Show($"Restart {id} finished in {dt:0.0}s. Watch Video Lab health for recovery to REALTIME.", "Fault injection");
        await RefreshAsync();
    }

    private async Task AuditCameraAsync()
    {
        var cams = _cameras();
        var stream = _streams().Streams.FirstOrDefault(s => s.StreamId == SelectedStreamId);
        var cam = cams.Cameras.FirstOrDefault(c => c.CameraId == (stream?.CameraId ?? ""));
        if (cam is null) { ProfileAudit = "No camera mapped to selected stream."; return; }
        ProfileAudit = await CameraProfileInspector.InspectAsync(cam.Host, cam.RtspPort);
        ProfileAudit += "\n\nNever treat PTS/DTS as UTC capture timestamp unless the camera provides correlated time.";
    }

    private void RefreshResources()
    {
        var pids = new Dictionary<string, int?>();
        foreach (var c in _components)
        {
            try
            {
                var snap = c.GetStatusAsync().GetAwaiter().GetResult();
                pids[c.DisplayName] = snap.ProcessId;
            }
            catch { pids[c.DisplayName] = null; }
        }
        var snapRes = ResourceMonitor.Capture(pids);
        var lines = new List<string>
        {
            "RESOURCE MONITOR (working set only; CPU%/GPU/VRAM NOT AVAILABLE without PerformanceCounter/NVML)",
            $"System CPU: {(snapRes.SystemCpuPercent?.ToString("0.0") ?? "NOT AVAILABLE")}",
            $"System RAM: {(snapRes.SystemRamUsedMb?.ToString("0") ?? "NOT AVAILABLE")} / {(snapRes.SystemRamTotalMb?.ToString("0") ?? "?")} MB",
        };
        foreach (var p in snapRes.Processes.Values)
            lines.Add($"  {p.Name}: PID={p.Pid?.ToString() ?? "—"}  WS={(p.WorkingSetMb?.ToString("0.0") ?? "—")} MB  CPU={(p.CpuPercent?.ToString("0.0") ?? "NOT AVAILABLE")}");
        ResourceText = string.Join("\n", lines);
    }

    private async Task RunAbBenchmarkAsync()
    {
        var streams = _streams().Streams.ToList();
        var targets = streams.Where(s =>
            s.StreamId.Contains("92", StringComparison.Ordinal) ||
            s.StreamId.Contains("90", StringComparison.Ordinal) ||
            s.StreamId.Contains("office", StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0)
            targets = streams.Take(2).ToList();
        if (targets.Count == 0)
        {
            AbReport = "No streams configured for A/B.";
            return;
        }

        var lines = new List<string>
        {
            "A/B CAMERA BENCHMARK (transport metrics only)",
            "G2G latency shown ONLY if Latency Test was recorded for that stream.",
            ""
        };
        foreach (var s in targets)
        {
            var m = await _collector.CollectAsync(s, s.CameraId);
            var g2g = _collector.GlassToGlassSamples.LastOrDefault(x => x.StreamId == s.StreamId && x.IsAuthoritative);
            lines.Add($"--- {s.StreamId} (cam={s.CameraId}) ---");
            lines.Add($"  path ready: {m.PathReady}");
            lines.Add($"  bitrate: {m.BitrateKbps}");
            lines.Add($"  received FPS: {m.ReceivedFps}");
            lines.Add($"  freezes: {m.FreezeCount}  reconnects: {m.ReconnectCount}");
            lines.Add($"  RTT/jitter: {m.RttMs} / {m.JitterMs}");
            lines.Add($"  G2G: {(g2g is null ? "NOT MEASURED" : $"{g2g.LatencyMs} ms MANUAL")}");
            lines.Add("");
        }
        AbReport = string.Join("\n", lines);

        var dir = VideoLabReportWriter.CreateRunDirectory(_config.PlatformRoot, $"ab-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        VideoLabReportWriter.WriteJson(Path.Combine(dir, "summary.json"), new { report = AbReport, note = "ENGINEERING" });
        AbReport += $"\nReport: {dir}";
    }

    private void StartProbeListener()
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_config.PlatformRoot, "runtime", "video-lab"));
            // File-based probe only — HttpListener/TcpListener on this host proved unreliable
            // (http.sys URLACL / SYN hang). Probe HTML downloads probe-stats.json for drop-in.
            _logger.Info("[VIDEO LAB] Probe input: runtime/video-lab/probe-stats.json (drop file from WHEP probe download)");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[VIDEO LAB] Probe dir: {ex.Message}");
        }
    }

    private void TryLoadProbeFile()
    {
        try
        {
            var path = Path.Combine(_config.PlatformRoot, "runtime", "video-lab", "probe-stats.json");
            if (!File.Exists(path)) return;
            var body = File.ReadAllText(path);
            var stats = JsonSerializer.Deserialize<BrowserProbeStats>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (stats is not null) LastProbe = stats;
        }
        catch { /* ignore */ }
    }
}
