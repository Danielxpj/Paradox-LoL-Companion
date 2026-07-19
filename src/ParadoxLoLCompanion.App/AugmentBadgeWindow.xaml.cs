using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ParadoxLoLCompanion.App;

/// <summary>
/// Superficie de badges: transparente, topmost, click-through (WS_EX_TRANSPARENT)
/// y sin foco (WS_EX_NOACTIVATE) — el juego recibe todos los clicks. Se posiciona
/// en píxeles físicos con SetWindowPos sobre el área cliente del juego.
/// </summary>
public partial class AugmentBadgeWindow : Window
{
    public AugmentBadgeWindow() => InitializeComponent();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle,
            style | WsExTransparent | WsExNoactivate | WsExToolwindow);
    }

    /// <summary>Muestra la ventana cubriendo exactamente el rect (screen px).</summary>
    public void ShowOver(Int32Rect px)
    {
        if (px.Width <= 0 || px.Height <= 0)
            return;
        if (!IsVisible)
        {
            Show();
            // Fade-in corto al aparecer; el Hide sigue siendo instantáneo (lo
            // molesto era el badge colgado, no que aparezca de golpe).
            BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
                0, 1, TimeSpan.FromMilliseconds(150)));
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HwndTopmost, px.X, px.Y, px.Width, px.Height,
            SwpNoactivate | SwpShowwindow);
    }

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolwindow = 0x80;
    private const int WsExNoactivate = 0x08000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
        int x, int y, int w, int h, uint flags);
}
