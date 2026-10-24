using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Bergamota.Models;

namespace Bergamota.Theme;

public class IconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Icons.Get(value as string ?? p as string);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool b = value is bool v ? v : value is int i ? i > 0 : value != null && !(value is string s && s.Length == 0);
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Mostra o controle só quando o tipo da fatia é o do parâmetro (Native, ContextMenu, Program, Send).</summary>
public class KindToVisibility : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is SliceKind k && k.ToString() == (string)p ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Hex → pincel. Vazio usa o parâmetro (ou o verde-bergamota).</summary>
public class HexToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => new SolidColorBrush(Parse(value as string, p as string));

    public static Color Parse(string? hex, string? fallback = null)
    {
        foreach (var h in new[] { hex, fallback, "#C9DC5A" })
        {
            if (string.IsNullOrWhiteSpace(h)) continue;
            try { return (Color)ColorConverter.ConvertFromString(h); } catch { }
        }
        return Colors.Gray;
    }

    /// <summary>Texto escuro em cores claras, branco em cores escuras.</summary>
    public static bool IsLight(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255 > 0.55;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
