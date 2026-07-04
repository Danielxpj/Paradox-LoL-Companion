using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LoLAdvisor.App.Diagnostics;
using LoLAdvisor.App.ViewModels;
using LoLAdvisor.Core.Config;

namespace LoLAdvisor.App;

/// <summary>Punto de entrada: crea el ViewModel principal y la ventana, y arranca las fuentes.</summary>
public partial class App : Application
{
    private MainViewModel? _viewModel;
    private bool _errorDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        FileLog.Init();
        WireGlobalExceptionHandlers();

        var samples = new ReplaySamples(
            RiftGame: LoadAsset("sample-allgamedata.json"),
            AramGame: LoadAsset("sample-allgamedata-aram.json"),
            ChampSelect: LoadAsset("sample-champselect.json"));
        var config = LoadConfig();
        _viewModel = new MainViewModel(Dispatcher, config, samples);

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();

        _viewModel.Start();
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
            "LoLAdvisor", "advisor-config.json");
        if (File.Exists(userPath))
            return AdvisorConfig.Load(userPath);

        var shipped = Path.Combine(AppContext.BaseDirectory, "Assets", "advisor-config.json");
        return AdvisorConfig.Load(shipped);
    }
}
