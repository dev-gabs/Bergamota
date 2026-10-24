using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Bergamota.UI.Views;

public partial class TriggersView : UserControl
{
    static readonly (int size, string text)[] Sizes = { (440, "Pequena"), (520, "Média"), (600, "Grande") };
    bool _recording;

    public TriggersView()
    {
        InitializeComponent();
        DataContext = App.Settings;
        SizeCombo.ItemsSource = Sizes.Select(s => s.text).ToList();
        SizeCombo.SelectedIndex = Array.FindIndex(Sizes, s => s.size == App.Settings.WheelSize) is var i && i >= 0 ? i : 1;
        App.Settings.Triggers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Models.TriggerSettings.HotkeyEnabled)) Dispatcher.BeginInvoke(UpdateHotkeyStatus);
            App.Settings.Save();
        };
        Loaded += (_, _) => { SyncKey(); SyncZones(); UpdateHotkeyStatus(); };
        Favorites.ItemsSource = App.Settings.FavoriteFolders;
        void syncFav() => NoFavorites.Visibility = App.Settings.FavoriteFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        App.Settings.FavoriteFolders.CollectionChanged += (_, _) => { syncFav(); App.Settings.Save(); };
        syncFav();
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta favorita para “Mover para…”", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (var dir in dlg.FolderNames)
            if (!App.Settings.FavoriteFolders.Contains(dir, StringComparer.OrdinalIgnoreCase)) App.Settings.FavoriteFolders.Add(dir);
    }

    void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string dir) App.Settings.FavoriteFolders.Remove(dir);
    }

    void SyncKey()
    {
        var key = App.Settings.Triggers.Key;
        KeyCap.Text = key switch
        {
            "CapsLock" or "Capital" => "⇪ CapsLock",
            "XButton1" => "🖱 Botão lateral",
            "XButton2" => "🖱 Botão lateral 2",
            "MButton" => "🖱 Botão do meio",
            "Space" => "␣ Espaço",
            _ => key,
        };
        foreach (var rb in Chips.Children.OfType<RadioButton>())
            if (rb.Tag is string t) rb.IsChecked = t == key || (t == "CapsLock" && key == "Capital");
    }

    void Chip_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key } && App.Settings.Triggers.Key != key)
        {
            App.Settings.Triggers.Key = key;
            SyncKey();
        }
    }

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_recording) return;
        _recording = true;
        KeyCap.Text = "Aperte uma tecla…";
        RecordBtn.Content = "Esc cancela";
        var win = Window.GetWindow(this)!;
        void handler(object s, KeyEventArgs ev)
        {
            ev.Handled = true;
            var k = ev.Key == Key.System ? ev.SystemKey : ev.Key;
            if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                KeyCap.Text = "Essa não: o Explorer usa. Tente outra…";
                return;
            }
            win.PreviewKeyDown -= handler;
            _recording = false;
            RecordBtn.Content = "Gravar outra tecla";
            if (k != Key.Escape) App.Settings.Triggers.Key = k == Key.Capital ? "CapsLock" : k.ToString();
            SyncKey();
        }
        win.PreviewKeyDown += handler;
    }

    void SyncZones()
    {
        foreach (var tb in FindAll<ToggleButton>(this))
            if (tb.Tag is string z) tb.IsChecked = App.Settings.Triggers.Edges.Contains(z);
    }

    void Zone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string z } tb) return;
        var edges = App.Settings.Triggers.Edges;
        if (tb.IsChecked == true && !edges.Contains(z)) edges.Add(z);
        if (tb.IsChecked != true) edges.Remove(z);
        App.Settings.Save();
    }

    void Size_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SizeCombo.SelectedIndex < 0) return;
        App.Settings.WheelSize = Sizes[SizeCombo.SelectedIndex].size;
        App.Settings.Save();
    }

    void UpdateHotkeyStatus()
    {
        var t = App.Settings.Triggers;
        HotkeyStatus.Text = !t.HotkeyEnabled ? "Abre a roda com o que está selecionado no Explorer"
            : App.Triggers.HotkeyRegistered ? "● Ativo: selecione arquivos no Explorer e aperte Win+Shift+B"
            : "Outro programa já usa Win+Shift+B";
        HotkeyStatus.Foreground = !t.HotkeyEnabled ? (Brush)FindResource("Muted")
            : App.Triggers.HotkeyRegistered ? (Brush)FindResource("Ok") : (Brush)FindResource("Warn");
    }

    static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var x in FindAll<T>(c)) yield return x;
        }
    }
}
