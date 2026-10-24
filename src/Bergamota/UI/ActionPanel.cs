using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Bergamota.Core;
using Bergamota.Theme;

namespace Bergamota.UI;

/// <summary>
/// Painel de ações que aparece onde os arquivos foram soltos: cabeçalho, seções de cartões,
/// dicas e rodapé. Fundo escuro quase opaco para o texto ficar legível em qualquer papel de parede.
/// Usado por "Converter e ajustar" e "Mover para…".
/// </summary>
public sealed class ActionPanel : Window
{
    static readonly Brush TextB = new SolidColorBrush(Color.FromRgb(0xE8, 0xEF, 0xE9));
    static readonly Brush MutedB = new SolidColorBrush(Color.FromRgb(0x9A, 0xAB, 0xA1));
    static readonly Brush DimB = new SolidColorBrush(Color.FromRgb(0x6A, 0x7B, 0x71));
    static readonly Brush InkB = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x10));
    static readonly Brush AccentB = new SolidColorBrush(Color.FromRgb(0xC9, 0xDC, 0x5A));
    static readonly Brush CardB = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255));
    static readonly Brush CardStroke = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255));

    readonly StackPanel _body = new() { Width = 516 };
    readonly Border _panel;
    readonly ScaleTransform _scale = new(0.92, 0.92);
    bool _closing, _hasCards;

    public ActionPanel(string title, string subtitle, string icon)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = title;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Animated);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0xC9, 0xDC, 0x5A)),
            Child = new Path
            {
                Data = Icons.Get(icon), Stroke = AccentB, StrokeThickness = 2, Width = 20, Height = 20, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            },
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = title, Foreground = TextB, FontSize = 17, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = subtitle, Foreground = MutedB, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        head.Children.Add(titles);
        _body.Children.Add(head);

        _panel = new Border
        {
            Child = _body, Padding = new Thickness(20, 18, 20, 16), CornerRadius = new CornerRadius(20), Margin = new Thickness(24),
            Background = new LinearGradientBrush(Color.FromArgb(0xF5, 0x18, 0x22, 0x1C), Color.FromArgb(0xF5, 0x10, 0x17, 0x13), 90),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x40, 255, 255, 255), Color.FromArgb(0x12, 255, 255, 255), 90),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.5 },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _scale, Opacity = 0,
        };
        Content = _panel;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseSoft(); };
        Deactivated += (_, _) => CloseSoft();
    }

    public bool HasCards => _hasCards;

    public void AddSection(string header, IEnumerable<ToolAction> actions)
    {
        var list = actions.ToList();
        if (list.Count == 0) return;
        _hasCards = true;
        _body.Children.Add(new TextBlock { Text = header, Foreground = DimB, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
        var grid = new WrapPanel();
        foreach (var a in list) grid.Children.Add(Card(a));
        _body.Children.Add(grid);
    }

    public void AddToggle(string label, bool value, Action<bool> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var sw = new CheckBox { Style = (Style)Application.Current.FindResource("Switch"), IsChecked = value };
        sw.Checked += (_, _) => changed(true);
        sw.Unchecked += (_, _) => changed(false);
        DockPanel.SetDock(sw, Dock.Right);
        row.Children.Add(sw);
        row.Children.Add(new TextBlock { Text = label, Foreground = TextB, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        _body.Children.Add(row);
    }

    public void AddHint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _body.Children.Add(new TextBlock { Text = text, Foreground = MutedB, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
    }

    public void AddFooter(string text) =>
        _body.Children.Add(new TextBlock { Text = text, Foreground = DimB, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });

    UIElement Card(ToolAction a)
    {
        var name = new TextBlock { Text = a.Name, Foreground = TextB, FontSize = 15.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var note = new TextBlock { Text = a.Note, Foreground = MutedB, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(name); sp.Children.Add(note);
        var card = new Border
        {
            Width = 116, Height = 64, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 10, 10, 8),
            CornerRadius = new CornerRadius(14), Background = CardB, BorderBrush = CardStroke, BorderThickness = new Thickness(1),
            Child = sp, Cursor = Cursors.Hand,
        };
        card.MouseEnter += (_, _) => { card.Background = AccentB; card.BorderBrush = AccentB; name.Foreground = InkB; note.Foreground = InkB; };
        card.MouseLeave += (_, _) => { card.Background = CardB; card.BorderBrush = CardStroke; name.Foreground = TextB; note.Foreground = MutedB; };
        card.MouseLeftButtonUp += (_, _) =>
        {
            // a ação roda só depois de o painel fechar de vez: se ela abrir um diálogo do Windows
            // (ex.: escolher pasta), ele não pode ficar preso a uma janela que está sendo destruída
            if (_closing) return;
            Closed += (_, _) => Application.Current.Dispatcher.BeginInvoke(async () => await a.Run());
            CloseSoft();
        };
        return card;
    }

    /// <summary>Mostra perto do cursor, inteira dentro da tela.</summary>
    public void Open()
    {
        Native.GetCursorPos(out var pt);
        Opacity = 0;
        Show();
        UpdateLayout();
        var hwnd = new WindowInteropHelper(this).Handle;
        var (_, work, scale) = Native.MonitorAt(pt);
        int w = (int)(ActualWidth * scale), h = (int)(ActualHeight * scale);
        int x = Math.Clamp(pt.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        int y = Math.Clamp(pt.Y - h / 3, work.Top, Math.Max(work.Top, work.Bottom - h));
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, 0x1 /*SWP_NOSIZE*/);
        Opacity = 1;
        Activate();
        Native.SetForegroundWindow(hwnd);
        Focus();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        _panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
    }

    void CloseSoft()
    {
        if (_closing) return;
        _closing = true;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        a.Completed += (_, _) => Close();
        _panel.BeginAnimation(OpacityProperty, a);
    }
}
