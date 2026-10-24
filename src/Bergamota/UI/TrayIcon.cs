using System.Drawing;
using System.Windows;

namespace Bergamota.UI;

/// <summary>Ícone da bandeja: o mesmo bergamota.ico do app, no tamanho certo para o DPI da tela.</summary>
public static class TrayIcon
{
    public static Icon Create()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/bergamota.ico"))
                   ?? throw new InvalidOperationException("bergamota.ico não está nos recursos");
        using var stream = info.Stream;
        // o ícone pequeno do sistema já vem escalado pelo DPI (16 a 100%, 24 a 150%, 32 a 200%...)
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }
}
