using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CableGuard.ControlCenter.Core.Services;
using CableGuard.ControlCenter.ViewModels;

namespace CableGuard.ControlCenter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Some GPU/driver combinations composite WPF hardware surfaces as blank;
        // an admin tool favors reliability over GPU rendering.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // Composition root — no DI framework needed at this size.
        var config = ControlCenterConfig.LoadOrDefault();
        var logger = new ControlCenterLogger(config.LogsDir);
        logger.Info("Control Center starting.");

        DispatcherUnhandledException += (_, args) =>
        {
            logger.Error($"Unhandled UI exception: {args.Exception}");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.Error($"Unhandled exception: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.Error($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var prober = new HttpProber(http);
        var processes = new WindowsProcessInspector();
        var scripts = new PowerShellScriptRunner(logger);
        var mediaMtxApi = new MediaMtxApiClient(http, config.MediaMtxApiBase);
        var persister = new MediaMtxLocalConfigPersister(config.MediaMtxLocalYml);
        var credentials = new WindowsCredentialStore();

        var factory = new ComponentFactory(config, processes, prober, scripts, mediaMtxApi);
        var components = factory.CreateAllInStartOrder();

        var switchService = new StreamSwitchService(mediaMtxApi, persister, prober, config.WhepBaseLocal);
        var camerasVm = new CamerasViewModel(config, logger, mediaMtxApi, credentials, switchService);
        var logsVm = new LogsViewModel(config);
        var settingsVm = new SettingsViewModel(config);
        var mainVm = new MainViewModel(config, logger, components, camerasVm, logsVm, settingsVm);

        logger.Info("Composition complete, showing window.");
        var window = new MainWindow(mainVm);
        window.ContentRendered += (_, _) => logger.Info("Main window rendered.");
        window.Show();
    }
}
