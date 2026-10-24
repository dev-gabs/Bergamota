using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Bergamota.Core;
using Bergamota.Models;
using Bergamota.Theme;
using Microsoft.Win32;

namespace Bergamota.UI.Views;

public record Option(object Value, string Text);

public partial class EditorView : UserControl
{
    Profile? _profile;
    Slice? _iconTarget;

    public List<Option> KindOptions { get; } = new()
    {
        new(SliceKind.Native, "Ação nativa"),
        new(SliceKind.ContextMenu, "Menu de contexto"),
        new(SliceKind.Program, "Programa"),
        new(SliceKind.Send, "Enviar para"),
    };

    public ObservableCollection<Option> DeviceOptions { get; } = new();

    public record ColorOption(string Hex, string Name, bool IsAuto, bool Selected);

    public EditorView()
    {
        InitializeComponent();
        Pills.ItemsSource = App.Settings.Profiles;
        IconGrid.ItemsSource = Icons.Pickable;
        RebuildDevices();
        App.Devices.Devices.CollectionChanged += (_, _) => RebuildDevices();
        Loaded += (_, _) => Select(_profile ?? App.Settings.Profiles.FirstOrDefault());
    }

    void RebuildDevices()
    {
        DeviceOptions.Clear();
        DeviceOptions.Add(new("", "Escolher na hora (mostra os aparelhos)"));
        foreach (var d in App.Devices.Devices) DeviceOptions.Add(new(d.Fingerprint, $"{d.Alias}  ·  {d.Subtitle}"));
    }

    public void Select(Profile? p)
    {
        if (p == null) return;
        _profile = p;
        Card.DataContext = p;
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var item in Pills.Items)
                if (Pills.ItemContainerGenerator.ContainerFromItem(item) is ContentPresenter cp && VisualTreeHelper.GetChildrenCount(cp) > 0
                    && VisualTreeHelper.GetChild(cp, 0) is RadioButton rb)
                    rb.IsChecked = item == p;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void Pill_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: Profile p } && p != _profile) Select(p);
    }

    static Slice? SliceOf(object sender) => (sender as FrameworkElement)?.DataContext as Slice;
    IntPtr Hwnd => new WindowInteropHelper(Window.GetWindow(this)).Handle;

    void Up_Click(object sender, RoutedEventArgs e) => Move(SliceOf(sender), -1);
    void Down_Click(object sender, RoutedEventArgs e) => Move(SliceOf(sender), +1);

    void Move(Slice? s, int delta)
    {
        if (_profile == null || s == null) return;
        int i = _profile.Slices.IndexOf(s), j = i + delta;
        if (i < 0 || j < 0 || j >= _profile.Slices.Count) return;
        _profile.Slices.Move(i, j);
    }

    void AddSlice_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null) return;
        if (_profile.Slices.Count >= 8) { Flash("A roda aceita até 8 fatias"); return; }
        _profile.Slices.Add(Slice.Create(SliceKind.Native, "copypath"));
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null || SliceOf(sender) is not { } s) return;
        if (_profile.Slices.Count <= 2) { Flash("A roda precisa de pelo menos 2 fatias"); return; }
        _profile.Slices.Remove(s);
    }

    // ---------- ícones ----------

    void Icon_Click(object sender, RoutedEventArgs e)
    {
        _iconTarget = SliceOf(sender);
        if (_iconTarget == null) return;
        FillColors(_iconTarget);
        IconPopup.PlacementTarget = (UIElement)sender;
        IconPopup.IsOpen = true;
    }

    void FillColors(Slice s)
    {
        var auto = s.Kind == SliceKind.Send ? "#C45218" : "#C9DC5A";
        var list = new List<ColorOption> { new(auto, "Automática", true, s.Color.Length == 0) };
        list.AddRange(Slice.Palette.Select(h => new ColorOption(h, h, false, string.Equals(s.Color, h, StringComparison.OrdinalIgnoreCase))));
        ColorGrid.ItemsSource = list;
    }

    void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (_iconTarget == null || (sender as FrameworkElement)?.DataContext is not ColorOption c) return;
        _iconTarget.Color = c.IsAuto ? "" : c.Hex;
        FillColors(_iconTarget); // continua aberto para escolher o ícone também
    }

    void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_iconTarget != null && sender is Button { Tag: string name }) _iconTarget.Icon = name;
        IconPopup.IsOpen = false;
    }

    // ---------- tipos específicos ----------

    void BrowseProgram_Click(object sender, RoutedEventArgs e)
    {
        if (SliceOf(sender) is not { } s) return;
        var dlg = new OpenFileDialog { Title = "Escolha o programa", Filter = "Programas|*.exe;*.bat;*.cmd;*.lnk|Todos|*.*" };
        if (dlg.ShowDialog() == true) s.Action = dlg.FileName;
    }

    void PickMenu_Click(object sender, RoutedEventArgs e)
    {
        if (SliceOf(sender) is not { } s) return;
        var dlg = new OpenFileDialog { Title = "Escolha um arquivo de exemplo para ler o menu de contexto dele" };
        if (dlg.ShowDialog() != true) return;
        var items = ShellMenu.ListItems(dlg.FileName);
        if (items.Count == 0) { Flash("Não consegui ler o menu desse arquivo"); return; }
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var text in items)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) =>
            {
                var leaf = text.Contains(" › ") ? text[(text.LastIndexOf(" › ") + 3)..] : text;
                s.Action = leaf;
                s.Label = leaf;
            };
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (SliceOf(sender) is not { } s) return;
        var dlg = new OpenFileDialog { Title = $"Testar “{s.Label}”: escolha os arquivos", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        await ActionRunner.RunAsync(s, dlg.FileNames, Hwnd);
    }

    // ---------- rodapé ----------

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null) return;
        if (App.Settings.Profiles.Count <= 1) { Flash("Precisa existir pelo menos um perfil"); return; }
        if (MessageBox.Show($"Excluir o perfil “{_profile.Name}”?", "Bergamota", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        App.Settings.Profiles.Remove(_profile);
        App.Settings.Save();
        Select(App.Settings.Profiles.First());
    }

    void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_profile != null) ((App)Application.Current).PreviewWheel(_profile);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Save();
        ActivityLog.Info("check", "Perfil salvo", _profile?.Name ?? "");
        Flash("SALVO ✓");
    }

    async void Flash(string text)
    {
        SaveText.Text = text;
        await Task.Delay(1600);
        SaveText.Text = "SALVAR ALTERAÇÕES";
    }
}
