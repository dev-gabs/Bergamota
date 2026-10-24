using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Bergamota.Core;
using Bergamota.Models;
using Bergamota.Theme;

namespace Bergamota.UI;

/// <summary>
/// A roda contextual que aparece por cima de tudo.
/// Vidro: copia a tela atrás da roda no momento em que ela abre, desfoca e escurece.
/// Contraste: fundo da roda (50%) + fundo das fatias (34%) escurecem o que estiver atrás em ~67%,
/// o que mantém o texto branco legível até sobre papel de parede branco.
/// </summary>
public sealed class WheelWindow : Window
{
    enum Mode { Drag, Click }

    static readonly Color SliceBase = Color.FromArgb(0x57, 0x0A, 0x0E, 0x0B);
    static readonly Brush White = Brushes.White;
    static readonly Brush White75 = new SolidColorBrush(Color.FromArgb(0xBF, 255, 255, 255));
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x10));

    const double Margin_ = 30; // folga em volta para a sombra
    readonly double _d, _s, _rOuter, _rInner, _rDisk;
    readonly Point _c;
    readonly Canvas _root = new();
    readonly Canvas _slicesLayer = new();
    readonly TextBlock _hubTitle = new(), _hubSub = new();
    readonly ScaleTransform _scale = new(0.6, 0.6);

    Mode _mode;
    Profile _profile;
    List<Slice> _slices = new();
    double _start = -90; // ângulo da fatia 0 (gira para deixar "Converter para" embaixo)
    readonly List<SliceVisual> _visuals = new();
    IReadOnlyList<string> _files = Array.Empty<string>();
    int _hover = -3;
    bool _done, _choosing, _filesKnown;
    IntPtr _hwnd;

    record SliceVisual(Path Base, Path Icon, TextBlock Label, TextBlock Sub, Slice Slice);

    public WheelWindow(Profile profile, double diameter)
    {
        _profile = profile;
        _d = diameter;
        _s = _d + Margin_ * 2;
        _c = new Point(_s / 2, _s / 2);
        _rDisk = _d / 2;
        _rOuter = _rDisk - 12;
        _rInner = _d * 0.21 + 8;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        AllowDrop = true;
        Width = Height = _s;
        Title = "Bergamota";
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        // texto suave como no navegador (sem encaixar na grade de pixels)
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Animated);

        _root.Width = _root.Height = _s;
        _root.RenderTransformOrigin = new Point(0.5, 0.5);
        _root.RenderTransform = _scale;
        _root.Opacity = 0;
        Content = _root;

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetHover(-3);
        Drop += OnDrop;
        MouseMove += (_, e) => { if (_mode == Mode.Click) SetHover(HitAt(e.GetPosition(_root))); };
        MouseLeftButtonUp += OnClick;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseWheel(); };
        Deactivated += (_, _) => { if (_mode == Mode.Click && !_done) CloseWheel(); };
    }

    // ---------------- abrir ----------------

    public void Open(WheelRequest req)
    {
        _mode = req.Source is TriggerSource.Hotkey or TriggerSource.Preview ? Mode.Click : Mode.Drag;
        if (req.Files is { Count: > 0 })
        {
            _files = req.Files;
            _filesKnown = true;
            if (App.Settings.AutoProfile) _profile = App.PickProfile(_files);
        }

        // posição em pixels físicos, no monitor do cursor, com a roda inteira dentro da tela
        var (_, work, scale) = Native.MonitorAt(req.Point);
        int sizePx = (int)Math.Round(_s * scale), half = sizePx / 2, diskHalf = (int)(_rDisk * scale) + 10;
        int cx = Math.Clamp(req.Point.X, work.Left + diskHalf, Math.Max(work.Left + diskHalf, work.Right - diskHalf));
        int cy = Math.Clamp(req.Point.Y, work.Top + diskHalf, Math.Max(work.Top + diskHalf, work.Bottom - diskHalf));
        int x = cx - half, y = cy - half;

        var backdrop = CaptureScreen(x, y, sizePx, sizePx); // antes de a janela existir na tela
        BuildGlass(backdrop);
        _root.Children.Add(_slicesLayer);
        BuildHub();
        LoadSlices();

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_TOOLWINDOW;
        if (_mode == Mode.Drag) ex |= Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        // duas vezes: a primeira leva a janela ao monitor (o WPF ajusta o DPI), a segunda acerta o tamanho
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, sizePx, sizePx, Native.SWP_NOACTIVATE);
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, sizePx, sizePx, Native.SWP_NOACTIVATE);
        Show();
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, sizePx, sizePx, Native.SWP_NOACTIVATE);
        if (_mode == Mode.Click) { Activate(); Native.SetForegroundWindow(_hwnd); Focus(); }

        var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        App.Devices.RefreshIfStale();
    }

    /// <summary>O arraste acabou fora da roda: fecha se nada foi solto nela.</summary>
    public void OnDragEnded()
    {
        if (_mode != Mode.Drag || _choosing) return;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        t.Tick += (_, _) => { t.Stop(); if (!_done && !_choosing) CloseWheel(); };
        t.Start();
    }

    public bool IsClosed { get; private set; }

    void CloseWheel()
    {
        if (IsClosed) return;
        IsClosed = true;
        _done = true;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        a.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, a);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(120)));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(120)));
    }

    // ---------------- visual ----------------

    static BitmapSource? CaptureScreen(int x, int y, int w, int h)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            var hb = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { Native.DeleteObject(hb); }
        }
        catch { return null; }
    }

    void BuildGlass(BitmapSource? backdrop)
    {
        var disk = new EllipseGeometry(_c, _rDisk, _rDisk);
        if (backdrop != null)
        {
            var holder = new Canvas { Width = _s, Height = _s, Clip = disk };
            holder.Children.Add(new Image
            {
                Source = backdrop, Width = _s, Height = _s, Stretch = Stretch.Fill,
                Effect = new BlurEffect { Radius = 38, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
            });
            _root.Children.Add(holder);
        }
        // sombra suave + véu escuro + anéis (claro por dentro, escuro por fora: borda visível em qualquer fundo)
        _root.Children.Add(At(new Ellipse
        {
            Width = _d, Height = _d, Fill = new SolidColorBrush(Color.FromArgb(backdrop != null ? (byte)0x80 : (byte)0xD9, 0x0E, 0x12, 0x0F)),
            Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.45 },
        }, _c.X - _rDisk, _c.Y - _rDisk));
        _root.Children.Add(At(new Ellipse { Width = _d + 4, Height = _d + 4, Stroke = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), StrokeThickness = 1.5 }, _c.X - _rDisk - 2, _c.Y - _rDisk - 2));
        _root.Children.Add(At(new Ellipse
        {
            Width = _d - 2, Height = _d - 2, StrokeThickness = 1.2,
            Stroke = new LinearGradientBrush(Color.FromArgb(0x52, 255, 255, 255), Color.FromArgb(0x1A, 255, 255, 255), 90),
        }, _c.X - _rDisk + 1, _c.Y - _rDisk + 1));
    }

    void BuildHub()
    {
        double r = _rInner - 12;
        _root.Children.Add(At(new Ellipse
        {
            Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(Color.FromArgb(0x66, 0x0A, 0x0E, 0x0B)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x47, 255, 255, 255)), StrokeThickness = 1.2,
        }, _c.X - r, _c.Y - r));
        var sp = new StackPanel { Width = r * 1.7, IsHitTestVisible = false };
        _hubTitle.Foreground = White; _hubTitle.FontWeight = FontWeights.SemiBold; _hubTitle.FontSize = 14;
        _hubTitle.TextAlignment = TextAlignment.Center; _hubTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _hubSub.Foreground = White75; _hubSub.FontSize = 11; _hubSub.TextAlignment = TextAlignment.Center;
        _hubSub.TextWrapping = TextWrapping.Wrap; _hubSub.Margin = new Thickness(0, 2, 0, 0);
        sp.Children.Add(_hubTitle); sp.Children.Add(_hubSub);
        _root.Children.Add(sp);
        sp.Loaded += (_, _) => { Canvas.SetLeft(sp, _c.X - sp.ActualWidth / 2); Canvas.SetTop(sp, _c.Y - sp.ActualHeight / 2); };
        sp.SizeChanged += (_, _) => { Canvas.SetLeft(sp, _c.X - sp.ActualWidth / 2); Canvas.SetTop(sp, _c.Y - sp.ActualHeight / 2); };
        ResetHubText();
    }

    void ResetHubText()
    {
        if (_choosing) return;
        _hubTitle.Text = _filesKnown ? (_files.Count == 1 ? "1 arquivo" : $"{_files.Count} arquivos") : _profile.Name;
        _hubSub.Text = _filesKnown ? ActionRunner.Describe(_files) : (_mode == Mode.Drag ? "solte numa fatia" : "prévia");
    }

    void BuildSlices()
    {
        _slicesLayer.Children.Clear();
        _visuals.Clear();
        int n = _slices.Count;
        if (n == 0) return;
        double step = 360.0 / n;
        var sheen = new LinearGradientBrush(Color.FromArgb(0x29, 255, 255, 255), Color.FromArgb(0x00, 255, 255, 255), 90);
        // borda bem discreta: só um brilho em cima, quase some embaixo
        var stroke = new LinearGradientBrush(Color.FromArgb(0x38, 255, 255, 255), Color.FromArgb(0x0C, 255, 255, 255), 75);
        sheen.Freeze(); stroke.Freeze();
        double gap = n > 6 ? 2.4 : 2.0;
        double labelW = Math.Min(130, 2 * Math.PI * ((_rInner + _rOuter) / 2) / n - 14);

        for (int i = 0; i < n; i++)
        {
            var s = _slices[i];
            double mid = _start + i * step;
            var geo = WheelGeometry.RoundSector(_c, _rInner, _rOuter, mid - step / 2 + gap, mid + step / 2 - gap, 24, 13);
            var basePath = new Path { Data = geo, Fill = new SolidColorBrush(SliceBase), Stroke = stroke, StrokeThickness = 0.8 };
            _slicesLayer.Children.Add(basePath);
            _slicesLayer.Children.Add(new Path { Data = geo, Fill = sheen, IsHitTestVisible = false });

            var center = WheelGeometry.P(_c, (_rInner + _rOuter) / 2 + 4, mid);
            var icon = new Path
            {
                Data = Icons.Get(s.Icon), Stroke = White, StrokeThickness = 2, Width = 20, Height = 20, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            // rótulo quebra em até 2 linhas em vez de cortar; o bloco todo fica centrado na fatia
            var label = new TextBlock
            {
                Text = s.Label.ToUpperInvariant(), Foreground = White, FontWeight = FontWeights.SemiBold, FontSize = 11,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 28,
                TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 13.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 6, 0, 0),
            };
            var sub = new TextBlock
            {
                Text = SubText(s), Foreground = White75, FontSize = 10,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0),
            };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            var block = new StackPanel { Width = labelW, IsHitTestVisible = false };
            block.Children.Add(icon); block.Children.Add(label); block.Children.Add(sub);
            block.Measure(new Size(labelW, double.PositiveInfinity));
            _slicesLayer.Children.Add(At(block, center.X - labelW / 2, center.Y - block.DesiredSize.Height / 2));
            _visuals.Add(new SliceVisual(basePath, icon, label, sub, s));
        }
    }

    static string SubText(Slice s) => s.Kind switch
    {
        SliceKind.Native => s.Action switch
        {
            "extract-here" or "extract-folder" => "WinRAR",
            "zip" => "nativo",
            "powerrename" => "PowerRename",
            "properties" => "do Windows",
            "copypath" => "do Explorer",
            "convert" => "formato · imagem · PDF",
            "sendfolder" => "copiar para a pasta",
            "addfolder" => "nova favorita",
            "moveto" => "pasta favorita",
            _ => "escolher app",
        },
        SliceKind.ContextMenu => "menu de contexto",
        SliceKind.Program => "programa",
        _ => string.IsNullOrEmpty(s.Action) ? "escolher aparelho"
            : App.Devices.Find(s.Action) is { } d ? (d.IsOnline ? "LocalSend" : "offline") : "LocalSend",
    };

    // ---------------- hover ----------------

    int HitAt(Point p) => WheelGeometry.HitTest(_c, p, _slices.Count, _rInner - 2, _rDisk, _start);

    void SetHover(int idx)
    {
        if (idx == _hover) return;
        if (_hover >= 0 && _hover < _visuals.Count) Paint(_visuals[_hover], false);
        _hover = idx;
        if (idx >= 0 && idx < _visuals.Count)
        {
            var v = _visuals[idx];
            Paint(v, true);
            if (!_choosing)
            {
                _hubTitle.Text = v.Slice.Label;
                _hubSub.Text = _filesKnown ? ActionRunner.Describe(_files) : v.Slice.KindText;
            }
        }
        else ResetHubText();
    }

    /// <summary>Cor da fatia: a escolhida no editor, ou verde-bergamota (laranja para "Enviar para").</summary>
    static Color SliceColor(Slice s) => HexToBrush.Parse(s.Color, s.Kind == SliceKind.Send ? "#C45218" : "#C9DC5A");

    void Paint(SliceVisual v, bool on)
    {
        var color = SliceColor(v.Slice);
        var brush = (SolidColorBrush)v.Base.Fill;
        if (brush.IsFrozen) { brush = brush.Clone(); v.Base.Fill = brush; }
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(on ? Color.FromArgb(0xF2, color.R, color.G, color.B) : SliceBase, TimeSpan.FromMilliseconds(110)));
        bool dark = on && HexToBrush.IsLight(color);
        v.Icon.Stroke = dark ? Ink : White; // o ícone não leva a cor; só a fatia
        v.Label.Foreground = dark ? Ink : White;
        v.Sub.Foreground = dark ? Ink : White75;
    }

    // ---------------- arrastar e soltar / clique ----------------

    void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        if (!_filesKnown && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _files = files;
            _filesKnown = true;
            if (App.Settings.AutoProfile)
            {
                var p = App.PickProfile(files);
                if (p != _profile) { _profile = p; LoadSlices(); }
            }
            ResetHubText();
        }
        OnDragOver(sender, e);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        SetHover(HitAt(e.GetPosition(_root)));
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) { _files = files; _filesKnown = true; }
        var idx = HitAt(e.GetPosition(_root));
        Execute(idx);
    }

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_mode != Mode.Click) return;
        Execute(HitAt(e.GetPosition(_root)));
    }

    void Execute(int idx)
    {
        if (idx < 0 || idx >= _slices.Count) { CloseWheel(); return; }
        var slice = _slices[idx];
        if (slice.Kind == SliceKind.Send && string.IsNullOrEmpty(slice.Action) && !_choosing && _files.Count > 0)
        {
            StartChooser();
            return;
        }
        _done = true;
        var files = _files;
        // só depois de a roda fechar de vez: diálogos como "Abrir com", Propriedades ou escolher pasta
        // não podem ficar presos a uma janela que está sendo destruída (isso derruba o app)
        Closed += (_, _) => Application.Current.Dispatcher.BeginInvoke(async () => await ActionRunner.RunAsync(slice, files, IntPtr.Zero));
        CloseWheel();
    }

    // ---------------- "Enviar para…": escolher aparelho na própria roda ----------------

    public void StartChooserPublic() { if (!IsClosed) StartChooser(); }

    void StartChooser()
    {
        _choosing = true;
        _mode = Mode.Click;
        var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64() & ~(long)Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        Activate();
        Native.SetForegroundWindow(_hwnd);
        Focus();
        FillChooser();
        if (!OnlineDevices().Any())
        {
            // as pastas já aparecem; os aparelhos entram quando a busca terminar
            _hubSub.Text = "procurando aparelhos…";
            Dispatcher.BeginInvoke(async () =>
            {
                await App.Devices.ScanAsync(deep: true);
                if (IsClosed) return;
                FillChooser();
                if (!OnlineDevices().Any()) _hubSub.Text = "nenhum aparelho com LocalSend aberto";
            });
        }
    }

    static IEnumerable<Net.Device> OnlineDevices() => App.Devices.Devices.Where(d => d.IsOnline && d.ShowInWheel);

    /// <summary>
    /// "Enviar para": aparelhos do LocalSend + pastas favoritas (copia para elas) + "Adicionar pasta…".
    /// No máximo 8 fatias; "Adicionar pasta…" sempre fica.
    /// </summary>
    void FillChooser()
    {
        var items = OnlineDevices().Select(d => Slice.Create(SliceKind.Send, d.Fingerprint, d.Alias, d.IconName)).Take(4).ToList();
        foreach (var dir in App.Settings.FavoriteFolders.Where(System.IO.Directory.Exists))
        {
            if (items.Count >= 7) break;
            var name = System.IO.Path.GetFileName(dir.TrimEnd('\\'));
            var s = Slice.Create(SliceKind.Native, "sendfolder", name.Length > 0 ? name : dir, "folder");
            s.Args = dir;
            items.Add(s);
        }
        items.Add(Slice.Create(SliceKind.Native, "addfolder", "Adicionar pasta…", "plus-circle"));
        _slices = items;
        _start = -90;
        _hover = -3;
        BuildSlices();
        _hubTitle.Text = "Enviar para";
        _hubSub.Text = ActionRunner.Describe(_files);
    }

    void LoadSlices()
    {
        (_slices, _start) = _profile.WheelLayout(App.Settings.ConvertSlice);
        _hover = -3;
        BuildSlices();
    }

    static T At<T>(T el, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        return el;
    }
}
