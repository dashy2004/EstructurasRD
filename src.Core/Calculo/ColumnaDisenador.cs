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

    /// <summary>Diámetro nominal (mm) de una barra ASTM A615 (#3..#11); 0 si no estándar.</summary>
    public static double DiametroBarraMm(int numero) => numero switch
    {
        3 => 9.53, 4 => 12.70, 5 => 15.88, 6 => 19.05, 7 => 22.23,
        8 => 25.40, 9 => 28.65, 10 => 32.26, 11 => 35.81,
        _ => 0.0,
    };

    /// <summary>Estribo de confinamiento: número de barra y separación (mm).</summary>
    public sealed record Estribo(int Numero, double SeparacionMm);

    /// <summary>
    /// Estribo de columna (ACI 318-19 §25.7.2): tamaño #3 si la barra longitudinal
    /// es ≤ #10, #4 si es ≥ #11; separación = min(16·db_long, 48·db_estribo, menor
    /// dimensión de la columna).
    /// </summary>
    public static Estribo EstriboColumna(ColumnaSeccion s, int numeroBarraLong)
    {
        int nEstribo = numeroBarraLong <= 10 ? 3 : 4;
        double dbLong = DiametroBarraMm(numeroBarraLong);
        double dbTie = DiametroBarraMm(nEstribo);
        double sep = Math.Min(Math.Min(16.0 * dbLong, 48.0 * dbTie), Math.Min(s.B, s.H));
        return new Estribo(nEstribo, sep);
    }

    /// <summary>Resultado del chequeo de demanda P-M de una columna.</summary>
    public sealed record ChequeoColumna(
        double PhiPnMaxN, double PhiMnCapacidadNmm, double Ratio, bool Cumple);

    /// <summary>
    /// Chequea la demanda (Pu, Mu) contra el diagrama de diseño φPn-φMn (con tope
    /// <see cref="PhiPnMax"/>): interpola la capacidad a momento φMn al nivel axial
    /// Pu y devuelve el ratio de demanda |Mu|/φMn. Cumple si Pu ≤ φPn,max y ratio ≤ 1.
    /// </summary>
    public static ChequeoColumna ChequearDemanda(
        ColumnaSeccion s, double puN, double muNmm, int nDiagrama = 60)
    {
        double phiPnMax = PhiPnMax(s);
        var diag = DiagramaInteraccion(s, nDiagrama);

        // Capacidad a momento φMn al nivel axial Pu: interpola la envolvente de
        // diseño (φPn capado a φPn,max) en los segmentos que cruzan φPn = Pu, y
        // toma el φMn mayor (la envolvente externa).
        double phiMnCap = 0.0;
        for (int i = 0; i + 1 < diag.Count; i++)
        {
            double p0 = Math.Min(diag[i].PhiPn, phiPnMax);
            double p1 = Math.Min(diag[i + 1].PhiPn, phiPnMax);
            if ((p0 - puN) * (p1 - puN) <= 0 && Math.Abs(p1 - p0) > 1e-9)
            {
                double t = (puN - p0) / (p1 - p0);
                double m = diag[i].PhiMn + t * (diag[i + 1].PhiMn - diag[i].PhiMn);
                if (m > phiMnCap) phiMnCap = m;
            }
        }

        double ratio = phiMnCap > 0 ? Math.Abs(muNmm) / phiMnCap : double.PositiveInfinity;
        bool cumple = puN <= phiPnMax + 1e-6 && ratio <= 1.0 + 1e-9;
        return new ChequeoColumna(phiPnMax, phiMnCap, ratio, cumple);
    }

    /// <summary>
    /// Coloca barras iguales en el <b>perímetro</b> de una sección b×h (mm): nx por
    /// cara horizontal (arriba y abajo, incluyendo esquinas) y ny por cara vertical
    /// (izquierda y derecha). Las esquinas no se duplican → total = 2·nx + 2·ny − 4.
    /// Centros a <c>recubrimiento + db/2</c> del borde; posiciones respecto al
    /// centro geométrico. <paramref name="numeroBarra"/> fija el diámetro/área.
    /// </summary>
    public static IReadOnlyList<BarraLong> LayoutPerimetral(
        double b, double h, double recubrimiento, int nx, int ny, int numeroBarra)
    {
        if (nx < 2 || ny < 2)
            throw new ArgumentOutOfRangeException(nameof(nx), "se necesitan ≥2 barras por cara.");
        double db = DiametroBarraMm(numeroBarra);
        double area = Math.PI / 4.0 * db * db;
        double inset = recubrimiento + db / 2.0;
        double xExt = b / 2.0 - inset;
        double yExt = h / 2.0 - inset;

        var barras = new List<BarraLong>(2 * nx + 2 * ny - 4);
        // Caras horizontal arriba/abajo (incluyen las 4 esquinas).
        for (int i = 0; i < nx; i++)
        {
            double x = -xExt + 2.0 * xExt * i / (nx - 1);
            barras.Add(new BarraLong(x, +yExt, area));
            barras.Add(new BarraLong(x, -yExt, area));
        }
        // Caras verticales izq/der, sólo interiores (las esquinas ya están).
        for (int j = 1; j < ny - 1; j++)
        {
            double y = -yExt + 2.0 * yExt * j / (ny - 1);
            barras.Add(new BarraLong(-xExt, y, area));
            barras.Add(new BarraLong(+xExt, y, area));
        }
        return barras;
    }

    /// <summary>Resumen de diseño de una columna: cuantía, capacidades, estribo, chequeo y diagrama.</summary>
    public sealed record DisenoColumna(
        double RhoG, bool CumpleCuantia, double PoN, double PhiPnMaxN,
        Estribo Estribo, ChequeoColumna Chequeo, IReadOnlyList<PuntoPM> Diagrama);

    /// <summary>
    /// Diseña una columna de punta a punta: compone cuantía + Po/φPn,max + estribos
    /// + diagrama de interacción + chequeo de la demanda (Pu, Mu) en un solo resumen,
    /// listo para que el VM/UI lo muestre.
    /// </summary>
    public static DisenoColumna DisenarColumna(
        ColumnaSeccion s, int numeroBarraLong, double puN, double muNmm)
        => new DisenoColumna(
            s.RhoG,
            CumpleCuantia(s),
            Po(s),
            PhiPnMax(s),
            EstriboColumna(s, numeroBarraLong),
            ChequearDemanda(s, puN, muNmm),
            DiagramaInteraccion(s));

    // ===================================================================
    // ESBELTEZ (ACI 318-19 §6.2.5 / §6.6.4)
    // ===================================================================

    /// <summary>
    /// Radio de giro r (mm) de una sección rectangular: ACI 318-19 §6.2.5.1
    /// permite r = 0.30·(dimensión total en la dirección de pandeo considerada).
    /// </summary>
    public static double RadioGiroRectangular(double dimensionMm) => 0.30 * dimensionMm;

    /// <summary>
    /// Relación de esbeltez k·Lu/r (adimensional): factor de longitud efectiva
    /// <paramref name="k"/>, longitud no arriostrada <paramref name="luMm"/> (mm) y
    /// radio de giro <paramref name="rMm"/> (mm). Radio nulo o negativo → infinito.
    /// </summary>
    public static double RelacionEsbeltez(double k, double luMm, double rMm)
        => rMm > 0 ? k * luMm / rMm : double.PositiveInfinity;

    /// <summary>
    /// Límite de esbeltez para <b>despreciar</b> los efectos de esbeltez en pórticos
    /// arriostrados / sin desplazamiento lateral (ACI 318-19 §6.2.5):
    /// <c>34 − 12·(M1/M2)</c>, sin exceder 40. <paramref name="m1"/> es el menor
    /// momento de extremo y <paramref name="m2"/> el mayor; <c>M1/M2</c> es negativo
    /// en curvatura simple y positivo en doble curvatura.
    /// </summary>
    public static double LimiteEsbeltezArriostrado(double m1, double m2)
    {
        double ratio = m2 != 0.0 ? m1 / m2 : 0.0;
        return Math.Min(34.0 - 12.0 * ratio, 40.0);
    }

    /// <summary>
    /// ¿La columna (arriostrada) es esbelta? Verdadero si <c>k·Lu/r</c> supera el
    /// <see cref="LimiteEsbeltezArriostrado"/>: en ese caso no pueden despreciarse
    /// los efectos de esbeltez (ACI 318-19 §6.2.5). <paramref name="dimensionMm"/>
    /// es la dimensión de la sección en la dirección de pandeo considerada.
    /// </summary>
    public static bool EsEsbeltaArriostrada(double k, double luMm, double dimensionMm, double m1, double m2)
        => RelacionEsbeltez(k, luMm, RadioGiroRectangular(dimensionMm)) > LimiteEsbeltezArriostrado(m1, m2);

    // ===================================================================
    // MAGNIFICACIÓN DE MOMENTO δ (ACI 318-19 §6.6.4)
    // ===================================================================

    /// <summary>Módulo de elasticidad del hormigón Ec = 4700·√(f'c) MPa (ACI 318-19 §19.2.2.1).</summary>
    public static double ModuloElasticidadConcreto(double fcMPa) => 4700.0 * Math.Sqrt(fcMPa);

    /// <summary>Inercia bruta de una sección rectangular Ig = b·h³/12 (mm⁴).</summary>
    public static double InerciaBrutaRectangular(double bMm, double hMm) => bMm * hMm * hMm * hMm / 12.0;

    /// <summary>
    /// Rigidez a flexión EI (N·mm²) para el cálculo de esbeltez, ACI 318-19
    /// §6.6.4.4.4 en su forma simplificada: <c>EI = 0.4·Ec·Ig</c> (asume βdns = 0,
    /// es decir, sin fluencia por carga sostenida — aproximación conservadora del
    /// lado de la rigidez para combos de corta duración).
    /// </summary>
    public static double RigidezEI(double fcMPa, double bMm, double hMm)
        => 0.4 * ModuloElasticidadConcreto(fcMPa) * InerciaBrutaRectangular(bMm, hMm);

    /// <summary>
    /// Carga crítica de pandeo de Euler Pc = π²·EI/(k·Lu)² (N), ACI 318-19
    /// §6.6.4.4.2. <paramref name="eiNmm2"/> en N·mm², longitudes en mm. Longitud
    /// efectiva nula o negativa → infinito.
    /// </summary>
    public static double CargaCriticaPandeo(double eiNmm2, double k, double luMm)
    {
        double kLu = k * luMm;
        return kLu > 0 ? Math.PI * Math.PI * eiNmm2 / (kLu * kLu) : double.PositiveInfinity;
    }

    /// <summary>
    /// Factor de magnificación de momento δ = Cm/(1 − Pu/(0.75·Pc)) ≥ 1.0
    /// (ACI 318-19 §6.6.4.5.2). Si <c>Pu ≥ 0.75·Pc</c> la columna es inestable y
    /// debe rediseñarse: se devuelve <see cref="double.PositiveInfinity"/>.
    /// </summary>
    public static double FactorMagnificacion(double cm, double puN, double pcN)
    {
        double denom = 1.0 - puN / (0.75 * pcN);
        if (denom <= 0.0) return double.PositiveInfinity;
        return Math.Max(1.0, cm / denom);
    }
}
