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
}
