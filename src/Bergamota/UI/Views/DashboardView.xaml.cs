using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Bergamota.Models;
using Bergamota.Theme;

namespace Bergamota.UI.Views;

/// <summary>Desenho técnico da roda, no estilo do Macro Hub: fatias de vidro com legendas puxadas por linhas.</summary>
public partial class DashboardView : UserControl
{
    static readonly Point C = new(500, 300);
    const double R1 = 62, R2 = 190;
    Profile? _profile;
    Border? _tip;
    TextBlock? _tipTitle, _tipDesc;

    static readonly Brush Line = new SolidColorBrush(Color.FromArgb(0x59, 0xB9, 0xC7, 0xBD));
    static readonly Brush Dot = new SolidColorBrush(Color.FromArgb(0xB3, 0xB9, 0xC7, 0xBD));
    static readonly Brush TextB = new SolidColorBrush(Color.FromRgb(0xE8, 0xEF, 0xE9));
    static readonly Brush MutedB = new SolidColorBrush(Color.FromRgb(0x8F, 0xA1, 0x97));
    static readonly Brush IconB = new SolidColorBrush(Color.FromRgb(0xCF, 0xDC, 0xD2));
    static readonly Brush GlassFill = new LinearGradientBrush(Color.FromArgb(0x29, 255, 255, 255), Color.FromArgb(0x05, 255, 255, 255), 45);
    static readonly Brush GlassStroke = new LinearGradientBrush(
        new GradientStopCollection { new(Color.FromArgb(0xB3, 255, 255, 255), 0), new(Color.FromArgb(0x2E, 255, 255, 255), 0.5), new(Color.FromArgb(0x59, 255, 255, 255), 1) }, 45);

    public DashboardView()
    {
        InitializeComponent();
        Pills.ItemsSource = App.Settings.Profiles;
        Loaded += (_, _) => Refresh();
    }

    public Profile? CurrentProfile => _profile;

    public void Refresh()
    {
        _profile = App.Settings.Profiles.Contains(_profile!) ? _profile : App.Settings.Profiles.FirstOrDefault();
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var item in Pills.Items)
                if (Pills.ItemContainerGenerator.ContainerFromItem(item) is ContentPresenter cp && VisualTreeHelper.GetChildrenCount(cp) > 0
                    && VisualTreeHelper.GetChild(cp, 0) is RadioButton rb)
                    rb.IsChecked = item == _profile;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        Draw();
    }

    void Pill_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: Profile p } && p != _profile) { _profile = p; Draw(); }
    }

    void Draw()
    {
        Diagram.Children.Clear();
        if (_profile == null) return;

        // manchas de cor atrás do vidro
        Orb(-90, -70, 150, Color.FromRgb(0xC9, 0xDC, 0x5A), 0x55);
        Orb(110, 80, 140, Color.FromRgb(0xE8, 0x92, 0x3A), 0x45);
        Orb(30, -120, 100, Color.FromRgb(0x5F, 0xB0, 0x8A), 0x40);

        // brilho embaixo, como o LED do keypad
        var glow = new Ellipse { Width = 300, Height = 22, Effect = new BlurEffect { Radius = 18 },
            Fill = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0, 0xC9, 0xDC, 0x5A), 0), new(Color.FromArgb(0xCC, 0xC9, 0xDC, 0x5A), 0.5), new(Color.FromArgb(0, 0xC9, 0xDC, 0x5A), 1) }, 0) };
        Add(glow, C.X - 150, C.Y + 226);
        Add(new Rectangle { Width = 240, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = glow.Fill }, C.X - 120, C.Y + 233);

        Add(new Ellipse { Width = (R2 + 16) * 2, Height = (R2 + 16) * 2, Fill = GlassFill, Stroke = GlassStroke, StrokeThickness = 1.2 }, C.X - R2 - 16, C.Y - R2 - 16);
        Add(new Ellipse { Width = (R1 - 8) * 2, Height = (R1 - 8) * 2, Fill = GlassFill, Stroke = GlassStroke, StrokeThickness = 1.2 }, C.X - R1 + 8, C.Y - R1 + 8);
        var hub = new TextBlock { Text = "SOLTE\nAQUI", Foreground = new SolidColorBrush(Color.FromRgb(0x5D, 0x6E, 0x64)), FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Width = 80 };
        Add(hub, C.X - 40, C.Y - 15);

        var (slices, start) = _profile.WheelLayout(App.Settings.ConvertSlice);
        int n = slices.Count;
        if (n == 0) return;
        double step = 360.0 / n;

        // legendas: fatias da metade direita à direita e da esquerda à esquerda, ordenadas de cima pra baixo
        // (topo exato vai para a direita, base exata para a esquerda)
        var mids = Enumerable.Range(0, n).Select(i => start + i * step).ToArray();
        bool IsRight(double m) { var c = Math.Cos(m * Math.PI / 180); return c > 1e-6 || (Math.Abs(c) <= 1e-6 && Math.Sin(m * Math.PI / 180) < 0); }
        var rightOrder = Enumerable.Range(0, n).Where(i => IsRight(mids[i])).OrderBy(i => Math.Sin(mids[i] * Math.PI / 180)).ToList();
        var leftOrder = Enumerable.Range(0, n).Where(i => !IsRight(mids[i])).OrderBy(i => Math.Sin(mids[i] * Math.PI / 180)).ToList();

        for (int i = 0; i < n; i++)
        {
            var s = slices[i];
            double mid = mids[i];
            var g = new Canvas { Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            var geo = WheelGeometry.RoundSector(C, R1, R2, mid - step / 2 + 2.6, mid + step / 2 - 2.6, 16, 9);
            var path = new Path { Data = geo, Fill = GlassFill, Stroke = GlassStroke, StrokeThickness = 1.2 };
            g.Children.Add(path);
            var ic = WheelGeometry.P(C, (R1 + R2) / 2, mid);
            var col = HexToBrush.Parse(s.SwatchColor);
            var colB = new SolidColorBrush(col);
            var colFill = new LinearGradientBrush(Color.FromArgb(0x73, col.R, col.G, col.B), Color.FromArgb(0x1F, col.R, col.G, col.B), 45);
            var restIcon = IconB; // a cor fica só na fatia
            var icon = new Path
            {
                Data = Icons.Get(s.Icon), Stroke = restIcon, StrokeThickness = 2, Width = 26, Height = 26, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
            };
            Canvas.SetLeft(icon, ic.X - 13); Canvas.SetTop(icon, ic.Y - 13);
            g.Children.Add(icon);

            bool right = rightOrder.Contains(i);
            int side = right ? 1 : -1;
            int count = right ? rightOrder.Count : leftOrder.Count;
            int slot = right ? rightOrder.IndexOf(i) : leftOrder.IndexOf(i);
            double ly = C.Y + (count == 1 ? 0 : -195 + slot * (390.0 / (count - 1)));
            var a = WheelGeometry.P(C, R2 - 18, mid);
            double tx = C.X + side * 470;
            var lead = new Polyline { Points = { a, new Point(C.X + side * (R2 + 40), ly), new Point(tx, ly) }, Stroke = Line, StrokeThickness = 1, IsHitTestVisible = false };
            g.Children.Add(lead);
            g.Children.Add(DotAt(a)); g.Children.Add(DotAt(new Point(tx, ly)));

            // título acima da linha, detalhe abaixo (como no Macro Hub)
            var align = right ? TextAlignment.Right : TextAlignment.Left;
            var label = new TextBlock { Text = s.Label, Foreground = TextB, FontSize = 17, FontWeight = FontWeights.SemiBold, Width = 260, TextAlignment = align, IsHitTestVisible = false };
            var sub = new TextBlock { Text = $"{s.KindText} · {Short(s)}", Foreground = MutedB, FontSize = 12.5, Width = 260, TextAlignment = align, IsHitTestVisible = false };
            Canvas.SetLeft(label, right ? tx - 260 : tx); Canvas.SetTop(label, ly - 28);
            Canvas.SetLeft(sub, right ? tx - 260 : tx); Canvas.SetTop(sub, ly + 6);
            g.Children.Add(label); g.Children.Add(sub);

            g.MouseEnter += (_, _) => { path.Fill = colFill; path.Stroke = colB; lead.Stroke = colB; ShowTip(s, mid); };
            g.MouseLeave += (_, _) => { path.Fill = GlassFill; path.Stroke = GlassStroke; icon.Stroke = restIcon; lead.Stroke = Line; HideTip(); };
            g.MouseLeftButtonUp += (_, _) => (Window.GetWindow(this) as MainWindow)?.Navigate("edit", _profile);
            Diagram.Children.Add(g);
        }
        BuildTip();
    }

    static string Short(Slice s) => s.Kind switch
    {
        SliceKind.Native => s.Action switch
        {
            "extract-here" or "extract-folder" => "WinRAR", "zip" => "nativo", "powerrename" => "PowerRename",
            "openwith" => "escolher app", "properties" => "do Windows", "convert" => "formato · imagem · PDF", "moveto" => "pasta favorita", _ => "do Explorer",
        },
        SliceKind.Send => string.IsNullOrEmpty(s.Action) ? "escolher na hora" : "LocalSend",
        SliceKind.Program => "programa",
        _ => "Explorer",
    };

    // ---------- popup suave no hover ----------

    void BuildTip()
    {
        _tipTitle = new TextBlock { Foreground = TextB, FontWeight = FontWeights.SemiBold, FontSize = 15 };
        _tipDesc = new TextBlock { Foreground = MutedB, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 240, Margin = new Thickness(0, 3, 0, 0) };
        var sp = new StackPanel(); sp.Children.Add(_tipTitle); sp.Children.Add(_tipDesc);
        _tip = new Border
        {
            Child = sp, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 11),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x1C, 0x17)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x47, 255, 255, 255)), BorderThickness = new Thickness(1),
            Opacity = 0, IsHitTestVisible = false, RenderTransform = new TranslateTransform(),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Direction = 270, Opacity = 0.4 },
        };
        Diagram.Children.Add(_tip);
    }

    void ShowTip(Slice s, double mid)
    {
        if (_tip == null) return;
        _tipTitle!.Text = s.Label;
        _tipDesc!.Text = s.Description;
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // logo fora da borda da fatia, na direção dela
        var p = WheelGeometry.P(C, R2 + 30, mid);
        double cos = Math.Cos(mid * Math.PI / 180), sin = Math.Sin(mid * Math.PI / 180);
        double left = cos > 0.35 ? p.X : cos < -0.35 ? p.X - _tip.DesiredSize.Width : p.X - _tip.DesiredSize.Width / 2;
        double top = sin > 0.35 ? p.Y : sin < -0.35 ? p.Y - _tip.DesiredSize.Height : p.Y - _tip.DesiredSize.Height / 2;
        Canvas.SetLeft(_tip, left); Canvas.SetTop(_tip, top);
        Panel.SetZIndex(_tip, 99);
        var tt = (TranslateTransform)_tip.RenderTransform;
        tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(cos * 8, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase() });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(sin * 8, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase() });
        _tip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }

    void HideTip() => _tip?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)));

    // ---------- helpers ----------

    void Orb(double dx, double dy, double r, Color c, byte alpha)
    {
        var e = new Ellipse
        {
            Width = r * 2, Height = r * 2, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(alpha, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)),
        };
        Add(e, C.X + dx - r, C.Y + dy - r);
    }

    static Ellipse DotAt(Point p)
    {
        var e = new Ellipse { Width = 5, Height = 5, Fill = Dot, IsHitTestVisible = false };
        Canvas.SetLeft(e, p.X - 2.5); Canvas.SetTop(e, p.Y - 2.5);
        return e;
    }

    void Add(UIElement el, double x, double y)
    {
        Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
        Diagram.Children.Add(el);
    }
}
