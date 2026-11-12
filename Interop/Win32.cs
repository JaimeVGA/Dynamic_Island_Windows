using System.Runtime.InteropServices;

namespace DynamicIsland.Interop;

internal static class Win32
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_NCHITTEST = 0x0084;
    public const int MA_NOACTIVATE = 3;
    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public static int GetExStyle(IntPtr hwnd) => (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);

    public static void AddExStyle(IntPtr hwnd, int flags)
        => SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(GetExStyle(hwnd) | flags));

    public static void ReassertTopmost(IntPtr hwnd)
        => SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

    // ---- Detección de ventana maximizada / pantalla completa / acoplada (snap) ----
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT r, int size);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint GA_ROOT = 2;
    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>
    /// true: la ventana en primer plano está maximizada, a pantalla completa o acoplada (snap/pantalla dividida) en el monitor principal.
    /// false: no lo está. null: ventana de shell (barra de tareas, etc.) o propia; conservar el estado anterior.
    /// </summary>
    public static bool? ForegroundIsMaximizedOnPrimary(uint ownPid)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == ownPid) return null;

        var cls = new System.Text.StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        string clsName = cls.ToString();

        // En el escritorio no hay ventanas maximizadas ni acopladas activas
        if (clsName == "Progman" || clsName == "WorkerW")
            return false;

        // Elementos del sistema donde conservamos el estado previo mientras el usuario interactúa con ellos
        switch (clsName)
        {
            case "Shell_TrayWnd": case "Shell_SecondaryTrayWnd":
            case "NotifyIconOverflowWindow": case "TopLevelWindowForOverflowXamlIsland":
            case "XamlExplorerHostIslandWindow": case "Windows.UI.Core.CoreWindow":
                return null;
        }

        // Obtener la ventana raíz por si el foco está en un control o diálogo hijo
        IntPtr root = GetAncestor(hwnd, GA_ROOT);
        if (root != IntPtr.Zero) hwnd = root;

        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        var primary = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        if (MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) != primary) return false;

        if (IsZoomed(hwnd)) return true;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(primary, ref mi)) return false;

        RECT r;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) != 0 || r.Right <= r.Left || r.Bottom <= r.Top)
        {
            if (!GetWindowRect(hwnd, out r)) return false;
        }

        // 1. Pantalla completa sin borde (juegos, vídeos a pantalla completa)
        if (r.Left <= mi.rcMonitor.Left + 5 && r.Top <= mi.rcMonitor.Top + 5 &&
            r.Right >= mi.rcMonitor.Right - 5 && r.Bottom >= mi.rcMonitor.Bottom - 5)
        {
            return true;
        }

        // 2. Ventana acoplada / Snap / Pantalla dividida (ej. 2 pestañas/ventanas lado a lado)
        // En Windows 11, las ventanas acopladas tocan el borde superior del área de trabajo
        // y tienen una altura y anchura sustanciales que invaden el área superior de la pantalla.
        int workTop = mi.rcWork.Top;
        int workBottom = mi.rcWork.Bottom;
        int workHeight = workBottom - workTop;
        int workWidth = mi.rcWork.Right - mi.rcWork.Left;

        int winWidth = r.Right - r.Left;
        int winHeight = r.Bottom - r.Top;

        bool touchesTop = r.Top <= workTop + 15 && Math.Abs(r.Top - workTop) <= 25;
        bool substantialHeight = winHeight >= workHeight * 0.40;
        bool substantialWidth = winWidth >= workWidth * 0.25;

        if (touchesTop && substantialHeight && substantialWidth)
        {
            return true;
        }

        return false;
    }
}