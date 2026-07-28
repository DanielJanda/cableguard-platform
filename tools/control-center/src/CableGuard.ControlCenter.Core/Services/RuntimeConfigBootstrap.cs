using System.IO;

namespace CableGuard.ControlCenter.Core.Services;

/// <summary>
/// Seeds gitignored runtime/config from tracked examples on first launch so GUI actions have data.
/// Never overwrites existing local files.
/// </summary>
public static class RuntimeConfigBootstrap
{
    public static void EnsureDefaults(ControlCenterConfig config, ControlCenterLogger logger)
    {
        Directory.CreateDirectory(RuntimeConfigPaths.Root(config));
        Directory.CreateDirectory(RuntimeConfigPaths.RoiDir(config));

        var examples = Path.Combine(config.PlatformRoot, "tools", "control-center", "config");
        CopyIfMissing(Path.Combine(examples, "cameras.example.json"), RuntimeConfigPaths.Cameras(config), logger);
        CopyIfMissing(Path.Combine(examples, "streams.example.json"), RuntimeConfigPaths.Streams(config), logger);
        CopyIfMissing(Path.Combine(examples, "detectors.example.json"), RuntimeConfigPaths.Detectors(config), logger);
        CopyIfMissing(Path.Combine(examples, "scenarios.example.json"), RuntimeConfigPaths.Scenarios(config), logger);
        CopyIfMissing(Path.Combine(examples, "test-stations.example.json"), RuntimeConfigPaths.TestStations(config), logger);
        CopyIfMissing(Path.Combine(examples, "notifications.example.json"), RuntimeConfigPaths.Notifications(config), logger);

        var roiExample = Path.Combine(examples, "roi", "office-fall-test.example.json");
        var roiDest = RuntimeConfigPaths.RoiFile(config, "office-fall-test");
        if (File.Exists(roiExample) && !File.Exists(roiDest))
        {
            // Example has .example.json naming — copy content without the "example" suffix name.
            File.Copy(roiExample, roiDest);
            logger.Info($"[BOOT] Seeded {roiDest}");
        }
    }

    private static void CopyIfMissing(string example, string dest, ControlCenterLogger logger)
    {
        if (!File.Exists(example) || File.Exists(dest)) return;
        File.Copy(example, dest);
        logger.Info($"[BOOT] Seeded {dest} from example");
    }
}
