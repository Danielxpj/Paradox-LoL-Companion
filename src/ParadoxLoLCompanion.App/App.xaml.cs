using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ParadoxLoLCompanion.App.Diagnostics;
using ParadoxLoLCompanion.App.Interop;
using ParadoxLoLCompanion.App.Update;
using ParadoxLoLCompanion.App.ViewModels;
using ParadoxLoLCompanion.Core.Config;

namespace ParadoxLoLCompanion.App;

/// <summary>Punto de entrada: crea el ViewModel principal y la ventana, y arranca las fuentes.</summary>
public partial class App : Application
{
    private MainViewModel? _viewModel;
    private SingleInstance? _instance;
    private bool _errorDialogShown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Mientras corre el chequeo de update, el splash puede abrirse y cerrarse sin que
        // exista aún la ventana principal: sin esto, cerrarlo dispararía OnLastWindowClose
        // y la app se apagaría. Volvemos al modo normal recién con la ventana principal ya arriba.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Una sola instancia. Dos a la vez duplican todo lo visible (dos overlays
        // superpuestos al morir, dos bucles de OCR comiéndose la CPU, dos escrituras de
        // páginas al cliente) y se siente como si la app estuviera rota.
        // Va ANTES de FileLog.Init() a propósito: ese Init trunca session.log, y un segundo
        // arranque no tiene por qué borrarle el log a la instancia que está trabajando.
        if (!AcquireSingleInstance(e.Args))
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        FileLog.Init();
        WireGlobalExceptionHandlers();

        // Auto-actualización: primero limpiamos restos de un update previo y luego
        // revisamos si hay una versión más nueva. Si la hay, se descarga, se aplica y la
        // app se relanza (salimos acá sin abrir la ventana principal). Todo fail-open.
        Updater.CleanupOldVersions();
        if (await Updater.TryUpdateAsync(Dispatcher))
            return;

        var samples = new ReplaySamples(
            RiftGame: LoadAsset("sample-allgamedata.json"),
            AramGame: LoadAsset("sample-allgamedata-aram.json"),
            ChampSelect: LoadAsset("sample-champselect.json"));
        var config = LoadConfig();
        // Las opciones de la UI (overlay al morir, autocierre) viven en su propio archivo:
        // el advisor-config.json empaquetado se pisa en cada actualización.
        var prefsPath = UserPreferences.DefaultPath;
        var prefs = UserPreferences.Load(prefsPath, config.Items.OverlayOnDeath);
        _viewModel = new MainViewModel(Dispatcher, config, samples, prefs, prefsPath);

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // Si abrís la app de nuevo (doble clic en el acceso directo), en vez de una
        // segunda copia se trae ESTA ventana al frente.
        _instance?.ListenForActivation(() => Dispatcher.Invoke(() => BringToFront(window)));

        _viewModel.Start();
    }

    /// <summary>
    /// Titularidad de la instancia única. Si venimos de un auto-update
    /// (<c>--updated &lt;pid&gt;</c>), primero se espera a que la instancia vieja termine
    /// de apagarse: hasta entonces el mutex sigue siendo suyo.
    /// </summary>
    private bool AcquireSingleInstance(string[] args)
    {
        var wait = TimeSpan.Zero;
        if (Updater.RelaunchedFromPid(args) is { } previousPid)
        {
            SingleInstance.WaitForProcessExit(previousPid, TimeSpan.FromSeconds(15));
            wait = TimeSpan.FromSeconds(10);
        }

        _instance = SingleInstance.Acquire(wait);
        return _instance is not null;
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    private void WireGlobalExceptionHandlers()
    {
        // Excepción en el hilo de UI: registrar, avisar una vez y MANTENER viva la app.
        DispatcherUnhandledException += (_, args) =>
        {
            FileLog.WriteException("DispatcherUnhandledException", args.Exception);
            _viewModel?.ReportError(args.Exception);
            ShowErrorOnce(args.Exception);
            args.Handled = true;
        };

        // Excepciones fuera del hilo de UI: al menos dejarlas registradas.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                FileLog.WriteException("AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            FileLog.WriteException("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private void ShowErrorOnce(Exception ex)
    {
        if (_errorDialogShown)
            return;
        _errorDialogShown = true;
        MessageBox.Show(
            $"An error occurred, but the app keeps running.\n\n{ex.Message}\n\nDetails at:\n{FileLog.LogFilePath}",
            "Paradox LoL Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_viewModel is not null)
            await _viewModel.DisposeAsync();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static string LoadAsset(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
        return File.Exists(path) ? File.ReadAllText(path) : "{}";
    }

    /// <summary>
    /// Carga la config de reglas. Prioridad: override del usuario en LocalAppData,
    /// luego el archivo que viene con la app, y si no hay ninguno, los valores por defecto.
    /// </summary>
    private static AdvisorConfig LoadConfig()
    {
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParadoxLoLCompanion", "advisor-config.json");
        if (File.Exists(userPath))
            return AdvisorConfig.Load(userPath);

        var shipped = Path.Combine(AppContext.BaseDirectory, "Assets", "advisor-config.json");
        return AdvisorConfig.Load(shipped);
    }
}
