using CableGuard.ControlCenter.Core.Services;

// Read-only USB-4761 diagnostics using the SAME adapter as Admin Studio.
// No writes, no relay output, no secrets.

var config = ControlCenterConfig.LoadOrDefault();
var logger = new ControlCenterLogger(config.LogsDir);
var adapter = (AdvantechUsb4761Adapter)AdvantechUsb4761Adapter.Create(config, logger);

adapter.RefreshDiscovery();
Console.Write(adapter.BuildDiagnosticsText());

return adapter.Discovery.Status == "CONNECTED" ? 0 : 1;
