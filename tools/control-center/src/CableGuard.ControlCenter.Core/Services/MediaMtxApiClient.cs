using System.Text;
using System.Text.Json;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>MediaMTX Control API client. Localhost only (127.0.0.1:9997) by contract.</summary>
public sealed class MediaMtxApiClient : IMediaMtxApi
{
    private readonly HttpClient _http;
    private readonly string _base;

    public MediaMtxApiClient(HttpClient http, string apiBase)
    {
        var uri = new Uri(apiBase);
        if (uri.Host is not ("127.0.0.1" or "localhost"))
            throw new ArgumentException("MediaMTX Control API must stay 127.0.0.1 only.", nameof(apiBase));
        _http = http;
        _base = apiBase.TrimEnd('/');
    }

    public async Task<bool?> IsPathReadyAsync(string pathName, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{_base}/v3/paths/get/{Uri.EscapeDataString(pathName)}", ct);
            if (!resp.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("ready", out var ready) && ready.GetBoolean();
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    public async Task<string?> GetConfiguredSourceAsync(string pathName, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{_base}/v3/config/paths/get/{Uri.EscapeDataString(pathName)}", ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("source", out var source) ? source.GetString() : null;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    public async Task<bool?> ConfigPathExistsAsync(string pathName, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{_base}/v3/config/paths/get/{Uri.EscapeDataString(pathName)}", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            if (!resp.IsSuccessStatusCode) return null;
            return true;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    public async Task<bool> PatchPathSourceAsync(string pathName, string source, CancellationToken ct = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { source });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await _http.PatchAsync($"{_base}/v3/config/paths/patch/{Uri.EscapeDataString(pathName)}", content, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    public async Task<bool> AddPathAsync(
        string pathName, string source, string? rtspTransport = "tcp", CancellationToken ct = default)
    {
        try
        {
            var transport = string.Equals(rtspTransport, "udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
            var body = JsonSerializer.Serialize(new
            {
                source,
                rtspTransport = transport,
                sourceOnDemand = false,
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(
                $"{_base}/v3/config/paths/add/{Uri.EscapeDataString(pathName)}", content, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    public async Task<bool> DeletePathAsync(string pathName, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.DeleteAsync(
                $"{_base}/v3/config/paths/delete/{Uri.EscapeDataString(pathName)}", ct);
            return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }
}
