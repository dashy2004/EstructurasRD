using System;
using System.Collections.Generic;

namespace LosasPlus.Columnas;

/// <summary>
/// Un punto del diagrama de interacción P-M de una columna: el par (momento,
/// carga axial) correspondiente a un estado de deformaciones cinemáticamente
/// admisible (Fase 5, Iteración 1).
/// </summary>
/// <param name="Momento">Momento flector M, en kN·m.</param>
/// <param name="Carga">
/// Carga axial P, en kN — positiva en compresión, negativa en tracción.
/// </param>
public readonly record struct PuntoInteraccion(double Momento, double Carga);

/// <summary>
/// Diagrama de interacción P-M <b>uniaxial 2D</b> de una columna de concreto
/// reforzado por compatibilidad de deformaciones elasto-plásticas según ACI
/// 318-19 (Fase 5, Iteración 1).
///
/// <para>
/// Contiene la <see cref="CurvaNominal"/> (Mn, Pn) sin minorar y la
/// <see cref="CurvaDiseno"/> (φMn, φPn) con el tope axial <c>α·φ·P0</c>
/// aplicado en la cabeza, más las anclas analíticas cerradas
/// <see cref="PnMaximoNominal"/> (compresión pura) y
/// <see cref="TraccionPuraNominal"/> (tracción pura).
/// </para>
/// </summary>
/// <param name="CurvaNominal">Pares (Mn, Pn) sin minorar.</param>
/// <param name="CurvaDiseno">
/// Pares (φMn, φPn) con el cap axial aplicado (α = 0.80 para columnas con
/// estribos, ACI 318-19 §22.4.2.1).
/// </param>
/// <param name="PnMaximoNominal">
/// Capacidad nominal a compresión pura P0 = 0.85·f'c·(Ag − Ast) + fy·Ast, en kN.
/// </param>
/// <param name="TraccionPuraNominal">
/// Capacidad nominal a tracción pura Tn = −fy·Ast, en kN (negativo por convención).
/// </param>
public sealed record DiagramaInteraccion2D(
    IReadOnlyList<PuntoInteraccion> CurvaNominal,
    IReadOnlyList<PuntoInteraccion> CurvaDiseno,
    double PnMaximoNominal,
    double TraccionPuraNominal)
{
    /// <summary>
    /// Diagrama vacío — para una columna con sección/refuerzo inválidos o
    /// degenerados (acero nulo, materiales no positivos, etc.).
    /// </summary>
    public static DiagramaInteraccion2D Vacio { get; } = new(
        Array.Empty<PuntoInteraccion>(),
        Array.Empty<PuntoInteraccion>(),
        0.0,
        0.0);
}
