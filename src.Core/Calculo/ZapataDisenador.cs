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

    // ===================================================================
    // CORTANTE UNIDIRECCIONAL (one-way shear) — ACI 318-19 §22.5
    // ===================================================================

    /// <summary>Voladizo de la zapata (mm): <c>(B − c)/2</c>, la proyección libre desde la cara de la columna.</summary>
    public static double VoladizoZapata(double bMm, double cMm) => (bMm - cMm) / 2.0;

    /// <summary>
    /// Cortante unidireccional <b>último</b> Vu (N) en la sección crítica a <c>d</c>
    /// de la cara de la columna: <c>Vu = q_u·B·(voladizo − d)</c>, con
    /// q_u = Pu/(B·L). Si el voladizo es ≤ d (zapata corta) no hay sección crítica
    /// de cortante en una dirección → 0.
    /// </summary>
    public static double CortanteUnidireccional(double puN, double bMm, double lMm, double cMm, double dMm)
    {
        double qu = PresionContactoUltima(puN, bMm, lMm);   // MPa = N/mm²
        double voladizo = VoladizoZapata(bMm, cMm);
        double franja = voladizo - dMm;
        return franja > 0 ? qu * bMm * franja : 0.0;
    }

    /// <summary>
    /// Resistencia de diseño a cortante unidireccional <b>φVc</b> (N), ACI 318-19
    /// §22.5.5.1 (sin armadura de cortante, λ=1): <c>φ·0.17·√f'c·B·d</c>, φ = <see cref="PhiCortante"/>.
    /// </summary>
    public static double ResistenciaCortanteUnidireccional(double fcMPa, double bMm, double dMm)
        => PhiCortante * 0.17 * Math.Sqrt(fcMPa) * bMm * dMm;

    /// <summary>Resultado del chequeo de cortante unidireccional de una zapata (N).</summary>
    public sealed record ChequeoZapataCortante(double VuN, double PhiVcN, double Ratio, bool Cumple);

    /// <summary>
    /// Chequea el cortante unidireccional de una zapata (ACI 318-19 §22.5):
    /// <see cref="CortanteUnidireccional"/> contra <see cref="ResistenciaCortanteUnidireccional"/>.
    /// </summary>
    public static ChequeoZapataCortante ChequeoCortanteUnidireccional(
        double puN, double bMm, double lMm, double cMm, double dMm, double fcMPa)
    {
        double vu = CortanteUnidireccional(puN, bMm, lMm, cMm, dMm);
        double phiVc = ResistenciaCortanteUnidireccional(fcMPa, bMm, dMm);
        double ratio = phiVc > 0 ? vu / phiVc : double.PositiveInfinity;
        return new ChequeoZapataCortante(vu, phiVc, ratio, vu <= phiVc + 1e-6);
    }

    // ===================================================================
    // FLEXIÓN — ACI 318-19 §13.3 (zapata como voladizo)
    // ===================================================================

    /// <summary>Factor de reducción a flexión (ACI 318-19 Tabla 21.2.1, sección controlada por tracción).</summary>
    public const double PhiFlexion = 0.90;

    /// <summary>
    /// Momento último de flexión Mu (N·mm) en la cara de la columna: la presión de
    /// contacto última actuando sobre el voladizo como ménsula →
    /// <c>Mu = q_u·B·voladizo²/2</c> (q_u = Pu/(B·L), voladizo = (B−c)/2).
    /// </summary>
    public static double MomentoFlexionZapata(double puN, double bMm, double lMm, double cMm)
    {
        double qu = PresionContactoUltima(puN, bMm, lMm);   // MPa
        double voladizo = VoladizoZapata(bMm, cMm);
        return qu * bMm * voladizo * voladizo / 2.0;
    }

    /// <summary>
    /// Acero de flexión de una zapata (mm²): <see cref="AsReqMm2"/> por el bloque
    /// rectangular de Whitney, <see cref="AsMinMm2"/> mínimo por retracción
    /// (§24.4.3.2: 0.0018·B·h), y <see cref="AsMm2"/> = el mayor de ambos.
    /// <see cref="SeccionInsuficiente"/> si el peralte no alcanza para el momento
    /// (discriminante negativo).
    /// </summary>
    public sealed record AceroZapata(double AsReqMm2, double AsMinMm2, double AsMm2, bool SeccionInsuficiente);

    /// <summary>
    /// Calcula el acero de flexión por el bloque de Whitney: resuelve <c>a</c> de
    /// <c>Mu = φ·0.85·f'c·B·a·(d − a/2)</c> (φ = <see cref="PhiFlexion"/>), con
    /// <c>As = 0.85·f'c·B·a/fy</c>, y aplica el mínimo por retracción 0.0018·B·h.
    /// </summary>
    public static AceroZapata AceroFlexionZapata(
        double muNmm, double fcMPa, double fyMPa, double bMm, double dMm, double hMm)
    {
        double asMin = 0.0018 * bMm * hMm;
        if (muNmm <= 0)
            return new AceroZapata(0.0, asMin, asMin, false);

        double disc = dMm * dMm - 2.0 * muNmm / (PhiFlexion * 0.85 * fcMPa * bMm);
        if (disc < 0)
            return new AceroZapata(double.NaN, asMin, double.NaN, true);

        double a = dMm - Math.Sqrt(disc);
        double asReq = 0.85 * fcMPa * bMm * a / fyMPa;
        return new AceroZapata(asReq, asMin, Math.Max(asReq, asMin), false);
    }

    // ===================================================================
    // CAPSTONE — diseño completo de la zapata
    // ===================================================================

    /// <summary>
    /// Diseño completo de una zapata aislada (capstone): presión de contacto +
    /// punzonamiento (§22.6) + cortante unidireccional (§22.5) + flexión/acero
    /// (§13.3). <see cref="Cumple"/> si pasan los dos cortantes y la sección de
    /// flexión es suficiente.
    /// </summary>
    public sealed record DisenoZapata(
        double QuMPa,
        ChequeoZapataPunzonamiento Punzonamiento,
        ChequeoZapataCortante Cortante,
        double MuNmm,
        AceroZapata Acero,
        bool Cumple);

    /// <summary>
    /// Diseña una zapata aislada concéntrica bajo una columna interior
    /// <paramref name="c1Mm"/>×<paramref name="c2Mm"/>, de planta
    /// <paramref name="bMm"/>×<paramref name="lMm"/>, peralte efectivo
    /// <paramref name="dMm"/> y altura <paramref name="hMm"/> (ACI 318-19).
    /// El cortante unidireccional y la flexión se evalúan en la dirección de
    /// <paramref name="bMm"/> usando el voladizo <c>(B − c1)/2</c>.
    ///
    /// <para>Supuestos: columna interior, zapata concéntrica sin momento, λ=1
    /// (hormigón de peso normal).</para>
    /// </summary>
    public static DisenoZapata DisenarZapata(
        double puN, double bMm, double lMm, double c1Mm, double c2Mm,
        double dMm, double hMm, double fcMPa, double fyMPa)
    {
        double qu = PresionContactoUltima(puN, bMm, lMm);
        var punz = ChequeoPunzonamiento(puN, bMm, lMm, c1Mm, c2Mm, dMm, fcMPa);
        var cort = ChequeoCortanteUnidireccional(puN, bMm, lMm, c1Mm, dMm, fcMPa);
        double mu = MomentoFlexionZapata(puN, bMm, lMm, c1Mm);
        var acero = AceroFlexionZapata(mu, fcMPa, fyMPa, bMm, dMm, hMm);
        bool cumple = punz.Cumple && cort.Cumple && !acero.SeccionInsuficiente;
        return new DisenoZapata(qu, punz, cort, mu, acero, cumple);
    }
}
