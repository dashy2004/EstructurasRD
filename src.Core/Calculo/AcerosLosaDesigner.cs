using System;
using System.Globalization;
using LosasPlus.Models;

namespace LosasPlus.Calculo;

/// <summary>
/// Diseño de acero a flexión para franjas de losa (ACI 318-19, método de
/// resistencia / LRFD). Funciones <b>puras</b> (no tocan el modelo), simétricas
/// en estilo a <see cref="CalculoEngine"/>.
///
/// <para><b>Unidades.</b> Todo el cálculo se hace en <b>ton·cm</b>, coherente con
/// <see cref="Sistema.Fc"/> (ton/cm²) y <see cref="Sistema.Fy"/> (ton/cm²). Los
/// momentos entran en <b>ton·m/m</b> (como los reporta el motor en
/// <see cref="MomentoLosa"/>) y se convierten a ton·cm sobre una franja de
/// <c>b = 100 cm</c> (acero "por metro"). El As resultante queda en <b>cm²/m</b>.</para>
///
/// <para><b>Flexión (sección simplemente armada).</b> Resolviendo
/// <c>Mu = φ·As·fy·(d − a/2)</c> con <c>a = As·fy/(0.85·f'c·b)</c> se obtiene la
/// forma cerrada
/// <c>As = (0.85·f'c·b/fy)·(d − √(d² − 2·Mu/(φ·0.85·f'c·b)))</c>.
/// Si el radicando es negativo la sección no alcanza como simplemente armada
/// (haría falta acero a compresión o más peralte): se marca
/// <see cref="DisenoAceroFranja.SeccionInsuficiente"/>.</para>
///
/// <para><b>Mínimos.</b> Acero por retracción y temperatura, ACI 318-19
/// §24.4.3.2: ρ = 0.0020 (fy ≤ 350 MPa), 0.0018 (Grado 420) o
/// <c>max(0.0018·420/fy, 0.0014)</c> (fy &gt; 420). <c>As_min = ρ·b·h</c>.</para>
///
/// <para><b>Espaciamiento máximo</b> a flexión en losas en dos direcciones,
/// ACI 318-19 §8.7.2.2: <c>min(2·h, 450 mm)</c>.</para>
/// </summary>
public static class AcerosLosaDesigner
{
    /// <summary>Factor de reducción a flexión (controlado por tracción), ACI 318-19 Tabla 21.2.1.</summary>
    public const double Phi = 0.90;

    /// <summary>Ancho de la franja de diseño: 1 m = 100 cm (acero "por metro").</summary>
    public const double BFranjaCm = 100.0;

    /// <summary>Recubrimiento libre por defecto para losas (cm).</summary>
    public const double RecubrimientoDefaultCm = 2.5;

    /// <summary>Módulo práctico de espaciamiento en obra (cm): los espaciamientos se redondean a múltiplos de éste.</summary>
    public const double ModuloEspaciamientoCm = 2.5;

    /// <summary>Espaciamiento mínimo práctico entre barras de losa (cm) — evita congestión.</summary>
    public const double EspaciamientoMinimoCm = 7.5;

    /// <summary>
    /// Canto útil <c>d</c> (cm) de una franja: <c>d = h − recubrimiento − db/2 − offsetCapa·db</c>.
    /// La <paramref name="capa"/> permite descontar el diámetro de la capa inferior
    /// para la dirección que va arriba (típicamente Y se apoya sobre X).
    /// </summary>
    /// <param name="hCm">Espesor total de la losa (cm).</param>
    /// <param name="recubrimientoCm">Recubrimiento libre (cm).</param>
    /// <param name="dbCm">Diámetro de la barra de esta dirección (cm).</param>
    /// <param name="capa">0 = capa inferior (más peralte), 1 = capa superior (resta un db extra).</param>
    public static double CantoUtil(double hCm, double recubrimientoCm, double dbCm, int capa = 0)
        => hCm - recubrimientoCm - dbCm / 2.0 - Math.Max(0, capa) * dbCm;

    /// <summary>
    /// As requerido por flexión (cm²/m) para una franja de 1 m. Devuelve
    /// <c>seccionInsuficiente=true</c> y As = NaN si la sección no alcanza como
    /// simplemente armada.
    /// </summary>
    /// <param name="muTonM">Momento último de diseño (ton·m/m). Se toma el valor absoluto.</param>
    /// <param name="dCm">Canto útil (cm).</param>
    /// <param name="fcTonCm2">f'c (ton/cm²).</param>
    /// <param name="fyTonCm2">fy (ton/cm²).</param>
    public static double AsRequerido(double muTonM, double dCm, double fcTonCm2, double fyTonCm2, out bool seccionInsuficiente)
    {
        seccionInsuficiente = false;
        double mu = Math.Abs(muTonM) * 100.0;            // ton·m/m → ton·cm sobre b=100 cm
        if (mu <= 0) return 0.0;
        if (dCm <= 0 || fcTonCm2 <= 0 || fyTonCm2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(dCm), "d, f'c y fy deben ser positivos.");

        double k = 0.85 * fcTonCm2 * BFranjaCm;          // ton/cm
        double radicando = dCm * dCm - 2.0 * mu / (Phi * k);
        if (radicando < 0)
        {
            seccionInsuficiente = true;
            return double.NaN;
        }
        return (k / fyTonCm2) * (dCm - Math.Sqrt(radicando));
    }

    /// <summary>
    /// Acero mínimo por retracción y temperatura (cm²/m), ACI 318-19 §24.4.3.2.
    /// </summary>
    /// <param name="hCm">Espesor total de la losa (cm).</param>
    /// <param name="fyTonCm2">fy (ton/cm²).</param>
    public static double AsMinimoTemperatura(double hCm, double fyTonCm2)
    {
        double fyKg = fyTonCm2 * 1000.0;                 // ton/cm² → kg/cm²
        double rho =
            fyKg <= 3500.0 ? 0.0020 :
            fyKg <= 4200.0 ? 0.0018 :
            Math.Max(0.0018 * 4200.0 / fyKg, 0.0014);
        return rho * BFranjaCm * hCm;
    }

    /// <summary>Espaciamiento máximo a flexión en losas en dos direcciones (cm), ACI 318-19 §8.7.2.2: min(2·h, 45 cm).</summary>
    public static double EspaciamientoMaximo(double hCm) => Math.Min(2.0 * hCm, 45.0);

    /// <summary>
    /// Selecciona la armadura (número de barra + espaciamiento) que cubre
    /// <paramref name="asDisenoCm2M"/> con la barra más pequeña posible cuyo
    /// espaciamiento práctico respete el mínimo y el máximo. Si ninguna barra
    /// del rango lo logra, devuelve la mayor con <c>cumple=false</c>.
    /// </summary>
    public static ArmaduraSeleccionada SeleccionarArmadura(double asDisenoCm2M, double espMaxCm)
    {
        if (asDisenoCm2M <= 0)
            return new ArmaduraSeleccionada(3, espMaxCm, AreasBarras.A3 * BFranjaCm / espMaxCm, true);

        ArmaduraSeleccionada? ultima = null;
        foreach (int n in new[] { 3, 4, 5, 6 })
        {
            double ab = AreasBarras.Para(n);
            double sBruto = ab * BFranjaCm / asDisenoCm2M;          // cm para cubrir exacto
            // Redondear hacia abajo al módulo práctico y capar al máximo normativo.
            double s = Math.Floor(sBruto / ModuloEspaciamientoCm) * ModuloEspaciamientoCm;
            if (s > espMaxCm) s = espMaxCm;
            double asProv = ab * BFranjaCm / s;
            bool cumpleEsp = s >= EspaciamientoMinimoCm && s <= espMaxCm + 1e-9;
            var cand = new ArmaduraSeleccionada(n, s, asProv, cumpleEsp && asProv >= asDisenoCm2M - 1e-6);
            ultima = cand;
            if (s < EspaciamientoMinimoCm) continue;               // barra muy chica → congestión, prueba mayor
            if (asProv >= asDisenoCm2M - 1e-6) return cand;        // primera factible = menos acero
        }
        return ultima!.Value;
    }

    /// <summary>
    /// Diseña una franja completa: As req. (flexión) vs As mín., As de diseño =
    /// el mayor, y la armadura seleccionada.
    /// </summary>
    public static DisenoAceroFranja DisenarFranja(
        string franja, double muTonM, double hCm, double dCm, double fcTonCm2, double fyTonCm2)
    {
        double asReq = AsRequerido(muTonM, dCm, fcTonCm2, fyTonCm2, out bool insuf);
        double asMin = AsMinimoTemperatura(hCm, fyTonCm2);
        double asDiseno = insuf ? double.NaN : Math.Max(asReq, asMin);
        double espMax = EspaciamientoMaximo(hCm);

        if (insuf)
            return new DisenoAceroFranja(franja, muTonM, dCm, asReq, asMin, asDiseno,
                true, 0, double.NaN, double.NaN, false, "SECCIÓN INSUFICIENTE");

        var arm = SeleccionarArmadura(asDiseno, espMax);
        string disponer = string.Create(CultureInfo.InvariantCulture,
            $"#{arm.NumeroBarra} @ {arm.EspaciamientoCm:0.#} cm");
        return new DisenoAceroFranja(franja, muTonM, dCm, asReq, asMin, asDiseno,
            false, arm.NumeroBarra, arm.EspaciamientoCm, arm.AsProvistaCm2M, arm.Cumple, disponer);
    }

    /// <summary>
    /// Aplica un <b>override manual</b> del ingeniero a una franja ya diseñada:
    /// fija la barra y el espaciamiento elegidos y recalcula el acero provisto
    /// (<c>As = ab·100/esp</c>) y si la franja cumple. Conserva los requerimientos
    /// (<c>AsRequerido/AsMinimo/AsDiseno</c>) — solo cambia lo dispuesto. Es la
    /// operación base de "editar el acero" en la pestaña Aceros.
    /// </summary>
    /// <param name="original">Franja diseñada automáticamente (la que se edita).</param>
    /// <param name="numeroBarra">Número de barra elegido (#3–#8).</param>
    /// <param name="espaciamientoCm">Espaciamiento elegido (cm).</param>
    /// <param name="espMaxCm">Espaciamiento máximo normativo para la losa (min(2h,45)).</param>
    public static DisenoAceroFranja AplicarOverride(
        DisenoAceroFranja original, int numeroBarra, double espaciamientoCm, double espMaxCm)
    {
        if (original is null) throw new ArgumentNullException(nameof(original));
        if (espaciamientoCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(espaciamientoCm), "El espaciamiento debe ser positivo.");
        double ab = AreasBarras.Para(numeroBarra);
        if (ab <= 0)
            throw new ArgumentOutOfRangeException(nameof(numeroBarra), "Número de barra fuera de rango (#3–#8).");

        double asProv = ab * BFranjaCm / espaciamientoCm;
        bool cumple = !original.SeccionInsuficiente
            && asProv >= original.AsDisenoCm2M - 1e-6
            && espaciamientoCm >= EspaciamientoMinimoCm
            && espaciamientoCm <= espMaxCm + 1e-9;
        string disponer = string.Create(CultureInfo.InvariantCulture, $"#{numeroBarra} @ {espaciamientoCm:0.#} cm");

        return original with
        {
            NumeroBarra = numeroBarra,
            EspaciamientoCm = espaciamientoCm,
            AsProvistaCm2M = asProv,
            CumpleEspaciamiento = cumple,
            Disponer = disponer,
        };
    }

    /// <summary>
    /// Diseña las 4 franjas de una losa a partir de sus momentos: X y Y en el
    /// centro (positivos, <see cref="MomentoLosa.Mfx"/>/<see cref="MomentoLosa.Mfy"/>)
    /// y X, Y sobre apoyos (negativos, <see cref="MomentoLosa.NMSx"/>/<see cref="MomentoLosa.NMSy"/>).
    /// </summary>
    /// <param name="m">Fila de momentos de la salida del motor.</param>
    /// <param name="fcTonCm2">f'c (ton/cm²).</param>
    /// <param name="fyTonCm2">fy (ton/cm²).</param>
    /// <param name="recubrimientoCm">Recubrimiento libre (cm).</param>
    /// <param name="numeroBarraSupuesto">Barra supuesta para estimar el canto útil (db). Default #4.</param>
    public static DisenoAceroLosa DisenarLosa(
        MomentoLosa m, double fcTonCm2, double fyTonCm2,
        double recubrimientoCm = RecubrimientoDefaultCm, int numeroBarraSupuesto = 4)
    {
        if (m is null) throw new ArgumentNullException(nameof(m));
        double hCm = m.H * 100.0;
        double db = DiametroBarraCm(numeroBarraSupuesto);
        double dInf = CantoUtil(hCm, recubrimientoCm, db, capa: 0);   // dirección inferior (X)
        double dSup = CantoUtil(hCm, recubrimientoCm, db, capa: 1);   // dirección superior (Y)

        return new DisenoAceroLosa(
            m.LosaId,
            DisenarFranja("X centro", m.Mfx, hCm, dInf, fcTonCm2, fyTonCm2),
            DisenarFranja("Y centro", m.Mfy, hCm, dSup, fcTonCm2, fyTonCm2),
            DisenarFranja("X apoyo", m.NMSx, hCm, dInf, fcTonCm2, fyTonCm2),
            DisenarFranja("Y apoyo", m.NMSy, hCm, dSup, fcTonCm2, fyTonCm2));
    }

    /// <summary>Diámetro nominal (cm) de una barra ASTM A615 (#3..#8 = 3/8"..1").</summary>
    public static double DiametroBarraCm(int numero) => numero switch
    {
        3 => 0.953, 4 => 1.270, 5 => 1.588, 6 => 1.905, 7 => 2.223, 8 => 2.540,
        _ => 0.0,
    };
}

/// <summary>Resultado de selección de armadura: barra, espaciamiento (cm), As provista (cm²/m) y si cumple.</summary>
public readonly record struct ArmaduraSeleccionada(
    int NumeroBarra, double EspaciamientoCm, double AsProvistaCm2M, bool Cumple);

/// <summary>
/// Diseño de acero de una franja de losa. Momentos en ton·m/m, áreas en cm²/m,
/// longitudes en cm.
/// </summary>
public sealed record DisenoAceroFranja(
    string Franja,
    double MuTonM,
    double DCm,
    double AsRequeridoCm2M,
    double AsMinimoCm2M,
    double AsDisenoCm2M,
    bool   SeccionInsuficiente,
    int    NumeroBarra,
    double EspaciamientoCm,
    double AsProvistaCm2M,
    bool   CumpleEspaciamiento,
    string Disponer)
{
    /// <summary>True si el mínimo por temperatura gobierna sobre la flexión.</summary>
    public bool GobiernaMinimo => !SeccionInsuficiente && AsMinimoCm2M >= AsRequeridoCm2M;
}

/// <summary>Diseño de acero de una losa completa: 4 franjas (X/Y centro y apoyo).</summary>
public sealed record DisenoAceroLosa(
    int LosaId,
    DisenoAceroFranja XCentro,
    DisenoAceroFranja YCentro,
    DisenoAceroFranja XApoyo,
    DisenoAceroFranja YApoyo);
