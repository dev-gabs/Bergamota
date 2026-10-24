using System.IO;
using System.Windows;
using Bergamota.Core;
using Bergamota.Models;
using Bergamota.Net;
using Bergamota.UI;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Bergamota;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static DeviceService Devices { get; private set; } = null!;
    public static TriggerService Triggers { get; private set; } = null!;

    Mutex? _single;
    EventWaitHandle? _showSignal;
    Forms.NotifyIcon? _tray;
    MainWindow? _hub;
    WheelWindow? _wheel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // uma instância só: a segunda apenas pede para a primeira abrir o hub
        _single = new Mutex(true, "Bergamota.SingleInstance", out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Bergamota.ShowHub");
        if (!first) { _showSignal.Set(); Shutdown(); return; }
        new Thread(() => { while (_showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowHub); }) { IsBackground = true }.Start();

        DispatcherUnhandledException += (_, ex) => { ActivityLog.Error("Erro inesperado", ex.Exception.Message); ex.Handled = true; };

        Settings = AppSettings.Load();
        Devices = new DeviceService(Settings);
        Triggers = new TriggerService(Settings.Triggers);
        Triggers.IsWheelOpen = () => _wheel is { IsClosed: false };
        Triggers.ShowWheel += OpenWheel;
        Triggers.DragEnded += () => _wheel?.OnDragEnded();
        ActionRunner.ChooseDeviceAndSend = async files =>
        {
            // fallback (ex.: fatia testada pelo ▶ do editor): manda para o único online, ou abre a roda de escolha
            var online = Devices.Devices.Where(d => d.IsOnline && d.ShowInWheel).ToList();
            if (online.Count == 1 && Settings.FavoriteFolders.Count == 0) await Devices.SendAsync(online[0], files);
            else
            {
                Native.GetCursorPos(out var pt);
                var temp = new Profile { Name = "Enviar para" };
                temp.Slices.Add(Slice.Create(SliceKind.Send, ""));
                OpenWheel(new WheelRequest(pt, TriggerSource.Preview, Files: files), temp, startChooser: true);
            }
        };
        ActivityLog.Notify += (title, text, isError) =>
            _tray?.ShowBalloonTip(3000, title, text, isError ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

        SetupTray();
        Triggers.Start();
        Devices.Start();
        ApplyStartWithWindows();
        Settings.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName == nameof(AppSettings.StartWithWindows)) ApplyStartWithWindows();
            Settings.Save();
        };

        ActivityLog.Info("wheel", "Bergamota iniciada", "Segure " + KeyName(Settings.Triggers.Key) + " enquanto arrasta arquivos");
        if (!e.Args.Contains("--tray")) ShowHub();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Triggers?.Dispose();
        Devices?.Stop();
        Settings?.Save();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }

    public static string KeyName(string key) => key switch
    {
        "Capital" or "CapsLock" => "CapsLock",
        "XButton1" => "o botão lateral do mouse",
        "XButton2" => "o botão lateral 2",
        "MButton" => "o botão do meio",
        "Space" => "Espaço",
        _ => key,
    };

    /// <summary>Perfil para os arquivos arrastados: o primeiro cujas extensões batem; senão o primeiro da lista.</summary>
    public static Profile PickProfile(IReadOnlyList<string> files) =>
        Settings.Profiles.FirstOrDefault(p => p.Matches(files)) ?? Settings.Profiles.First();

    public void OpenWheel(WheelRequest req) => OpenWheel(req, null, false);

    void OpenWheel(WheelRequest req, Profile? profile, bool startChooser)
    {
        if (_wheel is { IsClosed: false }) return;
        if (Settings.Profiles.Count == 0) return;
        _wheel = new WheelWindow(profile ?? Settings.Profiles.First(), Settings.WheelSize);
        _wheel.Closed += (_, _) => _wheel = null;
        _wheel.Open(req);
        if (startChooser) _wheel.Dispatcher.BeginInvoke(() => _wheel?.StartChooserPublic());
    }

    public void ShowHub()
    {
        if (_hub == null)
        {
            _hub = new MainWindow();
            _hub.Closed += (_, _) => _hub = null;
        }
        _hub.Show();
        if (_hub.WindowState == WindowState.Minimized) _hub.WindowState = WindowState.Normal;
        _hub.Activate();
    }

    void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Text = "Bergamota",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("Abrir Bergamota", null, (_, _) => ShowHub());
        _tray.ContextMenuStrip.Items.Add("Buscar aparelhos agora", null, async (_, _) => await Devices.ScanAsync(deep: true));
        _tray.ContextMenuStrip.Items.Add("Testar a roda", null, (_, _) => PreviewWheel());
        _tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Sair", null, (_, _) => Shutdown());
        _tray.MouseClick += (_, ev) => { if (ev.Button == Forms.MouseButtons.Left) ShowHub(); };
    }

    public void PreviewWheel(Profile? profile = null)
    {
        Native.GetCursorPos(out var pt);
        var (b, _, _) = Native.MonitorAt(pt);
        OpenWheel(new WheelRequest(new Native.POINT((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2), TriggerSource.Preview), profile, false);
    }

    static void ApplyStartWithWindows()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            var exe = Environment.ProcessPath;
            if (Settings.StartWithWindows && exe != null && !exe.Contains(Path.Combine("bin", "Debug"), StringComparison.OrdinalIgnoreCase))
                k.SetValue("Bergamota", $"\"{exe}\" --tray");
            else if (!Settings.StartWithWindows)
                k.DeleteValue("Bergamota", false);
        }
        catch (Exception ex) { ActivityLog.Error("Iniciar com o Windows", ex.Message, notify: false); }
    }
}
