using System;
using LosasPlus.Models;

namespace LosasPlus.Calculo;

/// <summary>
/// Diseño de acero a flexión para vigas rectangulares (ACI 318-19, método de
/// resistencia / LRFD). Funciones <b>puras</b> (no tocan el modelo), del mismo
/// estilo que <see cref="ZapataDisenador"/> y <see cref="AcerosLosaDesigner"/>.
///
/// <para><b>Unidades.</b> Las entradas siguen la convención SI del motor de
/// vigas: momentos en <b>kN·m</b> (como los reporta <c>EnvolventeViga</c>),
/// dimensiones de la sección en <b>metros</b> y materiales en <b>MPa</b>. El
/// cálculo interno del bloque de Whitney se hace en N·mm/MPa (idéntico a
/// <see cref="ZapataDisenador.AceroFlexionZapata"/>) y el acero resultante queda
/// en <b>mm²</b>.</para>
///
/// <para><b>Flexión (sección simplemente armada).</b> Resolviendo
/// <c>Mu = φ·0.85·f'c·b·a·(d − a/2)</c> con <c>As = 0.85·f'c·b·a/fy</c> se obtiene
/// el acero requerido; si el discriminante es negativo el peralte no alcanza como
/// simplemente armada y se marca <see cref="DisenoFlexionViga.SeccionInsuficiente"/>.</para>
///
/// <para><b>Mínimo a flexión en vigas</b>, ACI 318-19 §9.6.1.1:
/// <c>As_min = max(0.25·√f'c/fy, 1.4/fy)·b·d</c> (f'c, fy en MPa).</para>
/// </summary>
public static class VigaFlexionDesigner
{
    /// <summary>Factor de reducción a flexión (controlado por tracción), ACI 318-19 Tabla 21.2.1.</summary>
    public const double PhiFlexion = 0.90;

    /// <summary>Recubrimiento por defecto al centro de la barra longitudinal (m): recubrimiento libre + estribo ≈ 0.04 m.</summary>
    public const double RecubrimientoDefaultM = 0.04;

    /// <summary>Mínimo de barras por cara en una viga (práctica: ≥ 2 para alojar los estribos).</summary>
    public const int MinBarrasPorCara = 2;

    /// <summary>Área nominal de una barra ASTM A615 (mm²), reusando <see cref="AreasBarras"/> (cm² → mm²).</summary>
    public static double AreaBarraMm2(int numero) => AreasBarras.Para(numero) * 100.0;

    /// <summary>
    /// Acero requerido por flexión (mm²) por el bloque de Whitney. Devuelve
    /// <c>insuficiente=true</c> y NaN si la sección no alcanza como simplemente
    /// armada. <paramref name="muKNm"/> se toma en valor absoluto.
    /// </summary>
    public static double AsRequeridoMm2(double muKNm, double bM, double dM, double fcMPa, double fyMPa, out bool insuficiente)
    {
        insuficiente = false;
        double mu = Math.Abs(muKNm);
        if (mu <= 0) return 0.0;
        if (bM <= 0 || dM <= 0 || fcMPa <= 0 || fyMPa <= 0)
            throw new ArgumentOutOfRangeException(nameof(dM), "b, d, f'c y fy deben ser positivos.");

        double muNmm = mu * 1_000_000.0;   // kN·m → N·mm
        double bMm = bM * 1000.0;
        double dMm = dM * 1000.0;
        double disc = dMm * dMm - 2.0 * muNmm / (PhiFlexion * 0.85 * fcMPa * bMm);
        if (disc < 0) { insuficiente = true; return double.NaN; }
        double a = dMm - Math.Sqrt(disc);
        return 0.85 * fcMPa * bMm * a / fyMPa;
    }

    /// <summary>Acero mínimo a flexión en vigas (mm²), ACI 318-19 §9.6.1.1.</summary>
    public static double AsMinimoMm2(double bM, double dM, double fcMPa, double fyMPa)
    {
        if (bM <= 0 || dM <= 0 || fcMPa <= 0 || fyMPa <= 0)
            throw new ArgumentOutOfRangeException(nameof(dM), "b, d, f'c y fy deben ser positivos.");
        double bMm = bM * 1000.0;
        double dMm = dM * 1000.0;
        double rho = Math.Max(0.25 * Math.Sqrt(fcMPa) / fyMPa, 1.4 / fyMPa);
        return rho * bMm * dMm;
    }

    /// <summary>
    /// Número de barras (≥ <see cref="MinBarrasPorCara"/>) de un diámetro dado
    /// para cubrir <paramref name="asMm2"/>.
    /// </summary>
    public static int NumeroBarras(double asMm2, int numeroBarra)
    {
        double ab = AreaBarraMm2(numeroBarra);
        if (ab <= 0) throw new ArgumentOutOfRangeException(nameof(numeroBarra), "Número de barra fuera de rango (#3–#8).");
        int n = asMm2 <= 0 ? 0 : (int)Math.Ceiling(asMm2 / ab - 1e-9);
        return Math.Max(MinBarrasPorCara, n);
    }

    /// <summary>
    /// Diseña una cara en tracción de la viga (inferior por +M, superior por −M):
    /// canto útil <c>d = h − rec − db/2</c>, As requerido vs As mínimo, As de
    /// diseño = el mayor, y el número de barras del diámetro elegido.
    /// </summary>
    public static DisenoFlexionViga DisenarCara(
        string cara, double muKNm, double bM, double hM, double fcMPa, double fyMPa,
        double recubrimientoM, int numeroBarra)
    {
        double dbM = AcerosLosaDesigner.DiametroBarraCm(numeroBarra) / 100.0;   // cm → m
        double dM = hM - recubrimientoM - dbM / 2.0;
        if (dM <= 0)
            return new DisenoFlexionViga(cara, muKNm, dM, double.NaN, double.NaN, double.NaN,
                                         true, numeroBarra, 0, 0.0);

        double asReq = AsRequeridoMm2(muKNm, bM, dM, fcMPa, fyMPa, out bool insuf);
        double asMin = AsMinimoMm2(bM, dM, fcMPa, fyMPa);
        if (insuf)
            return new DisenoFlexionViga(cara, muKNm, dM, asReq, asMin, double.NaN,
                                         true, numeroBarra, 0, 0.0);

        double asDiseno = Math.Max(asReq, asMin);
        int nb = NumeroBarras(asDiseno, numeroBarra);
        double asProv = nb * AreaBarraMm2(numeroBarra);
        return new DisenoFlexionViga(cara, muKNm, dM, asReq, asMin, asDiseno,
                                     false, numeroBarra, nb, asProv);
    }

    /// <summary>
    /// Diseña la sección de un tramo a partir de los momentos de diseño de la
    /// envolvente: cara inferior por el momento positivo
    /// (<paramref name="momentoPositivoKNm"/>, tracción abajo) y cara superior por
    /// el negativo (<paramref name="momentoNegativoKNm"/>, tracción arriba). Donde
    /// el momento es ~0 gobierna el As mínimo (siempre ≥ 2 barras por cara).
    /// </summary>
    public static DisenoFlexionTramo DisenarTramo(
        double momentoPositivoKNm, double momentoNegativoKNm,
        double bM, double hM, double fcMPa, double fyMPa,
        double recubrimientoM = RecubrimientoDefaultM, int numeroBarra = 5)
    {
        var inferior = DisenarCara("Inferior", Math.Max(0.0, momentoPositivoKNm), bM, hM, fcMPa, fyMPa, recubrimientoM, numeroBarra);
        var superior = DisenarCara("Superior", Math.Abs(Math.Min(0.0, momentoNegativoKNm)), bM, hM, fcMPa, fyMPa, recubrimientoM, numeroBarra);
        return new DisenoFlexionTramo(inferior, superior, numeroBarra);
    }
}

/// <summary>
/// Diseño de acero a flexión de una cara de la viga. Momento en kN·m, canto útil
/// en m, áreas en mm².
/// </summary>
public sealed record DisenoFlexionViga(
    string Cara,
    double MuKNm,
    double DM,
    double AsRequeridoMm2,
    double AsMinimoMm2,
    double AsMm2,
    bool   SeccionInsuficiente,
    int    NumeroBarra,
    int    NumeroDeBarras,
    double AsProvistaMm2)
{
    /// <summary>True si el mínimo a flexión gobierna sobre el requerido.</summary>
    public bool GobiernaMinimo => !SeccionInsuficiente && AsMinimoMm2 >= AsRequeridoMm2;
}

/// <summary>Diseño a flexión de la sección de un tramo: cara inferior (+M) y superior (−M).</summary>
public sealed record DisenoFlexionTramo(
    DisenoFlexionViga Inferior,
    DisenoFlexionViga Superior,
    int NumeroBarra);
