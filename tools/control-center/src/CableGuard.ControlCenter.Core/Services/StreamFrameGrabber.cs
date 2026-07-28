using System.Diagnostics;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Grabs a single JPEG still from a local MediaMTX RTSP path via ffmpeg.
/// Used by ROI calibration so operators draw polygons on a real camera frame.
/// </summary>
public static class StreamFrameGrabber
{
    public sealed record Result(bool Ok, byte[]? JpegBytes, int WidthHint, int HeightHint, string Message);

    public static async Task<Result> GrabJpegAsync(
        string mediaMtxPath,
        string? rtspBase = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mediaMtxPath))
            return new Result(false, null, 0, 0, "Chybí MediaMTX path.");

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
            return new Result(false, null, 0, 0,
                "ffmpeg nenalezen v PATH. Nainstaluj ffmpeg, nebo otevři Náhled streamu v prohlížeči.");

        var baseUrl = (rtspBase ?? "rtsp://127.0.0.1:8554").TrimEnd('/');
        var url = $"{baseUrl}/{mediaMtxPath.TrimStart('/')}";
        var tmp = Path.Combine(Path.GetTempPath(), $"cg-roi-{Guid.NewGuid():N}.jpg");

        try
        {
            // FFmpeg 5+/7 uses -timeout (µs) as RTSP socket timeout; -stimeout was removed.
            var args =
                $"-hide_banner -loglevel error -y " +
                $"-rtsp_transport tcp -timeout 8000000 " +
                $"-i \"{url}\" -frames:v 1 -q:v 3 \"{tmp}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("ffmpeg se nespustil.");

            var errTask = proc.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(12));

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new Result(false, null, 0, 0,
                    $"Timeout při grabu snímku z {url}. Běží MediaMTX a je path ready?");
            }

            var err = await errTask.ConfigureAwait(false);
            if (proc.ExitCode != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length < 100)
            {
                var hint = string.IsNullOrWhiteSpace(err) ? $"exit={proc.ExitCode}" : Truncate(err, 240);
                return new Result(false, null, 0, 0,
                    $"Snímek z {url} se nepodařil ({hint}). Zkontroluj MediaMTX / stream.");
            }

            var bytes = await File.ReadAllBytesAsync(tmp, ct).ConfigureAwait(false);
            return new Result(true, bytes, 0, 0, $"OK {bytes.Length} B z {url}");
        }
        catch (Exception ex)
        {
            return new Result(false, null, 0, 0, ex.Message);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public static string? FindFfmpeg()
    {
        var fromPath = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (fromPath is not null) return fromPath;

        // Common winget / chocolatey locations on this host.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c) && c.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                return c;
            if (Directory.Exists(c))
            {
                try
                {
                    var hit = Directory.EnumerateFiles(c, "ffmpeg.exe", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (hit is not null) return hit;
                }
                catch { /* ignore */ }
            }
        }
        return null;
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), name);
                if (File.Exists(full)) return full;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    private static string Truncate(string s, int n)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= n ? s : s[..n] + "…";
    }
}
