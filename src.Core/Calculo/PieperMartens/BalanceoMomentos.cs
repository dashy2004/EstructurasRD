using System;
using System.Collections.Generic;

namespace LosasPlus.Calculo.PieperMartens;

/// <summary>Dirección de la armadura sobre el apoyo: X o Y.</summary>
public enum DireccionApoyo { X, Y }

/// <summary>
/// Momento de diseño sobre un borde compartido entre las losas I y J, en una
/// dirección. <see cref="MuI"/>/<see cref="MuJ"/> son los momentos de
/// empotramiento de cada losa; <see cref="MuIJ"/> es el momento de diseño tras
/// el balanceo. Unidades: ton·m/m.
/// </summary>
public readonly record struct MomentoApoyo(
    int LosaI, int LosaJ, DireccionApoyo Direccion, double MuI, double MuJ, double MuIJ);

/// <summary>
/// Balanceo de momentos negativos en bordes compartidos (Pieper-Martens / Perdomo).
/// Modo "S": promedia los momentos de las dos losas con un piso de 0.75·max.
/// Modo "N": no promedia — el momento de la losa I gobierna (voladizos, nudos de
/// tres lados, tramos cortos).
/// </summary>
public sealed class BalanceoMomentos
{
    /// <summary>Piso del momento balanceado: MuI-J ≥ 0.75·max(|MuI|,|MuJ|) (PDF §2.1).</summary>
    public const double FactorMinimo = 0.75;

    /// <summary>
    /// Calcula el momento de diseño del borde I–J en la dirección dada, a partir
    /// de los momentos ya calculados de cada losa.
    /// </summary>
    public MomentoApoyo Balancear(
        int losaI, int losaJ, DireccionApoyo direccion, string modo,
        IReadOnlyDictionary<int, MomentosLosa> momentos)
    {
        double muI = Componente(momentos[losaI], direccion);
        double muJ = Componente(momentos[losaJ], direccion);

        bool noBalancea = string.Equals(modo?.Trim(), "N", StringComparison.OrdinalIgnoreCase);
        double muIJ = noBalancea
            // "N": el vuelo / nudo de tres lados gobierna → momento de la losa I.
            ? muI
            // "S": promedio con piso de 0.75·max (PDF §2.1).
            : Math.Max((muI + muJ) / 2.0,
                       FactorMinimo * Math.Max(Math.Abs(muI), Math.Abs(muJ)));

        return new MomentoApoyo(losaI, losaJ, direccion, muI, muJ, muIJ);
    }

    private static double Componente(MomentosLosa m, DireccionApoyo direccion)
        => direccion == DireccionApoyo.X ? m.Msx : m.Msy;
}
