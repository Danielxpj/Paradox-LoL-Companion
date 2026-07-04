using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LoLAdvisor.App.ViewModels;

namespace LoLAdvisor.App;

/// <summary>Ventana principal. Solo maneja el autoscroll de la consola; el resto es MVVM.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SourceInitialized += OnSourceInitialized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Barra de título nativa en oscuro (DWMWA_USE_IMMERSIVE_DARK_MODE) para que
    /// no desentone con el tema; si el SO no lo soporta, se ignora sin romper nada.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch
        {
            // Windows sin soporte del atributo: la barra queda clara, nada más.
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            vm.ConsoleLines.CollectionChanged += OnConsoleLinesChanged;
            vm.DataUpdateAvailable += info => OnUpdateAvailable(vm, info);
        }
    }

    private void OnUpdateAvailable(MainViewModel vm, DataUpdateInfo info)
    {
        var result = MessageBox.Show(
            $"A new version of the game data (Data Dragon) is available:\n\n" +
            $"    Current:  {info.CachedVersion}\n    New:      {info.LatestVersion}\n\nUpdate now?",
            "Game data update available",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            _ = vm.DownloadAndApplyAsync(info.LatestVersion);
    }

    private GridLength _savedConsoleHeight = new(200);
    private bool _consoleCollapsed;

    /// <summary>
    /// Muestra/oculta el log de la consola. Al ocultarlo queda solo la barra de título
    /// (la fila pasa a Auto) y se recuerda la altura elegida con el splitter para
    /// restaurarla al volver a mostrarlo.
    /// </summary>
    private void OnToggleConsole(object sender, RoutedEventArgs e)
    {
        if (_consoleCollapsed)
        {
            ConsoleRow.MinHeight = 120;
            ConsoleRow.Height = _savedConsoleHeight;
            ConsoleList.Visibility = Visibility.Visible;
            ConsoleSplitter.Visibility = Visibility.Visible;
            ToggleConsoleButton.Content = "Hide";
        }
        else
        {
            _savedConsoleHeight = ConsoleRow.Height;
            ConsoleRow.MinHeight = 0;
            ConsoleRow.Height = GridLength.Auto;
            ConsoleList.Visibility = Visibility.Collapsed;
            ConsoleSplitter.Visibility = Visibility.Collapsed;
            ToggleConsoleButton.Content = "Show";
        }
        _consoleCollapsed = !_consoleCollapsed;
    }

    private void OnConsoleLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // Deferir el scroll FUERA del evento CollectionChanged: hacerlo en línea
        // corrompe el generador del ListBox (excepción "inconsistent with its items source").
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (ConsoleList.Items.Count > 0)
                    ConsoleList.ScrollIntoView(ConsoleList.Items[ConsoleList.Items.Count - 1]);
            }
            catch
            {
                // estado transitorio del generador: ignorar, el próximo tick reintenta
            }
        }, DispatcherPriority.Background);
    }
}
