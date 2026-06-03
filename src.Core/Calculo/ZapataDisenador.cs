using System;

namespace LosasPlus.Calculo;

/// <summary>
/// Diseño de <b>zapatas aisladas</b> de hormigón (ACI 318-19), en unidades SI
/// (N, mm, MPa) — espeja y complementa a <see cref="ColumnaDisenador"/> y al
/// predimensionado por presión admisible (<c>PredimZapata</c>). Cubre la presión
/// de contacto última y, progresivamente, punzonamiento (§22.6), cortante
/// unidireccional (§22.5) y flexión (§13.3). Funciones puras, testeables headless.
/// </summary>
public static class ZapataDisenador
{
    /// <summary>
    /// Presión de contacto <b>última</b> q_u (MPa) bajo una zapata: la carga
    /// axial factorizada <paramref name="puN"/> (N) repartida sobre el área de
    /// contacto <paramref name="bMm"/>×<paramref name="lMm"/> (mm). Área no
    /// positiva devuelve 0.
    /// </summary>
    public static double PresionContactoUltima(double puN, double bMm, double lMm)
    {
        double area = bMm * lMm;
        return area > 0 ? puN / area : 0.0;
    }

    /// <summary>
    /// Perímetro crítico de punzonamiento b0 (mm) de una columna <b>interior</b>,
    /// medido a <c>d/2</c> de las caras (ACI 318-19 §22.6.4.1):
    /// <c>b0 = 2(c1+d) + 2(c2+d)</c>, con <paramref name="c1Mm"/>×<paramref name="c2Mm"/>
    /// las dimensiones de la columna y <paramref name="dMm"/> el peralte efectivo.
    /// </summary>
    public static double PerimetroCriticoPunzonamiento(double c1Mm, double c2Mm, double dMm)
        => 2.0 * (c1Mm + dMm) + 2.0 * (c2Mm + dMm);

    /// <summary>Factor de reducción de resistencia a cortante/punzonamiento (ACI 318-19 Tabla 21.2.1).</summary>
    public const double PhiCortante = 0.75;

    /// <summary>
    /// Cortante de punzonamiento <b>último</b> Vu (N) en una zapata: la carga axial
    /// <paramref name="puN"/> menos la presión de contacto que actúa <i>dentro</i>
    /// del perímetro crítico → <c>Vu = Pu·(1 − (c1+d)(c2+d)/(B·L))</c> (columna
    /// interior). Área de zapata no positiva devuelve <paramref name="puN"/>.
    /// </summary>
    public static double CortantePunzonamiento(
        double puN, double bMm, double lMm, double c1Mm, double c2Mm, double dMm)
    {
        double areaZapata = bMm * lMm;
        if (areaZapata <= 0) return puN;
        double areaInterna = (c1Mm + dMm) * (c2Mm + dMm);
        return puN * (1.0 - areaInterna / areaZapata);
    }

    /// <summary>
    /// Resistencia de diseño a punzonamiento <b>φVc</b> (N), ACI 318-19 §22.6.5.2:
    /// <c>Vc = min(0.33√f'c, 0.17(1+2/β)√f'c, 0.083(αs·d/b0+2)√f'c)·b0·d</c>, por
    /// φ = <see cref="PhiCortante"/>. <paramref name="beta"/> = lado largo/corto de
    /// la columna; <paramref name="alphaS"/> = 40 (interior), 30 (borde), 20 (esquina).
    /// </summary>
    public static double ResistenciaPunzonamiento(
        double fcMPa, double b0Mm, double dMm, double beta, double alphaS = 40.0)
    {
        double raiz = Math.Sqrt(fcMPa);
        double vc1 = 0.33 * raiz;
        double vc2 = 0.17 * (1.0 + 2.0 / beta) * raiz;
        double vc3 = 0.083 * (alphaS * dMm / b0Mm + 2.0) * raiz;
        double vcMPa = Math.Min(vc1, Math.Min(vc2, vc3));
        return PhiCortante * vcMPa * b0Mm * dMm;
    }

    /// <summary>Resultado del chequeo de punzonamiento de una zapata (N).</summary>
    public sealed record ChequeoZapataPunzonamiento(double VuN, double PhiVcN, double Ratio, bool Cumple);

    /// <summary>
    /// Chequea el punzonamiento de una zapata cuadrada/rectangular bajo una columna
    /// interior (ACI 318-19 §22.6): compone el cortante último
    /// (<see cref="CortantePunzonamiento"/>) contra la resistencia de diseño φVc
    /// (<see cref="ResistenciaPunzonamiento"/>) sobre el perímetro crítico
    /// (<see cref="PerimetroCriticoPunzonamiento"/>), con β = lado mayor/menor de la
    /// columna. <see cref="ChequeoZapataPunzonamiento.Cumple"/> si Vu ≤ φVc.
    /// </summary>
    public static ChequeoZapataPunzonamiento ChequeoPunzonamiento(
        double puN, double bMm, double lMm, double c1Mm, double c2Mm, double dMm, double fcMPa)
    {
        double vu = CortantePunzonamiento(puN, bMm, lMm, c1Mm, c2Mm, dMm);
        double b0 = PerimetroCriticoPunzonamiento(c1Mm, c2Mm, dMm);
        double beta = Math.Max(c1Mm, c2Mm) / Math.Min(c1Mm, c2Mm);
        double phiVc = ResistenciaPunzonamiento(fcMPa, b0, dMm, beta);
        double ratio = phiVc > 0 ? vu / phiVc : double.PositiveInfinity;
        return new ChequeoZapataPunzonamiento(vu, phiVc, ratio, vu <= phiVc + 1e-6);
    }
}
