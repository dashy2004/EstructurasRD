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
}
