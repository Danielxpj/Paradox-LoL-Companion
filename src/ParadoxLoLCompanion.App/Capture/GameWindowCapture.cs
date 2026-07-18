using System.Runtime.InteropServices;

namespace ParadoxLoLCompanion.App.Capture;

/// <summary>Frame BGRA8 top-down del juego, listo para OCR.</summary>
public sealed record CapturedFrame(byte[] Bgra, int Width, int Height);

/// <summary>
/// Captura la ventana del juego con PrintWindow (GDI). Igual que el overlay,
/// requiere Borderless/Windowed: en Fullscreen exclusivo el resultado es negro —
/// eso no es un error, simplemente el OCR no matcheará nada.
/// </summary>
public static class GameWindowCapture
{
    /// <summary>PW_RENDERFULLCONTENT: incluye contenido DirectX, no solo GDI.</summary>
    private const uint PwRenderfullcontent = 2;

    public static CapturedFrame? Capture()
    {
        var hwnd = FindGameWindow();
        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var rect))
            return null;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width < 320 || height < 240)
            return null;   // minimizada o rota: nada que leer

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var bitmap = IntPtr.Zero;
        try
        {
            var info = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,   // negativo = top-down (fila 0 arriba)
                Planes = 1,
                BitCount = 32,
            };
            bitmap = CreateDIBSection(memDc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero)
                return null;
            var old = SelectObject(memDc, bitmap);
            var ok = PrintWindow(hwnd, memDc, PwRenderfullcontent);
            var buffer = new byte[width * height * 4];
            if (ok)
                Marshal.Copy(bits, buffer, 0, buffer.Length);

            // PrintWindow suele devolver negro con juegos DirectX aunque "funcione".
            // Fallback: BitBlt desde la pantalla sobre el rect del cliente — en
            // Borderless la ventana está visible, así que lo que hay en pantalla
            // ES el juego. (En Fullscreen exclusivo tampoco hay nada que hacer.)
            if (!ok || IsMostlyBlack(buffer))
            {
                var origin = new NativePoint();
                if (ClientToScreen(hwnd, ref origin)
                    && BitBlt(memDc, 0, 0, width, height, screenDc, origin.X, origin.Y, Srccopy))
                    Marshal.Copy(bits, buffer, 0, buffer.Length);
                else if (!ok)
                    return null;
            }
            SelectObject(memDc, old);
            return new CapturedFrame(buffer, width, height);
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Origen (screen px) del área cliente del juego: los píxeles del frame
    /// capturado son coordenadas de cliente, esto los vuelve absolutos para
    /// posicionar la ventana de badges. <c>null</c> si el juego no está.
    /// </summary>
    public static (int X, int Y)? GetClientOrigin()
    {
        var hwnd = FindGameWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        var origin = new NativePoint();
        return ClientToScreen(hwnd, ref origin) ? (origin.X, origin.Y) : null;
    }

    /// <summary>La ventana in-game de LoL (no el cliente/lobby, que es CEF).</summary>
    private static IntPtr FindGameWindow()
    {
        var byClass = FindWindow("RiotWindowClass", null);
        return byClass != IntPtr.Zero
            ? byClass
            : FindWindow(null, "League of Legends (TM) Client");
    }

    /// <summary>Muestrea 1 de cada ~1000 píxeles: si casi todos son ~negros, la
    /// captura falló de facto (PrintWindow "exitoso" pero vacío).</summary>
    internal static bool IsMostlyBlack(byte[] bgra)
    {
        int samples = 0, dark = 0;
        for (var i = 0; i < bgra.Length; i += 4000)
        {
            samples++;
            if (bgra[i] < 16 && bgra[i + 1] < 16 && bgra[i + 2] < 16)
                dark++;
        }
        return samples > 0 && dark >= samples * 0.98;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    /// <summary>BITMAPINFOHEADER pelado (sin tabla de colores: 32 bpp no la usa).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    private const uint Srccopy = 0x00CC0020;

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destDc, int x, int y, int w, int h,
        IntPtr srcDc, int srcX, int srcY, uint rop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info,
        uint usage, out IntPtr bits, IntPtr section, uint offset);
}
