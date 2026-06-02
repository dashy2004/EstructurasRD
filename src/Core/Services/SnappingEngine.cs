using System;
using System.Collections.Generic;

namespace LosasPlus.Services;

/// <summary>
/// Resultado de un intento de <i>snap</i> sobre un valor escalar.
/// </summary>
/// <param name="Valor">
/// El valor ajustado tras el enganche, o el valor original si no hubo snap.
/// </param>
/// <param name="Ajustado">
/// <c>true</c> si el valor se enganchó a un candidato o a la grilla.
/// </param>
public readonly record struct ResultadoSnap(double Valor, bool Ajustado);

/// <summary>
/// Motor de <i>snapping</i> puramente matemático del editor de dibujo manual
/// (Fase 3 del <c>PLAN_CAD_V1.md</c>).
///
/// <para>
/// No tiene ninguna dependencia de la capa de presentación: recibe coordenadas
/// en metros, busca bordes cercanos —la grilla métrica o los bordes de otras
/// losas— dentro de un umbral, y devuelve la coordenada ajustada. Toda la
/// lógica vive aquí para poder probarse de forma exhaustiva sin UI.
/// </para>
/// </summary>
public static class SnappingEngine
{
    /// <summary>Umbral de enganche por defecto, en metros (20 cm).</summary>
    public const double UmbralPorDefecto = 0.20;

    /// <summary>
    /// Engancha <paramref name="valor"/> al múltiplo de <paramref name="paso"/>
    /// más cercano, siempre que la distancia no supere <paramref name="umbral"/>.
    /// Un <paramref name="paso"/> no positivo deshabilita el snap a grilla.
    /// </summary>
    public static ResultadoSnap SnapAGrilla(double valor, double paso,
                                            double umbral = UmbralPorDefecto)
    {
        if (paso <= 0 || umbral < 0) return new ResultadoSnap(valor, false);

        double objetivo = Math.Round(valor / paso, MidpointRounding.AwayFromZero) * paso;
        return Math.Abs(objetivo - valor) <= umbral
            ? new ResultadoSnap(objetivo, true)
            : new ResultadoSnap(valor, false);
    }

    /// <summary>
    /// Engancha <paramref name="valor"/> al candidato más cercano de
    /// <paramref name="candidatos"/> que esté dentro de <paramref name="umbral"/>.
    /// Una lista vacía —o sin ningún candidato en rango— devuelve el valor
    /// original sin ajuste.
    /// </summary>
    public static ResultadoSnap SnapAValores(double valor, IEnumerable<double> candidatos,
                                             double umbral = UmbralPorDefecto)
    {
        if (candidatos is null || umbral < 0) return new ResultadoSnap(valor, false);

        bool hay = false;
        double mejor = valor;
        double mejorDist = double.PositiveInfinity;
        foreach (var c in candidatos)
        {
            double d = Math.Abs(c - valor);
            if (d <= umbral && d < mejorDist)
            {
                mejorDist = d;
                mejor = c;
                hay = true;
            }
        }
        return new ResultadoSnap(hay ? mejor : valor, hay);
    }

    /// <summary>
    /// Combina el snap a candidatos con el snap a la grilla; gana el ajuste de
    /// <b>menor desplazamiento</b>. En caso de empate gana el candidato — un
    /// borde de losa "manda" sobre la línea de grilla.
    /// </summary>
    public static ResultadoSnap SnapCombinado(double valor, IEnumerable<double> candidatos,
                                              double paso, double umbral = UmbralPorDefecto)
    {
        var porValor  = SnapAValores(valor, candidatos, umbral);
        var porGrilla = SnapAGrilla(valor, paso, umbral);

        if (!porValor.Ajustado)  return porGrilla;
        if (!porGrilla.Ajustado) return porValor;

        double dValor  = Math.Abs(porValor.Valor  - valor);
        double dGrilla = Math.Abs(porGrilla.Valor - valor);
        return dGrilla < dValor ? porGrilla : porValor;   // empate → candidato
    }

    /// <summary>
    /// Snap de un rango rígido <c>[inicio, inicio + longitud]</c> que se
    /// traslada entero —el caso de <b>mover</b> una losa—. Prueba ambos
    /// extremos contra los candidatos y la grilla, y aplica el desplazamiento
    /// de <b>menor magnitud</b> de los dos. Devuelve el <c>inicio</c> ajustado.
    /// </summary>
    public static ResultadoSnap SnapRango(double inicio, double longitud,
                                          IEnumerable<double> candidatos, double paso,
                                          double umbral = UmbralPorDefecto)
    {
        // Se recorre dos veces (un extremo cada vez) → materializar una vez.
        var lista = candidatos is null ? new List<double>() : new List<double>(candidatos);

        var snapIni = SnapCombinado(inicio, lista, paso, umbral);
        var snapFin = SnapCombinado(inicio + longitud, lista, paso, umbral);

        double dIni = snapIni.Ajustado ? snapIni.Valor - inicio : double.PositiveInfinity;
        double dFin = snapFin.Ajustado ? snapFin.Valor - (inicio + longitud) : double.PositiveInfinity;

        if (double.IsPositiveInfinity(dIni) && double.IsPositiveInfinity(dFin))
            return new ResultadoSnap(inicio, false);

        double delta = Math.Abs(dIni) <= Math.Abs(dFin) ? dIni : dFin;
        return new ResultadoSnap(inicio + delta, true);
    }
}
