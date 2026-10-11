using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ParadoxLoLCompanion.App.ViewModels;

namespace ParadoxLoLCompanion.App;

/// <summary>
/// Overlay in-game (Ctrl+X): lista mínima de recomendaciones de items, siempre visible
/// sobre el juego. No roba el foco (WS_EX_NOACTIVATE), no sale en Alt+Tab
/// (WS_EX_TOOLWINDOW) y se puede arrastrar a gusto; la posición se conserva entre toggles
/// porque la instancia se reutiliza. Requiere el juego en Borderless/Windowed: en
/// Fullscreen exclusivo Windows no dibuja ventanas encima.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;

    private bool _userMoved;
    private bool _openedForDeath;
    private readonly DispatcherTimer _autoClose;

    public OverlayWindow()
    {
        InitializeComponent();
        _autoClose = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        SourceInitialized += OnSourceInitialized;
        _autoClose.Tick += (_, _) =>
        {
            CancelAutoClose();
            if (IsVisible)
                Hide();
        };
        // Input must not silently cancel a checked AUTO-CLOSE option.
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                CancelAutoClose();
                _openedForDeath = false;
            }
        };
        Closed += (_, _) =>
        {
            CancelAutoClose();
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged -= OnViewModelPropertyChanged;
        };

        // Posición: pegado al borde derecho y arriba, por encima del minimapa, donde no
        // tapa ni el HUD de habilidades ni la tienda. Con SizeToContent el ancho recién
        // se conoce tras el layout (y cambia cuando aparece el panel de botas), así que
        // se re-ancla el borde DERECHO en cada SizeChanged hasta que el usuario lo mueva.
        Top = SystemParameters.WorkArea.Top + 140;
        SizeChanged += (_, _) =>
        {
            if (!_userMoved)
                Left = SystemParameters.WorkArea.Right - ActualWidth - 24;
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, styles | WsExNoactivate | WsExToolwindow);
    }

    /// <summary>Muestra u oculta el overlay, reafirmando TOPMOST al mostrar
    /// (el juego puede haber pasado por encima mientras estaba oculto).</summary>
    public void Toggle()
    {
        // Un toggle manual (Ctrl+X) manda: cancela cualquier autocierre pendiente.
        CancelAutoClose();
        _openedForDeath = false;
        if (IsVisible)
        {
            Hide();
            return;
        }

        ShowTopmost();
    }

    /// <summary>
    /// Apertura automática por muerte: muestra el overlay y, si el ticket AUTO-CLOSE está
    /// tildado, programa el cierre. Una apertura manual se conserva hasta Ctrl+X.
    /// </summary>
    public void ShowForDeath()
    {
        if (IsVisible && !_openedForDeath)
            return;

        _openedForDeath = true;
        ShowTopmost();
        RestartAutoClose();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is MainViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        RestartAutoClose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.OverlayAutoCloseOnDeath)
            or nameof(MainViewModel.AutoCloseSeconds))
            RestartAutoClose();
    }

    private void RestartAutoClose()
    {
        CancelAutoClose();
        if (_openedForDeath && IsVisible
            && DataContext is MainViewModel { OverlayAutoCloseOnDeath: true } vm)
        {
            _autoClose.Interval = TimeSpan.FromSeconds(vm.AutoCloseSeconds);
            _autoClose.Start();
        }
    }

    private void ShowTopmost()
    {
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
    }

    private void CancelAutoClose() => _autoClose.Stop();

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        _userMoved = true;
        DragMove();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);
}
