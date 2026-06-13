using System;
using System.Collections.Generic;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Eje de continuidad de un borde: X (cara vertical) o Y (cara horizontal).</summary>
public enum EjeBorde { X, Y }

/// <summary>Segmento ancla de un conector de borde, en metros, con su eje.</summary>
public readonly record struct SegmentoBorde(double X0, double Y0, double X1, double Y1, EjeBorde Eje);

/// <summary>Una arista de losa con su tipo visual y coordenadas mundo (metros).</summary>
public readonly record struct AristaHachura(BorderKind Kind, double X0, double Y0, double X1, double Y1);

/// <summary>Un borde localizado por hit-test: el <see cref="BordeAdic"/> y la colección (eje) a la que pertenece.</summary>
public readonly record struct BordeLocalizado(BordeAdic Borde, EjeBorde Eje);

/// <summary>
/// Geometría pura de los bordes de continuidad para Planta 2D (UI1.8). Sin estado,
/// sin I/O, sin Avalonia — testeable en aislamiento.
/// </summary>
public static class BordesPlantaService
{
    /// <summary>Tolerancia de contacto entre caras, en metros.</summary>
    public const double TolContactoM = 0.05;

    /// <summary>
    /// Eje del borde a crear entre dos losas seleccionadas libremente: si la
    /// separación de centroides domina en X las losas van lado a lado (cara ⟂ Lx
    /// ⇒ <see cref="EjeBorde.X"/>); si domina en Y, apiladas (⇒ <see cref="EjeBorde.Y"/>).
    /// Empate resuelto a X (convención del flujo histórico, ahora explícita).
    /// </summary>
    public static EjeBorde EjeInferido(Losa a, Losa b)
    {
        double dx = Math.Abs((a.CoordenadaX + a.Lx / 2) - (b.CoordenadaX + b.Lx / 2));
        double dy = Math.Abs((a.CoordenadaY + a.Ly / 2) - (b.CoordenadaY + b.Ly / 2));
        return dx >= dy ? EjeBorde.X : EjeBorde.Y;
    }

    /// <summary>
    /// Si <paramref name="a"/> y <paramref name="b"/> comparten una cara (hueco ≤
    /// <paramref name="tol"/> y solape &gt; tol en el eje paralelo), devuelve el
    /// segmento de solape en metros y su <see cref="EjeBorde"/>; si no, <c>null</c>.
    /// </summary>
    public static SegmentoBorde? SegmentoCompartido(Losa a, Losa b, double tol = TolContactoM)
    {
        double ax0 = a.CoordenadaX, ax1 = a.CoordenadaX + a.Lx;
        double ay0 = a.CoordenadaY, ay1 = a.CoordenadaY + a.Ly;
        double bx0 = b.CoordenadaX, bx1 = b.CoordenadaX + b.Lx;
        double by0 = b.CoordenadaY, by1 = b.CoordenadaY + b.Ly;

        // Cara vertical compartida (continuidad en X): A.der ~ B.izq  o  B.der ~ A.izq
        bool vertTouch = Math.Abs(ax1 - bx0) <= tol || Math.Abs(bx1 - ax0) <= tol;
        double yLo = Math.Max(ay0, by0), yHi = Math.Min(ay1, by1);
        if (vertTouch && yHi - yLo > tol)
        {
            double x = Math.Abs(ax1 - bx0) <= tol ? (ax1 + bx0) / 2 : (bx1 + ax0) / 2;
            return new SegmentoBorde(x, yLo, x, yHi, EjeBorde.X);
        }

        // Cara horizontal compartida (continuidad en Y): A.inf ~ B.sup  o  B.inf ~ A.sup
        bool horizTouch = Math.Abs(ay1 - by0) <= tol || Math.Abs(by1 - ay0) <= tol;
        double xLo = Math.Max(ax0, bx0), xHi = Math.Min(ax1, bx1);
        if (horizTouch && xHi - xLo > tol)
        {
            double y = Math.Abs(ay1 - by0) <= tol ? (ay1 + by0) / 2 : (by1 + ay0) / 2;
            return new SegmentoBorde(xLo, y, xHi, y, EjeBorde.Y);
        }

        return null;
    }

    /// <summary>
    /// Borde de continuidad más cercano al punto <c>(px,py)</c> (metros) dentro de
    /// <paramref name="tol"/>, o <c>null</c>. El ancla de cada borde es su cara
    /// compartida; si las losas no se tocan (par creado libremente), la línea
    /// centroide-a-centroide. Bordes con Ids inexistentes se ignoran.
    /// </summary>
    public static BordeLocalizado? HitTestBorde(double px, double py, Sistema sistema, double tol)
    {
        if (sistema is null) return null;

        BordeLocalizado? mejor = null;
        double mejorDist = double.MaxValue;

        void Probar(IEnumerable<BordeAdic> bordes, EjeBorde ejeColeccion)
        {
            foreach (var borde in bordes)
            {
                var a = BuscarLosa(sistema, borde.BI);
                var b = BuscarLosa(sistema, borde.BJ);
                if (a is null || b is null) continue;
                var seg = SegmentoCompartido(a, b) ?? AnclaCentroides(a, b, ejeColeccion);
                double d = DistanciaPuntoSegmento(px, py, seg.X0, seg.Y0, seg.X1, seg.Y1);
                if (d <= tol && d < mejorDist)
                {
                    mejorDist = d;
                    mejor = new BordeLocalizado(borde, ejeColeccion);
                }
            }
        }

        Probar(sistema.BordesX, EjeBorde.X);
        Probar(sistema.BordesY, EjeBorde.Y);
        return mejor;
    }

    private static Losa? BuscarLosa(Sistema s, int id)
    {
        foreach (var l in s.Losas) if (l.Id == id) return l;
        return null;
    }

    private static SegmentoBorde AnclaCentroides(Losa a, Losa b, EjeBorde eje)
        => new SegmentoBorde(
            a.CoordenadaX + a.Lx / 2, a.CoordenadaY + a.Ly / 2,
            b.CoordenadaX + b.Lx / 2, b.CoordenadaY + b.Ly / 2, eje);

    private static double DistanciaPuntoSegmento(double px, double py, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-9) return Math.Sqrt((px - x0) * (px - x0) + (py - y0) * (py - y0));
        double t = Math.Clamp(((px - x0) * dx + (py - y0) * dy) / len2, 0.0, 1.0);
        double cx = x0 + t * dx, cy = y0 + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    private static readonly BorderKind[] CuatroApoyados =
        { BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado };

    /// <summary>
    /// Las 4 aristas <c>[N, E, S, W]</c> de la losa con su <see cref="BorderKind"/>
    /// (resuelto desde <c>losa.Tipo</c> en <see cref="TipoLosa.Catalogo"/>) y sus
    /// coordenadas mundo. Tipo fuera del catálogo ⇒ 4 aristas <c>Apoyado</c>.
    /// </summary>
    public static IReadOnlyList<AristaHachura> HachuraAristas(Losa losa)
    {
        var kinds = TipoLosa.Catalogo.TryGetValue(TipoLosa.NormalizarCodigo(losa.Tipo), out var t)
            ? t.Bordes
            : CuatroApoyados;

        double x0 = losa.CoordenadaX, x1 = losa.CoordenadaX + losa.Lx;
        double y0 = losa.CoordenadaY, y1 = losa.CoordenadaY + losa.Ly;
        return new[]
        {
            new AristaHachura(kinds[0], x0, y0, x1, y0), // N — superior
            new AristaHachura(kinds[1], x1, y0, x1, y1), // E — derecha
            new AristaHachura(kinds[2], x0, y1, x1, y1), // S — inferior
            new AristaHachura(kinds[3], x0, y0, x0, y1), // W — izquierda
        };
    }
}
