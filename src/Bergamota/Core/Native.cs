using System.Runtime.InteropServices;
using System.Text;

namespace Bergamota.Core;

/// <summary>Chamadas diretas ao Windows (user32, shell32, dwmapi...).</summary>
public static class Native
{
    // ---------- hooks globais de teclado e mouse ----------
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    public const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    public const int WM_HOTKEY = 0x0312;

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    // ---------- janelas ----------
    public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    public const long WS_SYSMENU = 0x80000;
    public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public static (RECT bounds, RECT work, double scale) MonitorAt(POINT p)
    {
        var mon = MonitorFromPoint(p, 2 /*NEAREST*/);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref mi);
        double scale = 1;
        if (GetDpiForMonitor(mon, 0, out var dx, out _) == 0) scale = dx / 96.0;
        return (mi.rcMonitor, mi.rcWork, scale);
    }

    // ---------- DWM (vidro/acrÃ­lico da janela) ----------
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // ---------- shell ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENASINFO { public string pcszFile; public string? pcszClass; public int oaifInFlags; }
    public const int OAIF_ALLOW_REGISTRATION = 1, OAIF_REGISTER_EXT = 2, OAIF_EXEC = 4;
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHOpenWithDialog(IntPtr hwnd, ref OPENASINFO info);

    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
}
