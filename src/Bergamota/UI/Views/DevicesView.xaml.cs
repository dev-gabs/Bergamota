using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Bergamota.Net;

namespace Bergamota.UI.Views;

public partial class DevicesView : UserControl
{
    static readonly (int min, string text)[] Intervals = { (0, "Desligada"), (1, "A cada 1 min"), (5, "A cada 5 min"), (15, "A cada 15 min"), (30, "A cada 30 min") };
    readonly DoubleAnimation _spin = new(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };

    public DevicesView()
    {
        InitializeComponent();
        DataContext = App.Devices;
        DeviceList.ItemsSource = App.Devices.Devices;
        IntervalCombo.ItemsSource = Intervals.Select(i => i.text).ToList();
        IntervalCombo.SelectedIndex = Math.Max(0, Array.FindIndex(Intervals, i => i.min == App.Settings.ScanIntervalMin));
        AliasBox.Text = App.Settings.Alias;
        ReceiverText.Text = LocalSendInstalled() ? "LocalSend para Windows (instalado)" : "LocalSend para Windows (não instalado)";

        App.Devices.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DeviceService.IsScanning)) Dispatcher.BeginInvoke(SyncScanning); };
        App.Devices.Devices.CollectionChanged += (_, _) => SyncEmpty();
        SyncScanning();
        SyncEmpty();
    }

    void SyncEmpty() => EmptyHint.Visibility = App.Devices.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    void SyncScanning()
    {
        bool on = App.Devices.IsScanning;
        ScanBtn.IsEnabled = !on;
        ScanText.Text = on ? "Procurando…" : "Buscar agora";
        Spin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, on ? _spin : null);
    }

    async void Scan_Click(object sender, RoutedEventArgs e) => await App.Devices.ScanAsync(deep: true);

    void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalCombo.SelectedIndex < 0) return;
        App.Settings.ScanIntervalMin = Intervals[IntervalCombo.SelectedIndex].min;
        App.Settings.Save();
    }

    void Forget_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Device d) App.Devices.Forget(d);
    }

    void Alias_LostFocus(object sender, RoutedEventArgs e)
    {
        var a = AliasBox.Text.Trim();
        if (a.Length > 0 && a != App.Settings.Alias) { App.Settings.Alias = a; App.Settings.Save(); }
    }

    static bool LocalSendInstalled()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return File.Exists(Path.Combine(pf, "LocalSend", "localsend_app.exe")) || File.Exists(Path.Combine(local, "Programs", "LocalSend", "localsend_app.exe"));
    }
}
