using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Bergamota.Models;

namespace Bergamota.Core;

public enum TriggerSource { Key, Edge, Hotkey, Preview }

public record WheelRequest(Native.POINT Point, TriggerSource Source, string? Zone = null, IReadOnlyList<string>? Files = null);

/// <summary>
/// Decide quando a roda aparece. Usa hooks de teclado/mouse do Windows (só reagem a eventos,
/// não ficam consultando nada em loop). O temporizador da borda só roda enquanto o botão
/// esquerdo está pressionado num arraste que começou no Explorer.
/// </summary>
public sealed class TriggerService : IDisposable
{
    readonly TriggerSettings _cfg;
    readonly Native.HookProc _kbProc, _msProc; // referências guardadas para o GC não coletar
    IntPtr _kbHook, _msHook;
    readonly DispatcherTimer _edgeTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
    HwndSource? _msgWindow;
    const int HotkeyId = 0xB36A;

    volatile bool _leftDown;
    Native.POINT _downPt;
    bool _downInExplorer, _swallowKeyUp, _swallowMouseUp;
    string? _zone;
    DateTime _zoneSince;

    /// <summary>Pede para abrir a roda.</summary>
    public event Action<WheelRequest>? ShowWheel;
    /// <summary>O botão esquerdo foi solto (fim do arraste).</summary>
    public event Action? DragEnded;
    /// <summary>A roda está aberta? (não dispara de novo enquanto estiver)</summary>
    public Func<bool> IsWheelOpen { get; set; } = () => false;

    public bool HotkeyRegistered { get; private set; }

    public TriggerService(TriggerSettings cfg)
    {
        _cfg = cfg;
        _kbProc = KeyboardHook;
        _msProc = MouseHook;
        _edgeTimer.Tick += EdgeTick;
        cfg.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TriggerSettings.HotkeyEnabled)) UpdateHotkey(); };
    }

    public void Start()
    {
        var mod = Native.GetModuleHandle(null);
        _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, mod, 0);
        _msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _msProc, mod, 0);
        if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
            ActivityLog.Error("Gatilhos", "Não consegui instalar os atalhos globais (erro " + Marshal.GetLastWin32Error() + ").");

        _msgWindow = new HwndSource(new HwndSourceParameters("BergamotaMessages") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _msgWindow.AddHook(WndProc);
        UpdateHotkey();
    }

    public void Dispose()
    {
        if (_kbHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_kbHook);
        if (_msHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_msHook);
        if (_msgWindow != null) { Native.UnregisterHotKey(_msgWindow.Handle, HotkeyId); _msgWindow.Dispose(); }
    }

    // ---------------- atalho global Win+Shift+B ----------------

    void UpdateHotkey()
    {
        if (_msgWindow == null) return;
        Native.UnregisterHotKey(_msgWindow.Handle, HotkeyId);
        HotkeyRegistered = false;
        if (_cfg.HotkeyEnabled)
        {
            HotkeyRegistered = Native.RegisterHotKey(_msgWindow.Handle, HotkeyId, Native.MOD_WIN | Native.MOD_SHIFT | Native.MOD_NOREPEAT, 0x42 /*B*/);
            if (!HotkeyRegistered) ActivityLog.Error("Gatilhos", "Win+Shift+B já está em uso por outro programa.");
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            var files = ExplorerSelection.Get();
            if (files.Count == 0) { ActivityLog.Error("Atalho", "Selecione arquivos numa janela do Explorer antes de usar Win+Shift+B.", notify: true); return IntPtr.Zero; }
            Native.GetCursorPos(out var pt);
            ShowWheel?.Invoke(new WheelRequest(pt, TriggerSource.Hotkey, Files: files));
        }
        return IntPtr.Zero;
    }

    // ---------------- hooks ----------------

    bool DragMoved(Native.POINT p) => Math.Abs(p.X - _downPt.X) > 6 || Math.Abs(p.Y - _downPt.Y) > 6;

    int TriggerVk()
    {
        if (Enum.TryParse<Key>(_cfg.Key, out var k)) return KeyInterop.VirtualKeyFromKey(k);
        return -1;
    }

    IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _cfg.KeyEnabled)
        {
            var kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            if ((int)kb.vkCode == TriggerVk())
            {
                bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                if (down && _leftDown)
                {
                    Native.GetCursorPos(out var p);
                    if (DragMoved(p))
                    {
                        if (!IsWheelOpen()) Fire(new WheelRequest(p, TriggerSource.Key));
                        _swallowKeyUp = true;
                        return (IntPtr)1; // engole a tecla (o CapsLock não liga)
                    }
                }
                else if (!down && _swallowKeyUp)
                {
                    _swallowKeyUp = false;
                    return (IntPtr)1;
                }
            }
        }
        return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var ms = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            switch (msg)
            {
                case Native.WM_LBUTTONDOWN:
                    _leftDown = true;
                    _downPt = ms.pt;
                    // descobrir se o clique foi no Explorer é feito fora do hook (que precisa ser rápido)
                    if (_cfg.EdgeEnabled && _cfg.Edges.Count > 0)
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => BeginEdgeWatch(ms.pt));
                    break;
                case Native.WM_LBUTTONUP:
                    _leftDown = false;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() => { _edgeTimer.Stop(); DragEnded?.Invoke(); });
                    break;
                case Native.WM_MBUTTONDOWN or Native.WM_XBUTTONDOWN:
                    if (_cfg.KeyEnabled && _leftDown && MouseTriggerMatches(msg, ms.mouseData) && DragMoved(ms.pt))
                    {
                        if (!IsWheelOpen()) Fire(new WheelRequest(ms.pt, TriggerSource.Key));
                        _swallowMouseUp = true;
                        return (IntPtr)1;
                    }
                    break;
                case Native.WM_MBUTTONUP or Native.WM_XBUTTONUP:
                    if (_swallowMouseUp) { _swallowMouseUp = false; return (IntPtr)1; }
                    break;
            }
        }
        return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
    }

    bool MouseTriggerMatches(int msg, uint mouseData)
    {
        if (msg == Native.WM_MBUTTONDOWN) return _cfg.Key == "MButton";
        int x = (int)(mouseData >> 16);
        return (x == 1 && _cfg.Key == "XButton1") || (x == 2 && _cfg.Key == "XButton2");
    }

    void Fire(WheelRequest r) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ShowWheel?.Invoke(r));

    // ---------------- borda da tela ----------------

    void BeginEdgeWatch(Native.POINT pt)
    {
        _downInExplorer = IsExplorerAt(pt);
        _zone = null;
        if (_downInExplorer && _leftDown) _edgeTimer.Start();
    }

    void EdgeTick(object? sender, EventArgs e)
    {
        if (!_leftDown || !_cfg.EdgeEnabled || IsWheelOpen()) { _edgeTimer.Stop(); return; }
        Native.GetCursorPos(out var p);
        if (!DragMoved(p)) return;
        var zone = ZoneAt(p);
        if (zone == null || !_cfg.Edges.Contains(zone)) { _zone = null; return; }
        if (zone != _zone) { _zone = zone; _zoneSince = DateTime.Now; return; }
        if ((DateTime.Now - _zoneSince).TotalMilliseconds >= _cfg.DwellMs)
        {
            _edgeTimer.Stop();
            ShowWheel?.Invoke(new WheelRequest(p, TriggerSource.Edge, zone));
        }
    }

    static string? ZoneAt(Native.POINT p)
    {
        var (b, _, scale) = Native.MonitorAt(p);
        int t = (int)Math.Ceiling(3 * scale), c = (int)(48 * scale);
        bool l = p.X <= b.Left + t, r = p.X >= b.Right - 1 - t, top = p.Y <= b.Top + t, bot = p.Y >= b.Bottom - 1 - t;
        bool nl = p.X < b.Left + c, nr = p.X > b.Right - c, nt = p.Y < b.Top + c, nb = p.Y > b.Bottom - c;
        if ((l || top) && nl && nt) return "TL";
        if ((r || top) && nr && nt) return "TR";
        if ((l || bot) && nl && nb) return "BL";
        if ((r || bot) && nr && nb) return "BR";
        if (l) return "Left";
        if (r) return "Right";
        if (top) return "Top";
        if (bot) return "Bottom";
        return null;
    }

    static bool IsExplorerAt(Native.POINT p)
    {
        try
        {
            var root = Native.GetAncestor(Native.WindowFromPoint(p), 2 /*GA_ROOT*/);
            Native.GetWindowThreadProcessId(root, out var pid);
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>Itens selecionados na janela do Explorer que está em primeiro plano.</summary>
public static class ExplorerSelection
{
    public static List<string> Get()
    {
        var result = new List<string>();
        try
        {
            var fg = Native.GetForegroundWindow();
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return result;
            dynamic shell = Activator.CreateInstance(type)!;
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    if (new IntPtr((long)w.HWND) != fg) continue;
                    foreach (dynamic item in w.Document.SelectedItems()) result.Add((string)item.Path);
                    if (result.Count > 0) break;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Error("Atalho", "Não consegui ler a seleção do Explorer: " + ex.Message, notify: false);
        }
        return result;
    }
}
