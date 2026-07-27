namespace CableGuard.ControlCenter.Core.Services;

/// <summary>Gitignored runtime config paths under platform/runtime/config/.</summary>
public static class RuntimeConfigPaths
{
    public static string Root(ControlCenterConfig c) => Path.Combine(c.PlatformRoot, "runtime", "config");
    public static string Cameras(ControlCenterConfig c) => Path.Combine(Root(c), "cameras.json");
    public static string Streams(ControlCenterConfig c) => Path.Combine(Root(c), "streams.json");
    public static string Detectors(ControlCenterConfig c) => Path.Combine(Root(c), "detectors.json");
    public static string Notifications(ControlCenterConfig c) => Path.Combine(Root(c), "notifications.json");
    public static string Hardware(ControlCenterConfig c) => Path.Combine(Root(c), "hardware.json");
    public static string Scenarios(ControlCenterConfig c) => Path.Combine(Root(c), "scenarios.json");
    public static string RoiDir(ControlCenterConfig c) => Path.Combine(Root(c), "roi");
    public static string RoiFile(ControlCenterConfig c, string id) => Path.Combine(RoiDir(c), $"{id}.json");
}
