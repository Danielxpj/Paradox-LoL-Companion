using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

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

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;

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
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
    }

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
