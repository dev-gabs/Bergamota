using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Bergamota.Core;
using Bergamota.Models;
using Bergamota.UI.Views;

namespace Bergamota.UI;

public partial class MainWindow : Window
{
    readonly Dictionary<string, UserControl> _views = new();

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyBackdrop();
        NavDash.IsChecked = true;

        App.Devices.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Net.DeviceService.OnlineSummary) or nameof(Net.DeviceService.IsScanning)) UpdateStatus();
        };
        App.Settings.Triggers.PropertyChanged += (_, _) => UpdateStatus();
        App.Settings.Triggers.Edges.CollectionChanged += (_, _) => UpdateStatus();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        NetSummary.Text = App.Devices.IsScanning ? "procurando aparelhos…" : App.Devices.OnlineSummary;
        var t = App.Settings.Triggers;
        var parts = new List<string>();
        if (t.KeyEnabled) parts.Add(App.KeyName(t.Key) is var k && k.StartsWith("o ") ? k[2..] : App.KeyName(t.Key));
        if (t.EdgeEnabled && t.Edges.Count > 0) parts.Add($"borda · {t.DwellMs} ms");
        if (t.HotkeyEnabled) parts.Add("Win+Shift+B");
        TrigSummary.Text = parts.Count == 0 ? "nenhum gatilho ligado" : string.Join("  ·  ", parts);
    }

    public void Navigate(string key, Profile? profile = null)
    {
        var map = new Dictionary<string, RadioButton> { ["dash"] = NavDash, ["edit"] = NavEdit, ["trig"] = NavTrig, ["devs"] = NavDevs, ["log"] = NavLog, ["about"] = NavAbout };
        if (profile != null && GetView("edit") is EditorView ed) ed.Select(profile);
        if (map.TryGetValue(key, out var rb)) { if (rb.IsChecked == true) Show(key); else rb.IsChecked = true; }
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key }) Show(key);
    }

    void Show(string key)
    {
        var v = GetView(key);
        Host.Content = v;
        if (v is DashboardView d) d.Refresh();
    }

    UserControl GetView(string key)
    {
        if (_views.TryGetValue(key, out var v)) return v;
        v = key switch
        {
            "edit" => new EditorView(),
            "trig" => new TriggersView(),
            "devs" => new DevicesView(),
            "log" => new LogView(),
            "about" => new AboutView(),
            _ => new DashboardView(),
        };
        return _views[key] = v;
    }

    void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var p = new Profile { Name = "Novo perfil", Color = "#5FB08A" };
        foreach (var id in new[] { "zip", "openwith", "properties", "copypath" }) p.Slices.Add(Slice.Create(SliceKind.Native, id));
        p.Slices.Add(Slice.Create(SliceKind.Send, ""));
        App.Settings.Profiles.Add(p);
        App.Settings.Save();
        Navigate("edit", p);
    }

    void TestWheel_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).PreviewWheel(GetView("dash") is DashboardView d ? d.CurrentProfile : null);

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        App.Settings.Save();
        base.OnClosing(e); // fecha só o hub; a Bergamota continua na bandeja
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // maximizada, a janela sem moldura passa um pouco da tela; compensa com margem
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    /// <summary>Windows 11: fundo acrílico de verdade atrás da janela (o desktop aparece desfocado).</summary>
    void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1, round = 2;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        if (Environment.OSVersion.Version.Build < 22621) return;

        int acrylic = 3; // DWMSBT_TRANSIENTWINDOW
        if (Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, sizeof(int)) != 0) return;
        var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
        Background = Brushes.Transparent;
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } ct }) ct.BackgroundColor = Colors.Transparent;
        BaseFill.Opacity = 0.94; // só um toque do acrílico, para manter o tom escuro do mockup

        // com o quadro estendido o Windows desenha os botões nativos atrás dos nossos ("fantasma");
        // sem WS_SYSMENU ele não desenha. Minimizar/maximizar continuam pelos nossos botões.
        var style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64() & ~Native.WS_SYSMENU;
        Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style));
    }
}
