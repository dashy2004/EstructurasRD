using System;
using System.Collections.Generic;
using System.Linq;

namespace LosasPlus.Calculo;

/// <summary>
/// Una barra longitudinal en la sección, posicionada respecto al <b>centroide
/// geométrico</b> de la sección bruta (mm) con su área (mm²).
/// </summary>
public sealed record BarraLong(double X, double Y, double AreaMm2);

/// <summary>
/// Sección rectangular de columna de hormigón armado en unidades SI (mm, MPa).
/// <paramref name="B"/> = ancho (dirección X), <paramref name="H"/> = peralte
/// (dirección Y, la de flexión por defecto).
/// </summary>
public sealed record ColumnaSeccion(
    double B, double H, double FcMPa, double FyMPa, IReadOnlyList<BarraLong> Barras)
{
    /// <summary>Área bruta Ag = B·H (mm²).</summary>
    public double Ag => B * H;

    /// <summary>Área total de acero longitudinal Ast = Σ áreas de barra (mm²).</summary>
    public double Ast => Barras.Sum(b => b.AreaMm2);

    /// <summary>Cuantía geométrica ρg = Ast / Ag.</summary>
    public double RhoG => Ag > 0 ? Ast / Ag : 0.0;
}

/// <summary>
/// Diseño de columnas de hormigón a flexo-compresión uniaxial (ACI 318-19), en
/// unidades SI (N, mm, MPa) — espeja y cruza-valida el motor FEA
/// (<c>motor_fea/normativa/aci318.py</c>). Funciones puras, testeables headless.
/// </summary>
public static class ColumnaDisenador
{
    /// <summary>Deformación unitaria última del hormigón (ACI 318-19 §22.2.2.1).</summary>
    public const double EpsilonCU = 0.003;

    /// <summary>Módulo de elasticidad del acero, MPa (ACI 318-19 §20.2.2.2).</summary>
    public const double Es = 200_000.0;

    /// <summary>Cuantía mínima de acero longitudinal en columnas (ACI 318-19 §10.6.1.1).</summary>
    public const double RhoMin = 0.01;

    /// <summary>Cuantía máxima de acero longitudinal en columnas (ACI 318-19 §10.6.1.1).</summary>
    public const double RhoMax = 0.08;

    /// <summary>
    /// Factor β1 del bloque rectangular equivalente (ACI 318-19 §22.2.2.4.3):
    /// 0.85 para f'c ≤ 28 MPa, baja 0.05 por cada 7 MPa por encima, con piso 0.65.
    /// </summary>
    public static double Beta1(double fcMPa)
        => Math.Clamp(0.85 - 0.05 * (fcMPa - 28.0) / 7.0, 0.65, 0.85);

    /// <summary>
    /// True si la cuantía geométrica está dentro de [<see cref="RhoMin"/>,
    /// <see cref="RhoMax"/>] (ACI 318-19 §10.6.1.1).
    /// </summary>
    public static bool CumpleCuantia(ColumnaSeccion s)
        => s.RhoG >= RhoMin && s.RhoG <= RhoMax;

    /// <summary>Factor de reducción φ para columnas con estribos (compresión), ACI 318-19 Tabla 21.2.2.</summary>
    public const double PhiTied = 0.65;

    /// <summary>Tope de carga axial para columnas con estribos (ACI 318-19 §22.4.2.1).</summary>
    public const double FactorPnMaxTied = 0.80;

    /// <summary>
    /// Centroide plástico de la sección (mm, respecto al centroide geométrico):
    /// el punto donde la carga axial pura no produce momento. Para refuerzo
    /// simétrico coincide con el centro. Pondera la capacidad de aplastamiento del
    /// hormigón (0.85·f'c·Ag, en el centro geométrico) con el acero neto
    /// (fy − 0.85·f'c)·As de cada barra, en su posición.
    /// </summary>
    public static (double X, double Y) CentroidePlastico(ColumnaSeccion s)
    {
        double w = 0.85 * s.FcMPa;                       // aplastamiento del hormigón por mm²
        double den = w * s.Ag;                           // fuerza del hormigón, en el centro (0,0)
        double numX = 0.0, numY = 0.0;
        foreach (var b in s.Barras)
        {
            double fNet = (s.FyMPa - w) * b.AreaMm2;      // acero neto (descuenta hormigón desplazado)
            numX += fNet * b.X;
            numY += fNet * b.Y;
            den += fNet;
        }
        return den != 0.0 ? (numX / den, numY / den) : (0.0, 0.0);
    }

    /// <summary>
    /// Carga axial nominal máxima Po (N) — "squash load" (ACI 318-19 §22.4.2.2):
    /// Po = 0.85·f'c·(Ag − Ast) + fy·Ast.
    /// </summary>
    public static double Po(ColumnaSeccion s)
        => 0.85 * s.FcMPa * (s.Ag - s.Ast) + s.FyMPa * s.Ast;

    /// <summary>
    /// Carga axial de diseño máxima φPn,max (N) para columna con estribos
    /// (ACI 318-19 §22.4.2.1 + Tabla 21.2.2): φ·0.80·Po, con φ = 0.65.
    /// </summary>
    public static double PhiPnMax(ColumnaSeccion s)
        => PhiTied * FactorPnMaxTied * Po(s);

    /// <summary>Un punto (Pn, Mn) del diagrama de interacción P-M, con su φ por εt. Compresión positiva.</summary>
    public sealed record PuntoPM(double C, double Pn, double Mn, double Et, double Phi)
    {
        /// <summary>Axial de diseño φPn (N).</summary>
        public double PhiPn => Phi * Pn;
        /// <summary>Momento de diseño φMn (N·mm).</summary>
        public double PhiMn => Phi * Mn;
    }

    /// <summary>
    /// φ por deformación neta de tracción εt (ACI 318-19 Tabla 21.2.2), miembro con
    /// estribos: 0.65 (compresión, εt ≤ εy) → 0.90 (tracción, εt ≥ εy+0.003), lineal
    /// en la transición. Para zunchos el piso es 0.75.
    /// </summary>
    public static double PhiPorDeformacion(double et, double fy, bool estribos = true)
    {
        double phiMin = estribos ? 0.65 : 0.75;
        double ey = fy / Es;
        if (et <= ey) return phiMin;
        if (et >= ey + 0.003) return 0.90;
        return phiMin + (0.90 - phiMin) * (et - ey) / 0.003;
    }

    /// <summary>Profundidad del eje neutro balanceado c_b = εcu/(εcu+εy)·d (mm).</summary>
    public static double ProfundidadBalanceada(double d, double fy)
        => EpsilonCU / (EpsilonCU + fy / Es) * d;

    /// <summary>
    /// Punto del diagrama P-M para un eje neutro <paramref name="c"/> (mm), por
    /// compatibilidad de deformaciones (εcu=0.003, bloque de Whitney a=β1·c, fs
    /// capado a ±fy, descuento del hormigón desplazado por barras en compresión).
    /// Momentos respecto al centro geométrico. Compresión positiva. Espeja
    /// <c>aci318.punto_interaccion</c> del motor FEA.
    /// </summary>
    public static PuntoPM PuntoInteraccion(ColumnaSeccion s, double c)
    {
        if (c <= 0) throw new ArgumentOutOfRangeException(nameof(c), "c debe ser positivo.");
        double a = Math.Min(Beta1(s.FcMPa) * c, s.H);
        double cc = 0.85 * s.FcMPa * s.B * a;        // resultante del bloque de hormigón (N)
        double pn = cc;
        double mn = cc * (s.H / 2.0 - a / 2.0);      // momento del hormigón sobre el centro

        double dMax = double.NegativeInfinity;
        foreach (var bar in s.Barras)
        {
            double d = s.H / 2.0 - bar.Y;            // profundidad desde la fibra comprimida (arriba)
            if (d > dMax) dMax = d;
        }
        foreach (var bar in s.Barras)
        {
            double d = s.H / 2.0 - bar.Y;
            double eps = EpsilonCU * (c - d) / c;    // + compresión, − tracción
            double fs = Math.Max(-s.FyMPa, Math.Min(s.FyMPa, Es * eps));
            double fuerza = bar.AreaMm2 * fs;
            if (d <= a) fuerza -= bar.AreaMm2 * 0.85 * s.FcMPa;   // descontar hormigón desplazado
            pn += fuerza;
            mn += fuerza * (s.H / 2.0 - d);
        }
        double et = EpsilonCU * (dMax - c) / c;      // tracción positiva en la capa extrema
        return new PuntoPM(c, pn, mn, et, PhiPorDeformacion(et, s.FyMPa));
    }

    /// <summary>
    /// Diagrama de interacción P-M: barre el eje neutro c (de 0.05·H a 2·H) y
    /// devuelve <paramref name="n"/> puntos nominales (Pn, Mn, φ) ordenados de la
    /// región de tracción/flexión a la de alta compresión. El tope de diseño en
    /// compresión es <see cref="PhiPnMax"/> (se aplica al envolver/chequear).
    /// </summary>
    public static IReadOnlyList<PuntoPM> DiagramaInteraccion(ColumnaSeccion s, int n = 40)
    {
        if (n < 2) throw new ArgumentOutOfRangeException(nameof(n), "se necesitan ≥2 puntos.");
        double cMin = 0.05 * s.H, cMax = 2.0 * s.H;
        var pts = new List<PuntoPM>(n);
        for (int i = 0; i < n; i++)
            pts.Add(PuntoInteraccion(s, cMin + (cMax - cMin) * i / (n - 1)));
        return pts;
    }
}
