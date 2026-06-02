using System;
using System.Collections.Generic;

namespace LosasPlus.Services;

/// <summary>
/// Clasificación de la relación de adyacencia entre dos losas (A3 de la FASE A
/// del <c>PLAN_V1.1_DIBUJO_AVANZADO.md</c>).
/// </summary>
public enum ClasificacionAdyacencia
{
    /// <summary>Las losas no forman una adyacencia (demasiado separadas).</summary>
    SinRelacion,
    /// <summary>En contacto, con la cara compartida completa (bordes alineados).</summary>
    Total,
    /// <summary>En contacto, compartiendo sólo una parte de la cara (hay desfase).</summary>
    Parcial,
    /// <summary>Caras solapadas en el eje paralelo, pero separadas por una holgura.</summary>
    Holgura,
    /// <summary>Desplazadas tanto que no hay cara compartida; la vecina quedó hacia el extremo inicial del eje de la cara.</summary>
    DesfaseSuperior,
    /// <summary>Desplazadas tanto que no hay cara compartida; la vecina quedó hacia el extremo final del eje de la cara.</summary>
    DesfaseInferior,
}

/// <summary>
/// Resultado inmutable del análisis de adyacencia parcial entre dos losas.
/// Todas las distancias en metros. Cuando <paramref name="EsBordeX"/> es
/// <c>true</c> la cara compartida es vertical (el eje paralelo a la cara es Y);
/// si es <c>false</c> la cara es horizontal (eje paralelo X). <c>Superior</c> /
/// <c>Inferior</c> denotan el extremo inicial / final de ese eje paralelo.
/// </summary>
/// <param name="Clasificacion">Tipo de relación detectada.</param>
/// <param name="EsBordeX">True si la cara compartida es vertical (adyacencia en X).</param>
/// <param name="LongitudCaraCompartida">Longitud del tramo de cara compartida (0 si no hay).</param>
/// <param name="CaraInicio">Coordenada inicial de la cara compartida en el eje paralelo.</param>
/// <param name="CaraFin">Coordenada final de la cara compartida en el eje paralelo.</param>
/// <param name="DesfaseSuperior">Desplazamiento relativo, con signo, del extremo inicial.</param>
/// <param name="DesfaseInferior">Desplazamiento relativo, con signo, del extremo final.</param>
/// <param name="Holgura">Separación, con signo, entre las caras en el eje de conexión (&gt; 0 = hueco).</param>
/// <param name="IndiceVecina">Índice de la vecina en el vecindario; -1 en un análisis por par.</param>
public readonly record struct AdyacenciaParcialInfo(
    ClasificacionAdyacencia Clasificacion,
    bool EsBordeX,
    double LongitudCaraCompartida,
    double CaraInicio,
    double CaraFin,
    double DesfaseSuperior,
    double DesfaseInferior,
    double Holgura,
    int IndiceVecina);

/// <summary>
/// Motor de topología de adyacencia parcial (A3 de la FASE A). Dadas losas como
/// <see cref="RectM"/>, calcula la cara compartida y los desplazamientos
/// relativos (offsets dₙ).
///
/// <para>
/// Lógica puramente matemática, sin dependencias de UI. El offset es una función
/// <b>derivada</b> de la geometría — no se almacena en la topología de cálculo:
/// <c>BordeAdic</c> y el formato <c>.DL</c> permanecen intactos.
/// </para>
/// </summary>
public static class TopologiaAdyacencia
{
    /// <summary>Tolerancia de contacto entre bordes por defecto, en metros.</summary>
    public const double ToleranciaPorDefecto = 0.05;

    /// <summary>
    /// Banda de proximidad máxima, en metros: más allá de esta separación entre
    /// las caras enfrentadas, dos losas no se consideran una adyacencia.
    /// </summary>
    private const double BandaProximidad = 1.0;

    /// <summary>
    /// Analiza la relación de adyacencia entre <paramref name="a"/> y
    /// <paramref name="b"/>: elige el eje de conexión (las caras enfrentadas más
    /// cercanas) y clasifica la relación.
    /// </summary>
    public static AdyacenciaParcialInfo Analizar(
        RectM a, RectM b, double tolerancia = ToleranciaPorDefecto)
        => Analizar(a, b, tolerancia, indiceVecina: -1);

    /// <summary>
    /// Analiza <paramref name="activa"/> contra cada losa de
    /// <paramref name="otras"/>. Cada resultado lleva el índice de su vecina en
    /// <see cref="AdyacenciaParcialInfo.IndiceVecina"/>.
    /// </summary>
    public static IReadOnlyList<AdyacenciaParcialInfo> AnalizarVecindario(
        RectM activa, IReadOnlyList<RectM> otras, double tolerancia = ToleranciaPorDefecto)
    {
        var resultado = new List<AdyacenciaParcialInfo>();
        if (otras is null) return resultado;
        for (int i = 0; i < otras.Count; i++)
            resultado.Add(Analizar(activa, otras[i], tolerancia, indiceVecina: i));
        return resultado;
    }

    private static AdyacenciaParcialInfo Analizar(RectM a, RectM b, double tol, int indiceVecina)
    {
        // --- Paso 1: elegir el par de caras enfrentadas más cercano ---
        double gXmas   = b.Izq - a.Der;   // b a la derecha de a
        double gXmenos = a.Izq - b.Der;   // b a la izquierda de a
        double gYmas   = b.Sup - a.Inf;   // b debajo de a
        double gYmenos = a.Sup - b.Inf;   // b encima de a

        double absXmas = Math.Abs(gXmas), absXmenos = Math.Abs(gXmenos);
        double absYmas = Math.Abs(gYmas), absYmenos = Math.Abs(gYmenos);
        double minAbs = Math.Min(Math.Min(absXmas, absXmenos), Math.Min(absYmas, absYmenos));

        if (minAbs > BandaProximidad)
            return new AdyacenciaParcialInfo(
                ClasificacionAdyacencia.SinRelacion, true, 0, 0, 0, 0, 0, 0, indiceVecina);

        // izqRect = losa del borde inicial; derRect = losa del borde final.
        // En empates de distancia se prefiere el eje X.
        bool esBordeX;
        RectM izqRect, derRect;
        double holgura;
        if (minAbs == absXmas)        { esBordeX = true;  izqRect = a; derRect = b; holgura = gXmas;   }
        else if (minAbs == absXmenos) { esBordeX = true;  izqRect = b; derRect = a; holgura = gXmenos; }
        else if (minAbs == absYmas)   { esBordeX = false; izqRect = a; derRect = b; holgura = gYmas;   }
        else                          { esBordeX = false; izqRect = b; derRect = a; holgura = gYmenos; }

        // --- Paso 2: solape en el eje paralelo a la cara + desfases ---
        double aPerpIni = esBordeX ? izqRect.Sup : izqRect.Izq;
        double aPerpFin = esBordeX ? izqRect.Inf : izqRect.Der;
        double bPerpIni = esBordeX ? derRect.Sup : derRect.Izq;
        double bPerpFin = esBordeX ? derRect.Inf : derRect.Der;

        double caraInicio = Math.Max(aPerpIni, bPerpIni);
        double caraFin    = Math.Min(aPerpFin, bPerpFin);
        double longitud   = caraFin - caraInicio;

        double desfaseSuperior = bPerpIni - aPerpIni;
        double desfaseInferior = bPerpFin - aPerpFin;

        // --- Paso 3: clasificar ---
        ClasificacionAdyacencia clasificacion;
        if (longitud <= tol)
        {
            // Sin cara compartida real (incluye el roce sólo por una esquina).
            clasificacion = bPerpIni >= aPerpFin - tol
                ? ClasificacionAdyacencia.DesfaseInferior
                : ClasificacionAdyacencia.DesfaseSuperior;
        }
        else if (holgura > tol)
        {
            clasificacion = ClasificacionAdyacencia.Holgura;
        }
        else if (Math.Abs(desfaseSuperior) <= tol && Math.Abs(desfaseInferior) <= tol)
        {
            clasificacion = ClasificacionAdyacencia.Total;
        }
        else
        {
            clasificacion = ClasificacionAdyacencia.Parcial;
        }

        return new AdyacenciaParcialInfo(
            clasificacion, esBordeX, Math.Max(0.0, longitud),
            caraInicio, caraFin, desfaseSuperior, desfaseInferior, holgura, indiceVecina);
    }
}
