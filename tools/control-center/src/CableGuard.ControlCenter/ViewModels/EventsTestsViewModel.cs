using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using CableGuard.ControlCenter.Core.Services;

namespace CableGuard.ControlCenter.ViewModels;

/// <summary>Events &amp; Tests — synthetic TEST alarms only (Gate 1 stubs; Gate 2 expands Monitor UX).</summary>
public sealed class EventsTestsViewModel : ObservableObject
{
    private readonly ControlCenterConfig _config;
    private readonly ControlCenterLogger _logger;
    private readonly SelectedCameraSession _session;
    private readonly HttpClient _http;
    private string _lastResult = "";

    public EventsTestsViewModel(
        ControlCenterConfig config,
        ControlCenterLogger logger,
        SelectedCameraSession session,
        HttpClient http)
    {
        _config = config;
        _logger = logger;
        _session = session;
        _http = http;
        TriggerTestAlarmCommand = new AsyncRelayCommand(() => TriggerAsync("TEST_ALARM"));
        TriggerTestFallCommand = new AsyncRelayCommand(() => TriggerAsync("FALL"));
        TestWebSocketCommand = new AsyncRelayCommand(TestWebSocketAsync);
        TestAudioHintCommand = new RelayCommand(() =>
            MessageBox.Show(
                "TEST AUDIO: otevřete Monitor a použijte tamní self-test, nebo Gate 2 audio control.\nControl Center nespouští kiosk audio přímo.",
                "TEST AUDIO", MessageBoxButton.OK, MessageBoxImage.Information));
        ClearTestEventsHintCommand = new RelayCommand(() =>
            MessageBox.Show(
                "CLEAR TEST EVENTS bude napojeno na Event Core filtr is_test=true (Gate 2/4).\nZatím smažte test eventy přes Event Core admin API / DB tool.",
                "CLEAR TEST EVENTS", MessageBoxButton.OK, MessageBoxImage.Information));
    }

    public string LastResult { get => _lastResult; private set => SetField(ref _lastResult, value); }
    public AsyncRelayCommand TriggerTestAlarmCommand { get; }
    public AsyncRelayCommand TriggerTestFallCommand { get; }
    public AsyncRelayCommand TestWebSocketCommand { get; }
    public RelayCommand TestAudioHintCommand { get; }
    public RelayCommand ClearTestEventsHintCommand { get; }

    private async Task TriggerAsync(string eventType)
    {
        var cam = _session.Selected;
        if (cam is null)
        {
            MessageBox.Show("Vyberte kameru (office-63).", "TEST EVENT", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.Equals(cam.Environment, "test", StringComparison.OrdinalIgnoreCase))
        {
            var warn = MessageBox.Show(
                "Vybraná kamera není označena jako TEST.\nOpravdu poslat test event?",
                "TEST EVENT", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (warn != MessageBoxResult.Yes) return;
        }

        var eventId = Guid.NewGuid().ToString("D");
        var payload = new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["event_type"] = eventType == "TEST_ALARM" ? "FALL" : eventType,
            ["camera_id"] = cam.CameraId,
            ["path_name"] = cam.MediaMtxPath,
            ["occurred_at"] = DateTime.UtcNow.ToString("o"),
            ["severity"] = "test",
            ["status"] = "NEW",
            ["is_test"] = true,
            ["risk_score"] = 0.91,
            ["track_id"] = "test-track",
            ["source_session_id"] = "test-session",
            ["clip_status"] = "NOT_REQUESTED",
        };

        // Best-effort POST to Event Core — contract may evolve in Gate 2.
        try
        {
            var url = $"{_config.EventCoreBaseLocal.TrimEnd('/')}/api/v1/events";
            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            // Ingest key from process env or platform .env — never log it.
            var key = PlatformEnvSecrets.TryGet(PlatformEnvSecrets.IngestApiKey, _config.PlatformRoot);
            if (string.IsNullOrWhiteSpace(key))
            {
                LastResult =
                    "Chybí CABLEGUARD_INGEST_API_KEY (process env nebo platform .env). " +
                    "Bez klíče Event Core vrací 401.";
                _logger.Warn("[TEST-EVENT] missing CABLEGUARD_INGEST_API_KEY");
                MessageBox.Show(LastResult, "TEST EVENT", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            req.Headers.TryAddWithoutValidation("X-API-Key", key);

            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            LastResult = $"HTTP {(int)resp.StatusCode} event_id={eventId} is_test=true camera={cam.CameraId}\n{TrimSafe(body)}";
            _logger.Info($"[TEST-EVENT] posted event_id={eventId} camera={cam.CameraId} status={(int)resp.StatusCode}");
            MessageBox.Show(LastResult, "TEST EVENT", MessageBoxButton.OK,
                resp.IsSuccessStatusCode ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            LastResult = $"FAILED: {ex.Message} (event_id={eventId})";
            _logger.Warn($"[TEST-EVENT] {ex.Message}");
            MessageBox.Show(LastResult, "TEST EVENT", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task TestWebSocketAsync()
    {
        try
        {
            var url = $"{_config.EventCoreBaseLocal.TrimEnd('/')}/health";
            using var resp = await _http.GetAsync(url);
            LastResult = $"Event Core health HTTP {(int)resp.StatusCode} — WS test deep-check in Gate 2.";
            MessageBox.Show(LastResult, "TEST WEBSOCKET", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LastResult = ex.Message;
            MessageBox.Show(LastResult, "TEST WEBSOCKET", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string TrimSafe(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        body = body.Length > 400 ? body[..400] + "…" : body;
        return body.Replace("rtsp://", "rtsp://***", StringComparison.OrdinalIgnoreCase);
    }
}
