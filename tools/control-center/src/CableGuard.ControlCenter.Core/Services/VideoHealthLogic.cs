using CableGuard.ControlCenter.Core.Models;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Engineering video health. REALTIME requires continuing frames + path ready + not stale.
/// ICE connected alone is NEVER enough.
/// </summary>
public static class VideoHealthEvaluator
{
    public static VideoHealthState Evaluate(
        bool pathReady,
        bool iceConnected,
        double? secondsSinceLastFrame,
        double? receivedFps,
        EngineeringThresholds thresholds,
        out string detail)
    {
        if (!pathReady)
        {
            detail = "MediaMTX path not READY";
            return VideoHealthState.Offline;
        }

        if (secondsSinceLastFrame is null && receivedFps is null && !iceConnected)
        {
            detail = "Path ready but no transport samples yet";
            return VideoHealthState.Unknown;
        }

        if (secondsSinceLastFrame.HasValue && secondsSinceLastFrame.Value >= 10)
        {
            detail = $"No new frame for {secondsSinceLastFrame:0.0}s (STALE)";
            return VideoHealthState.Stale;
        }

        if (secondsSinceLastFrame.HasValue && secondsSinceLastFrame.Value > thresholds.MaxSecondsSinceLastFrame)
        {
            detail = $"Frame gap {secondsSinceLastFrame:0.0}s > engineering max {thresholds.MaxSecondsSinceLastFrame}s";
            return VideoHealthState.Degraded;
        }

        // Connected but frozen: ICE up, but no frames / zero FPS
        if (iceConnected && receivedFps is 0)
        {
            detail = "ICE connected but received FPS is 0 — not REALTIME";
            return VideoHealthState.Stale;
        }

        if (iceConnected && secondsSinceLastFrame is null && receivedFps is null)
        {
            detail = "ICE connected but frame continuity not verified — UNKNOWN (not REALTIME)";
            return VideoHealthState.Unknown;
        }

        if (receivedFps.HasValue && receivedFps.Value < thresholds.MinReceivedFps)
        {
            detail = $"Received FPS {receivedFps:0.0} below engineering min {thresholds.MinReceivedFps}";
            return VideoHealthState.Degraded;
        }

        if (pathReady && (receivedFps is null || receivedFps >= thresholds.MinReceivedFps) &&
            (secondsSinceLastFrame is null || secondsSinceLastFrame <= thresholds.MaxSecondsSinceLastFrame) &&
            (iceConnected || receivedFps is not null))
        {
            // Require either measured FPS continuity OR verified recent frame; ICE alone insufficient (handled above).
            if (receivedFps is not null || secondsSinceLastFrame is not null)
            {
                detail = "Path ready, frames continuing (engineering REALTIME)";
                return VideoHealthState.Realtime;
            }
        }

        detail = "Insufficient evidence for REALTIME";
        return VideoHealthState.Unknown;
    }
}

public static class SoakStatisticsCalculator
{
    public static SoakStatistics Compute(string name, IReadOnlyList<double> samples, string note = "")
    {
        if (samples.Count == 0)
            return new SoakStatistics { MetricName = name, Note = note + " (no samples)" };

        var sorted = samples.OrderBy(x => x).ToArray();
        return new SoakStatistics
        {
            MetricName = name,
            Mean = samples.Average(),
            Median = Percentile(sorted, 0.50),
            P95 = Percentile(sorted, 0.95),
            P99 = Percentile(sorted, 0.99),
            Max = sorted[^1],
            Min = sorted[0],
            Note = note,
        };
    }

    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0) return double.NaN;
        if (sortedAscending.Count == 1) return sortedAscending[0];
        var idx = (sortedAscending.Count - 1) * p;
        var lo = (int)Math.Floor(idx);
        var hi = (int)Math.Ceiling(idx);
        if (lo == hi) return sortedAscending[lo];
        var w = idx - lo;
        return sortedAscending[lo] * (1 - w) + sortedAscending[hi] * w;
    }

    /// <summary>Positive slope of simple linear regression — latency drift only when G2G samples exist.</summary>
    public static double? LatencyDriftMsPerMinute(IReadOnlyList<(double Minutes, double LatencyMs)> points)
    {
        if (points.Count < 2) return null;
        var n = points.Count;
        var sumX = points.Sum(p => p.Minutes);
        var sumY = points.Sum(p => p.LatencyMs);
        var sumXy = points.Sum(p => p.Minutes * p.LatencyMs);
        var sumXx = points.Sum(p => p.Minutes * p.Minutes);
        var denom = n * sumXx - sumX * sumX;
        if (Math.Abs(denom) < 1e-9) return null;
        return (n * sumXy - sumX * sumY) / denom;
    }
}

public static class StaleFrameDetector
{
    public static bool IsBacklog(IReadOnlyList<double> queueAgeMsSeries, double risingThresholdMs = 500)
    {
        if (queueAgeMsSeries.Count < 5) return false;
        var first = queueAgeMsSeries.Take(3).Average();
        var last = queueAgeMsSeries.TakeLast(3).Average();
        return last - first >= risingThresholdMs && last > risingThresholdMs;
    }

    public static bool IsFrozen(double? secondsSinceLastFrame, double freezeThresholdSec = 2.0) =>
        secondsSinceLastFrame is not null && secondsSinceLastFrame >= freezeThresholdSec;
}
