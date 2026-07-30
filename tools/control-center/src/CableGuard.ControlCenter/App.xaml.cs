using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CableGuard.ControlCenter.Core.Models;
using CableGuard.ControlCenter.Core.Services;
using CableGuard.ControlCenter.ViewModels;

namespace CableGuard.ControlCenter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var config = ControlCenterConfig.LoadOrDefault();
        BuildInfo.Initialize(config);
        var logger = new ControlCenterLogger(config.LogsDir);
        logger.Info($"Control Center (Admin Studio) starting. {BuildInfo.Summary}");
        logger.Info($"PlatformRoot={config.PlatformRoot}");

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
        var cameraApply = new CameraRuntimeApplyService(config, mediaMtxApi, persister, credentials, prober, logger);
        var hardware = AdvantechUsb4761Adapter.Create(config, logger);

        var switchService = new StreamSwitchService(mediaMtxApi, persister, prober, config.WhepBaseLocal);
        var detectorManager = new DetectorProcessManager(config, logger, processes);
        RuntimeConfigBootstrap.EnsureDefaults(config, logger);

        var factory = new ComponentFactory(config, processes, prober, scripts, mediaMtxApi, detectorManager);
        var components = factory.CreateAllInStartOrder();

        var mode = new AdminModeViewModel();
        var session = new SelectedCameraSession();
        OfficeCameraBootstrap.EnsureOffice63(config, logger);

        var notificationsVm = new NotificationsViewModel(config, logger, credentials);
        var detectorsVm = new DetectorsViewModel(config, logger, detectorManager,
            () => StreamsService.Load(config.StreamsJsonPath), () => notificationsVm.Document);
        var camerasVm = new CamerasViewModel(config, logger, mediaMtxApi, credentials, switchService, cameraApply,
            session, detectorsVm, notificationsVm);
        var streamsVm = new StreamsViewModel(config, logger, mediaMtxApi, switchService, () => camerasVm.Registry);
        var mediaMtx = components.First(c => c.Id == ComponentId.MediaMtx);
        var calibrationVm = new CalibrationViewModel(
            config, logger,
            () => StreamsService.Load(config.StreamsJsonPath),
            () => camerasVm.Registry,
            mediaMtx);
        var hardwareVm = new HardwareViewModel(logger, hardware);
        var scenariosVm = new ScenariosViewModel(config, logger, detectorsVm, notificationsVm, hardwareVm);
        var videoLabCollector = new VideoLabCollector(config, mediaMtxApi, http);
        var videoLabVm = new VideoLabViewModel(config, logger, videoLabCollector,
            () => StreamsService.Load(config.StreamsJsonPath), () => camerasVm.Registry, components);
        var logsVm = new LogsViewModel(config);
        var settingsVm = new SettingsViewModel(config);
        var detectionOps = new DetectionOpsViewModel(config, logger, session, detectorsVm, notificationsVm, mediaMtxApi);
        var recordingOps = new RecordingOpsViewModel(config, session, mediaMtxApi);
        var eventsTests = new EventsTestsViewModel(config, logger, session, http);

        // Default operator focus: office test camera (Detection tab works without Cameras SELECT).
        var officeCam = camerasVm.Registry.Cameras.FirstOrDefault(c =>
            string.Equals(c.CameraId, OfficeCameraBootstrap.OfficeCameraId, StringComparison.OrdinalIgnoreCase));
        if (officeCam is not null)
            session.Select(officeCam);
        detectionOps.EnsureDefaultOfficeCamera();

        var mainVm = new MainViewModel(config, logger, components, mode, camerasVm, streamsVm, detectorsVm,
            calibrationVm, notificationsVm, hardwareVm, scenariosVm, videoLabVm, logsVm, settingsVm,
            session, detectionOps, recordingOps, eventsTests, mediaMtxApi);

        logger.Info("Composition complete, showing window.");
        var window = new MainWindow(mainVm);
        window.ContentRendered += (_, _) => logger.Info("Main window rendered.");
        window.Show();
    }
}
