using System.Windows;
using System.Windows.Media;

namespace Bergamota.UI;

/// <summary>Desenho das fatias da roda: setor de anel com os quatro cantos arredondados.</summary>
public static class WheelGeometry
{
    public static Point P(Point c, double r, double deg)
    {
        var a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    /// <param name="co">raio dos cantos de fora</param>
    /// <param name="ci">raio dos cantos de dentro</param>
    public static Geometry RoundSector(Point c, double r1, double r2, double a0, double a1, double co, double ci)
    {
        double Deg(double len, double r) => len / r * 180 / Math.PI;
        double o = Deg(co, r2), i = Deg(ci, r1);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(c, r2 - co, a0), true, true);
            ctx.QuadraticBezierTo(P(c, r2, a0), P(c, r2, a0 + o), true, true);
            ctx.ArcTo(P(c, r2, a1 - o), new Size(r2, r2), 0, false, SweepDirection.Clockwise, true, true);
            ctx.QuadraticBezierTo(P(c, r2, a1), P(c, r2 - co, a1), true, true);
            ctx.LineTo(P(c, r1 + ci, a1), true, true);
            ctx.QuadraticBezierTo(P(c, r1, a1), P(c, r1, a1 - i), true, true);
            ctx.ArcTo(P(c, r1, a0 + i), new Size(r1, r1), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.QuadraticBezierTo(P(c, r1, a0), P(c, r1 + ci, a0), true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Índice da fatia sob o ponto, ou -1 (centro) / -2 (fora da roda).</summary>
    public static int HitTest(Point c, Point p, int count, double rInner, double rOuter, double start = -90)
    {
        var dx = p.X - c.X; var dy = p.Y - c.Y;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < rInner) return -1;
        if (r > rOuter || count == 0) return -2;
        var step = 360.0 / count;
        var ang = Math.Atan2(dy, dx) * 180 / Math.PI; // 0 = direita, sentido horário
        var rel = (ang - start + step / 2 + 720) % 360; // fatia 0 centrada em "start" (topo por padrão)
        return (int)(rel / step) % count;
    }
}
