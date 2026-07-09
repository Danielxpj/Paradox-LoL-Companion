using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ParadoxLoLCompanion.App.Interop;

/// <summary>
/// Hook global de teclado (WH_KEYBOARD_LL) que detecta Ctrl+X SOLO cuando el foco está
/// en el juego (League of Legends) o en esta app; en cualquier otra ventana la combinación
/// pasa intacta, así no se rompe el "cortar" del resto del sistema. Se usa un hook LL en
/// vez de RegisterHotKey porque RegisterHotKey secuestra la tecla en TODO Windows.
/// Instalar desde el hilo de UI: el callback llega por su message pump, por lo que
/// <see cref="Pressed"/> se dispara ya en el hilo de UI.
/// </summary>
public sealed class CtrlXHotkey : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int VkX = 0x58;
    private const int VkControl = 0x11;

    /// <summary>Procesos donde Ctrl+X abre el overlay (el juego, y la app para probar sin partida).</summary>
    private static readonly string[] TargetProcesses = { "League of Legends" };

    // Mantener la referencia al delegate: si la recoge el GC, el hook nativo queda colgado.
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    public event Action? Pressed;

    public CtrlXHotkey()
    {
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == WmKeydown || wParam == WmSyskeydown))
        {
            // Primer campo de KBDLLHOOKSTRUCT: vkCode. GetAsyncKeyState y no GetKeyState:
            // dentro de un hook LL el estado sincronizado a la cola del hilo va atrasado
            // y el Ctrl presionado se pierde de forma intermitente.
            var vk = Marshal.ReadInt32(lParam);
            if (vk == VkX && (GetAsyncKeyState(VkControl) & 0x8000) != 0 && ForegroundIsTarget())
            {
                Pressed?.Invoke();
                return 1; // consumida: que el juego no reciba también el Ctrl+X
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool ForegroundIsTarget()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            if (pid == Environment.ProcessId)
                return true;
            using var process = Process.GetProcessById((int)pid);
            return TargetProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Proceso ya muerto o sin permisos para consultarlo: no togglear.
            return false;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
