namespace CableGuard.ControlCenter.Core.Services;

public sealed class HttpProber : IHttpProber
{
    private readonly HttpClient _http;

    public HttpProber(HttpClient http) => _http = http;

    public async Task<int?> GetStatusCodeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)resp.StatusCode;
        }
        catch (Exception) { return null; } // probe — connection reset / timeout / DNS = offline
    }

    public async Task<int?> OptionsStatusCodeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Options, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)resp.StatusCode;
        }
        catch (Exception) { return null; }
    }

    public async Task<string?> GetBodyAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception) { return null; }
    }
}
