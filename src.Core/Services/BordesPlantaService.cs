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
