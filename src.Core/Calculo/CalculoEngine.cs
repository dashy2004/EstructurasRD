using System;
using LosasPlus.Models;

namespace LosasPlus.Calculo;

/// <summary>
/// Motor de cálculo de Memoria Plus. Funciones puras estáticas que aplican
/// las fórmulas extraídas de la hoja <c>Cargas</c> y <c>Espesor *</c> del libro
/// <c>cargas_estructurales_demo.xlsx</c> (ver <c>docs/referencia/README.md</c>
/// para la equivalencia formulas ↔ ACI 318).
///
/// <para>
/// Convención: cada función es <b>pura</b> (no toca propiedades del modelo);
/// <see cref="RecalcularLosa"/>, <see cref="RecalcularSistema"/> y
/// <see cref="RecalcularProyecto"/> son los orquestadores que aplican el
/// resultado al modelo (rellenan <see cref="Losa.HCalc"/>, <see cref="Losa.Qu"/>,
/// etc.).
/// </para>
///
/// <para>
/// Unidades:
/// </para>
/// <list type="bullet">
///   <item>Luces (Lx, Ly, Ln) y espesores: metros (m).</item>
///   <item>Cargas: ton/m² (tonelada-fuerza por metro cuadrado).</item>
///   <item>Fy: kg/cm² (4200 kg/cm² ≈ 420 MPa = grado 60).</item>
/// </list>
/// </summary>
public static class CalculoEngine
{
    // =====================================================================
    // ESPESOR (h_calc)
    // =====================================================================

    /// <summary>
    /// Espesor mínimo para una losa <b>1D</b>: <c>h = Ln / K</c>.
    /// El factor K depende de las condiciones de borde:
    /// <list type="bullet">
    ///   <item>K = 20 simplemente apoyada</item>
    ///   <item>K = 24 un extremo continuo</item>
    ///   <item>K = 28 ambos extremos continuos (default)</item>
    ///   <item>K = 10 voladizo</item>
    /// </list>
    /// Referencia: ACI 318-08 / ACI 318-19 Tabla 9.5(a) — losas en una dirección.
    /// </summary>
    public static double ComputeHCalc1D(double ln, int k)
    {
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), "K debe ser > 0");
        return ln / k;
    }

    /// <summary>
    /// Espesor mínimo para una losa <b>2D</b>:
    /// <c>h = Ln · (0.8 + Fy/14000) / (36 + 9·ratio)</c>.
    /// Referencia: ACI 318 9.5.3.2. Fy en <b>kg/cm²</b> (no MPa) y ratio = max(Lx,Ly)/min(Lx,Ly).
    /// </summary>
    public static double ComputeHCalc2D(double ln, double fyKgCm2, double ratio)
    {
        if (ratio <= 0) throw new ArgumentOutOfRangeException(nameof(ratio), "ratio debe ser > 0");
        return ln * (0.8 + fyKgCm2 / 14000.0) / (36.0 + 9.0 * ratio);
    }

    /// <summary>Despacha entre <see cref="ComputeHCalc1D"/> y <see cref="ComputeHCalc2D"/> según <see cref="Losa.Cond"/>.</summary>
    public static double ComputeHCalc(Losa losa, double fyKgCm2)
    {
        if (losa is null) throw new ArgumentNullException(nameof(losa));
        return losa.Cond == "1D"
            ? ComputeHCalc1D(losa.Ln, losa.K)
            : ComputeHCalc2D(losa.Ln, fyKgCm2, losa.Ratio);
    }

    /// <summary>
    /// Espesor a usar: <c>MAX(0.12 m, ROUND(h_calc, 2))</c>. El piso de 0.12 m
    /// refleja la práctica del ingeniero (espesor mínimo razonable para losa
    /// maciza residencial dominicana).
    /// </summary>
    public static double ComputeHUsar(double hCalc)
        => Math.Max(0.12, Math.Round(hCalc, 2));

    // =====================================================================
    // ESPESOR EQUIVALENTE (h_eq) — ACI 318 §9.5.3.3 / vigueta+bloque
    // =====================================================================

    /// <summary>
    /// Ancho típico de panel (separación entre centros de viguetas) en losas
    /// vigueta+bloque dominicanas: <c>0.50 m</c> (bloque hueco de 0.40 m +
    /// nervio de 0.10 m). Usado como default cuando
    /// <see cref="Losa"/> no expone un campo explícito de ancho de panel.
    /// </summary>
    public const double BPanelDefault = 0.50;

    /// <summary>
    /// Espesor de la capeta (capa superior maciza) por defecto en losas
    /// vigueta+bloque: <c>0.05 m</c>. Si el espesor total
    /// <c>hUsar</c> entregado a <see cref="ComputeHEqViguetaBloque"/> no excede
    /// <see cref="HBloque"/>, se asume este valor para mantener positivos los
    /// términos de la T-section.
    /// </summary>
    public const double HCapetaDefault = 0.05;

    /// <summary>
    /// Espesor equivalente para una losa.
    /// <list type="bullet">
    ///   <item>Losa <b>maciza</b> (sin <see cref="Losa.Bw"/> o
    ///         <see cref="Losa.HBloque"/>): <c>h_eq = h_usar</c>.</item>
    ///   <item>Losa <b>vigueta+bloque</b>: aplica
    ///         <see cref="ComputeHEqViguetaBloque(double, double, double, double)"/>
    ///         con <c>B = </c><see cref="BPanelDefault"/> y
    ///         <c>h_capeta = hUsar − HBloque</c> (con piso
    ///         <see cref="HCapetaDefault"/>).</item>
    /// </list>
    /// <para>
    /// El parámetro <paramref name="fyKgCm2"/> se mantiene en la firma por
    /// compatibilidad — el modelo paramétrico de I-equivalente no depende del
    /// acero. Reservado para futuras correcciones αfm de borde (ACI 9.5.3.3)
    /// donde la rigidez de las vigas perimetrales modifica el espesor mínimo.
    /// </para>
    /// </summary>
    public static double ComputeHEq(Losa losa, double hUsar, double fyKgCm2)
    {
        if (!losa.Bw.HasValue || !losa.HBloque.HasValue) return hUsar;
        var bw = losa.Bw.Value;
        var hBloque = losa.HBloque.Value;
        if (bw <= 0 || hBloque <= 0) return hUsar;

        var hCapeta = Math.Max(HCapetaDefault, hUsar - hBloque);
        return ComputeHEqViguetaBloque(bw, hBloque, hCapeta, BPanelDefault);
    }

    /// <summary>
    /// Convierte la sección transversal de una losa <b>vigueta+bloque</b> en el
    /// espesor de una losa maciza equivalente que tiene el mismo momento de
    /// inercia por unidad de ancho de panel.
    ///
    /// <para>
    /// Modelo geométrico (T-section por panel de ancho <paramref name="bPanel"/>):
    /// </para>
    /// <code>
    ///       ←———— B ————→
    ///      ┌──────────────┐  ↑ h_capeta (capeta superior maciza)
    ///      ├──┐        ┌──┤  ↓
    ///         │        │
    ///         │ Bw     │      ↑ h_bloque (altura del bloque hueco
    ///         │        │      │           = altura del nervio bajo capeta)
    ///         └────────┘      ↓
    /// </code>
    ///
    /// <para>
    /// Procedimiento:
    /// </para>
    /// <list type="number">
    ///   <item>Áreas:
    ///       <c>A_cap = B·h_cap</c>,
    ///       <c>A_ner = Bw·h_blo</c>.</item>
    ///   <item>Centroide desde la fibra inferior:
    ///       <c>y_bar = (A_cap·(h_blo + h_cap/2) + A_ner·(h_blo/2)) / (A_cap + A_ner)</c>.</item>
    ///   <item>Momento de inercia de cada componente respecto al centroide
    ///       (Steiner):
    ///       <c>I_cap = B·h_cap³/12 + A_cap·(y_cap − y_bar)²</c>,
    ///       <c>I_ner = Bw·h_blo³/12 + A_ner·(y_ner − y_bar)²</c>.</item>
    ///   <item><c>I_total = I_cap + I_ner</c>.</item>
    ///   <item><c>h_eq = ∛(12·I_total / B)</c>.</item>
    /// </list>
    ///
    /// <para>
    /// Referencia: ACI 318-05 §9.5.3.3 / 13.6.1.6 (ribbed slabs). Excel
    /// equivalente: fórmulas N9..R9 de <c>cargas_estructurales_demo.xlsx</c>
    /// (hojas <c>Espesor *</c>).
    /// </para>
    /// </summary>
    public static double ComputeHEqViguetaBloque(double bw, double hBloque, double hCapeta, double bPanel)
    {
        if (bw     <= 0) throw new ArgumentOutOfRangeException(nameof(bw),     "bw debe ser > 0");
        if (hBloque<= 0) throw new ArgumentOutOfRangeException(nameof(hBloque), "hBloque debe ser > 0");
        if (hCapeta<= 0) throw new ArgumentOutOfRangeException(nameof(hCapeta), "hCapeta debe ser > 0");
        if (bPanel <= 0) throw new ArgumentOutOfRangeException(nameof(bPanel),  "bPanel debe ser > 0");
        if (bw >= bPanel)
            throw new ArgumentOutOfRangeException(nameof(bw),
                $"bw ({bw}) debe ser < bPanel ({bPanel}); el nervio no puede ocupar todo el ancho.");

        var aCapeta = bPanel * hCapeta;
        var aNervio = bw     * hBloque;
        var aTotal  = aCapeta + aNervio;

        var yCapeta = hBloque + hCapeta / 2.0;
        var yNervio = hBloque / 2.0;
        var yBar    = (aCapeta * yCapeta + aNervio * yNervio) / aTotal;

        var iCapeta = bPanel * Math.Pow(hCapeta, 3) / 12.0 + aCapeta * Math.Pow(yCapeta - yBar, 2);
        var iNervio = bw     * Math.Pow(hBloque, 3) / 12.0 + aNervio * Math.Pow(yNervio - yBar, 2);
        var iTotal  = iCapeta + iNervio;

        return Math.Pow(12.0 * iTotal / bPanel, 1.0 / 3.0);
    }

    // =====================================================================
    // CARGA DE MAMPOSTERÍA (Qmamp, Qmap)
    // =====================================================================

    /// <summary>
    /// Peso total de la mampostería sobre la losa (ton):
    /// <c>Qmamp = 1.8 · (h_piso − h_losa) · (0.2·N + 0.15·O + 0.1·P)</c>
    /// donde:
    /// <list type="bullet">
    ///   <item>1.8 ton/m³ es la densidad típica de bloque de hormigón hueco con repello.</item>
    ///   <item>(h_piso − h_losa) es la altura libre de mampostería desde la losa al techo.</item>
    ///   <item>N, O, P son los metros lineales de mampostería de espesor 0.20, 0.15 y 0.10 m respectivamente.</item>
    /// </list>
    /// Si <c>h_piso ≤ h_losa</c> (caso degenerado o losa de techo) devuelve 0.
    /// </summary>
    public static double ComputeQmamp(double hPiso, double hLosa, double mampN, double mampO, double mampP)
    {
        var diff = hPiso - hLosa;
        if (diff <= 0) return 0;
        return 1.8 * diff * (0.2 * mampN + 0.15 * mampO + 0.1 * mampP);
    }

    /// <summary>
    /// Carga distribuida equivalente de mampostería (ton/m²):
    /// <c>Qmap = MAX(0.10, Qmamp / Area)</c>, con piso de 0.10 ton/m² (~ ACI mínimo
    /// recomendado para particiones móviles). Si <c>Qmamp = 0</c>, devuelve 0
    /// directamente (sin clamp).
    /// </summary>
    public static double ComputeQmap(double qmamp, double area)
    {
        if (qmamp <= 0) return 0;
        if (area <= 0) return 0;
        return Math.Max(0.10, qmamp / area);
    }

    // =====================================================================
    // CARGA MUERTA Y CARGA ÚLTIMA
    // =====================================================================

    /// <summary>
    /// Carga muerta total (ton/m²) = lookup en
    /// <see cref="CargasGlobales.CargaMuertaPorEspesor"/> + Qmap.
    /// El lookup retorna h·d_hormigón + pesos propios del uso del nivel.
    /// </summary>
    public static double ComputeQd(double hEq, CargasGlobales cargas, SistemaUso uso, double qmap)
    {
        if (cargas is null) throw new ArgumentNullException(nameof(cargas));
        var pesosPropios = uso == SistemaUso.Techo
            ? cargas.PesosPropiosTecho.Total
            : cargas.PesosPropiosEntrepiso.Total;
        return cargas.CargaMuertaPorEspesor.LookupQd(hEq, pesosPropios) + qmap;
    }

    /// <summary>Carga viva (ton/m²) según el uso del nivel.</summary>
    public static double ComputeQl(SistemaUso uso, CargasGlobales cargas)
        => cargas.CargasVivas.Para(uso);

    /// <summary>
    /// Carga última (ton/m²) = <see cref="FactoresCombinacion.Combinar"/>(qd, ql).
    /// Por defecto: <c>qu = 1.2·qd + 1.6·ql</c> (ACI 318-05).
    /// </summary>
    public static double ComputeQu(double qd, double ql, FactoresCombinacion factores)
        => factores.Combinar(qd, ql);

    // =====================================================================
    // ESPESOR EQUIVALENTE — αfm (ACI 9.5.3.3)
    // Ref: ESPESOR EQUIVALENTE.xlsx, columnas T..AA.
    // =====================================================================

    /// <summary>
    /// Momento de inercia de una sección rectangular: <c>b·h³/12</c>.
    /// Las unidades se preservan: si <paramref name="b"/> y <paramref name="h"/>
    /// están en cm, el resultado está en cm⁴.
    /// </summary>
    public static double ComputeInerciaRectangular(double b, double h)
        => b * Math.Pow(h, 3) / 12.0;

    /// <summary>
    /// Inercia de la franja de losa equivalente para el cálculo de α en una dirección.
    /// Sigue el modelo del Excel ESPESOR EQUIVALENTE:
    /// <c>I_losa = (L_perp · 100 / 2) · J³·10⁶ / 12</c>, donde
    /// <list type="bullet">
    ///   <item><c>L_perp</c> es la luz perpendicular a la dirección de la viga (m).</item>
    ///   <item><c>J</c> es el espesor a usar de la losa (m).</item>
    /// </list>
    /// Equivale a tomar como ancho de cálculo medio ancho de la franja tributaria
    /// (<c>L_perp/2</c>) en cm, y como peralte <c>J·100</c> cm.
    /// </summary>
    public static double ComputeInerciaLosa(double lPerp, double j)
    {
        if (lPerp <= 0) throw new ArgumentOutOfRangeException(nameof(lPerp), "L perpendicular debe ser > 0");
        if (j     <= 0) throw new ArgumentOutOfRangeException(nameof(j),     "Espesor J debe ser > 0");
        var anchoCm  = lPerp * 100.0 / 2.0;
        var peralteCm3 = Math.Pow(j * 100.0, 3);
        return anchoCm * peralteCm3 / 12.0;
    }

    /// <summary>
    /// Calcula α en una dirección: <c>α = I_viga / I_losa</c>.
    /// </summary>
    public static double ComputeAlpha(double iViga, double iLosa)
    {
        if (iLosa <= 0) throw new ArgumentOutOfRangeException(nameof(iLosa), "I_losa debe ser > 0");
        return iViga / iLosa;
    }

    /// <summary>
    /// Resultado de la verificación αfm para una losa:
    /// <list type="bullet">
    ///   <item><c>AlphaX</c> en la dirección de Lx.</item>
    ///   <item><c>AlphaY</c> en la dirección de Ly.</item>
    ///   <item><c>AlphaM</c> = (αx + αy) / 2.</item>
    ///   <item><c>Estado</c>: <c>"OK"</c> si αm &gt; 2 (ACI 9.5.3.3 — losa con vigas rígidas),
    ///         sino <c>"CHK"</c> (revisar espesor).</item>
    /// </list>
    /// </summary>
    public readonly record struct AlphaFmResult(double AlphaX, double AlphaY, double AlphaM, string Estado);

    /// <summary>
    /// Calcula α en X, α en Y, αm y el estado OK/CHK para una losa.
    /// <list type="bullet">
    ///   <item>Iviga = b·h³/12 con (b, h) de la viga tipo (cm).</item>
    ///   <item>Ilosa_x = (Ly·100/2) · J³·10⁶ / 12  (franja perpendicular a la viga X).</item>
    ///   <item>Ilosa_y = (Lx·100/2) · J³·10⁶ / 12  (franja perpendicular a la viga Y).</item>
    ///   <item>αx = Iviga / Ilosa_x; αy = Iviga / Ilosa_y.</item>
    ///   <item>αm = (αx + αy) / 2. Status OK si αm &gt; 2.</item>
    /// </list>
    /// <para>
    /// El parámetro <paramref name="j"/> es el espesor a usar (m): normalmente
    /// <see cref="Losa.Espesor"/> tras pasar por <see cref="ComputeHUsar"/>.
    /// </para>
    /// </summary>
    public static AlphaFmResult ComputeAlphaFm(Losa losa, VigaTipo viga, double j)
    {
        if (losa is null) throw new ArgumentNullException(nameof(losa));
        if (viga is null) throw new ArgumentNullException(nameof(viga));
        if (j <= 0) throw new ArgumentOutOfRangeException(nameof(j), "Espesor J debe ser > 0");

        var iViga  = ComputeInerciaRectangular(viga.BaseCm, viga.AlturaCm);
        var iLosaX = ComputeInerciaLosa(losa.Ly, j);
        var iLosaY = ComputeInerciaLosa(losa.Lx, j);
        var ax = ComputeAlpha(iViga, iLosaX);
        var ay = ComputeAlpha(iViga, iLosaY);
        var am = (ax + ay) / 2.0;
        var estado = am > 2.0 ? "OK" : "CHK";
        return new AlphaFmResult(ax, ay, am, estado);
    }

    // =====================================================================
    // ESPESOR EQUIVALENTE — cantidades de bovedilla y volúmenes
    // =====================================================================

    /// <summary>
    /// Resultado del cómputo métrico de bovedillas / volúmenes para una losa.
    /// </summary>
    public readonly record struct VolumenesResult(int CantBovedillasX, int CantBovedillasY, int Total,
                                                  double VBovedilla, double VTotal, double VConcreto);

    /// <summary>
    /// Calcula la cantidad de bovedillas en X y Y, su volumen agregado, el volumen
    /// total de la losa y el volumen de concreto, siguiendo el modelo del Excel
    /// ESPESOR EQUIVALENTE (columnas M..R).
    ///
    /// <para>
    /// El número de bovedillas por dirección se calcula como
    /// <c>floor( luz / (S + B) )</c>, donde <c>S</c> es el ancho del nervio y
    /// <c>B</c> el ancho de la bovedilla — i.e. cuántos módulos S+B caben en la
    /// luz. Para losas 1D solo se cuentan bovedillas en la dirección de los
    /// nervios; en la perpendicular se reporta 1.
    /// </para>
    /// </summary>
    public static VolumenesResult ComputeVolumenes(Losa losa, Bovedilla bov, double hUsar)
    {
        if (losa is null) throw new ArgumentNullException(nameof(losa));
        if (bov  is null) throw new ArgumentNullException(nameof(bov));
        if (hUsar <= 0) throw new ArgumentOutOfRangeException(nameof(hUsar), "h_usar debe ser > 0");

        var modulo = bov.S + bov.B;
        int m, n;
        if (losa.Cond == "1D")
        {
            // En 1D los nervios corren paralelos a la luz mayor; las bovedillas se
            // cuentan a lo largo de la luz menor (la franja con vigueta+bloque).
            var luzNervios = Math.Min(losa.Lx, losa.Ly);
            var luzPerp    = Math.Max(losa.Lx, losa.Ly);
            m = (int)Math.Floor(luzNervios / modulo);
            // Cantidad de bovedillas por nervio según largo (L) de la bovedilla.
            n = bov.L > 0 ? (int)Math.Floor(luzPerp / bov.L) : 1;
        }
        else
        {
            // En 2D los nervios forman retícula en ambas direcciones.
            m = (int)Math.Floor(losa.Lx / modulo);
            n = (int)Math.Floor(losa.Ly / modulo);
        }
        int total = m * n;
        double vBov  = total * bov.VolumenIndividual;
        double vTot  = hUsar * losa.Lx * losa.Ly;
        double vCon  = Math.Max(0.0, vTot - vBov);
        return new VolumenesResult(m, n, total, vBov, vTot, vCon);
    }

    // =====================================================================
    // ACERO POR BARRAS — As total a partir de un conteo por diámetro
    // =====================================================================

    /// <summary>
    /// Área total de acero (cm²) de un refuerzo distribuido por diámetro:
    /// <c>As = 0.71·n3 + 1.27·n4 + 1.99·n5 + 2.85·n6 + 3.88·n7 + 5.07·n8</c>.
    /// Áreas nominales según ASTM A615 (ver <see cref="AreasBarras"/>).
    /// </summary>
    public static double ComputeAsTotal(RefuerzoBarras r)
    {
        if (r is null) throw new ArgumentNullException(nameof(r));
        return r.N3 * AreasBarras.A3
             + r.N4 * AreasBarras.A4
             + r.N5 * AreasBarras.A5
             + r.N6 * AreasBarras.A6
             + r.N7 * AreasBarras.A7
             + r.N8 * AreasBarras.A8;
    }

    // =====================================================================
    // EMPALMES / BARRAS ADICIONALES — separación
    // =====================================================================

    /// <summary>
    /// Calcula el acero adicional faltante en un empalme de losa:
    /// <c>As_adic = As_req − As_a/2 − As_b/2</c>.
    /// Si las barras de los paños empalmados ya cubren <c>As_req</c>, devuelve 0
    /// (no hace falta refuerzo adicional).
    /// </summary>
    public static double ComputeAsAdicional(double asReq, double asEmpalmeA, double asEmpalmeB)
    {
        var faltante = asReq - asEmpalmeA / 2.0 - asEmpalmeB / 2.0;
        return Math.Max(0.0, faltante);
    }

    /// <summary>
    /// Separación máxima de barras adicionales (m) para cubrir el área faltante
    /// en un empalme: <c>s = área_barra / As_adic</c>, donde <c>área_barra</c>
    /// es el área nominal de la barra elegida (cm²) y <c>As_adic</c> el área
    /// adicional requerida (cm²/m). El resultado se entrega en metros porque
    /// internamente As_adic se expresa por metro de franja.
    /// </summary>
    /// <param name="asAdicional">Área de acero adicional requerida (cm²).</param>
    /// <param name="numeroBarra">Número de barra (3..8).</param>
    public static double ComputeSeparacionBarrasAdicionales(double asAdicional, int numeroBarra)
    {
        if (asAdicional <= 0) return double.PositiveInfinity;  // no hace falta refuerzo adicional
        var areaBarra = AreasBarras.Para(numeroBarra);
        if (areaBarra <= 0) throw new ArgumentOutOfRangeException(nameof(numeroBarra),
            $"Número de barra {numeroBarra} no soportado (válidos: 3..8).");
        return areaBarra / asAdicional;
    }

    // =====================================================================
    // PIPELINE ORQUESTADOR
    // =====================================================================

    /// <summary>
    /// Pipeline completo: corre todas las fórmulas en orden y rellena los
    /// outputs computados de <paramref name="losa"/>:
    /// <see cref="Losa.HCalc"/>, <see cref="Losa.HEq"/>, <see cref="Losa.Qmamp"/>,
    /// <see cref="Losa.Qmap"/>, <see cref="Losa.Qd"/>, <see cref="Losa.Ql"/>,
    /// <see cref="Losa.Qu"/>.
    ///
    /// <para>
    /// Si <see cref="Losa.HUsarOverride"/> está set, se usa ese valor como espesor
    /// activo; de lo contrario se calcula con <see cref="ComputeHUsar"/>.
    /// El espesor activo se sincroniza a <see cref="Losa.Espesor"/> (campo que
    /// también consume <c>Losas.exe</c>).
    /// </para>
    ///
    /// <para>
    /// Si <see cref="Losa.CarryQuToCarga"/> es <c>true</c>, también se sincroniza
    /// <see cref="Losa.Qu"/> a <see cref="Losa.Carga"/>. Default <c>false</c> para
    /// no pisar overrides manuales del LosasPlus.App.
    /// </para>
    /// </summary>
    public static void RecalcularLosa(Losa losa, Sistema sistema, Proyecto proyecto)
    {
        if (losa     is null) throw new ArgumentNullException(nameof(losa));
        if (sistema  is null) throw new ArgumentNullException(nameof(sistema));
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));

        // 1. h_calc + h_usar
        var hCalc = ComputeHCalc(losa, proyecto.FyKgCm2);
        losa.HCalc = Math.Round(hCalc, 6);

        var hUsar = losa.HUsarOverride ?? ComputeHUsar(hCalc);
        losa.Espesor = hUsar;

        // 2. h_eq
        var hEq = ComputeHEq(losa, hUsar, proyecto.FyKgCm2);
        losa.HEq = Math.Round(hEq, 6);

        // 3. Qmamp + Qmap
        var qmamp = ComputeQmamp(losa.HPisoTecho, hUsar, losa.MampN, losa.MampO, losa.MampP);
        losa.Qmamp = Math.Round(qmamp, 6);

        var qmap = ComputeQmap(qmamp, losa.Area);
        losa.Qmap = Math.Round(qmap, 6);

        // 4. Qd
        var qd = ComputeQd(hEq, proyecto.Cargas, sistema.Uso, qmap);
        losa.Qd = Math.Round(qd, 6);

        // 5. Ql
        var ql = ComputeQl(sistema.Uso, proyecto.Cargas);
        losa.Ql = ql;

        // 6. Qu
        var qu = ComputeQu(qd, ql, proyecto.Cargas.Factores);
        losa.Qu = Math.Round(qu, 6);

        // 7. Sync opcional a Carga
        if (losa.CarryQuToCarga)
            losa.Carga = qu;

        // 8. αfm (ACI 9.5.3.3): rigideces de viga vs losa
        try
        {
            var afm = ComputeAlphaFm(losa, proyecto.VigaPrincipal, hUsar);
            losa.AlphaX = Math.Round(afm.AlphaX, 4);
            losa.AlphaY = Math.Round(afm.AlphaY, 4);
            losa.AlphaM = Math.Round(afm.AlphaM, 4);
            losa.EstadoAlphaFm = afm.Estado;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Losa con luces degeneradas o viga inválida: dejar nulls.
            losa.AlphaX = null;
            losa.AlphaY = null;
            losa.AlphaM = null;
            losa.EstadoAlphaFm = null;
        }

        // 9. Volúmenes / cantidades de bovedilla (cómputos métricos)
        try
        {
            var bov = losa.Cond == "1D" ? proyecto.Bovedilla1D : proyecto.Bovedilla2D;
            var vols = ComputeVolumenes(losa, bov, hUsar);
            losa.CantBovedillasX = vols.CantBovedillasX;
            losa.CantBovedillasY = vols.CantBovedillasY;
            losa.CantBovedillasTotal = vols.Total;
            losa.VBovedilla = Math.Round(vols.VBovedilla, 6);
            losa.VTotal     = Math.Round(vols.VTotal,     6);
            losa.VConcreto  = Math.Round(vols.VConcreto,  6);
        }
        catch (ArgumentOutOfRangeException)
        {
            losa.CantBovedillasX = null;
            losa.CantBovedillasY = null;
            losa.CantBovedillasTotal = null;
            losa.VBovedilla = null;
            losa.VTotal     = null;
            losa.VConcreto  = null;
        }

        // 10. Acero distribuido (si el usuario completó RefuerzoX/Y)
        losa.AsxCalc = Math.Round(ComputeAsTotal(losa.RefuerzoX), 4);
        losa.AsyCalc = Math.Round(ComputeAsTotal(losa.RefuerzoY), 4);
    }

    /// <summary>Recalcula todas las losas de un sistema.</summary>
    public static void RecalcularSistema(Sistema sistema, Proyecto proyecto)
    {
        foreach (var losa in sistema.Losas)
            RecalcularLosa(losa, sistema, proyecto);
    }

    /// <summary>
    /// Recalcula todos los sistemas y losas de un proyecto, recorriendo el árbol
    /// completo <c>Edificios → Niveles → Sistemas</c> vía
    /// <see cref="LosasPlus.Services.ProyectoService.EnumerarSistemas"/> — no sólo
    /// la fachada legacy <c>proyecto.Sistemas</c> (= <c>Niveles[0]</c>), que omitía
    /// los sistemas de las plantas 2+ (B2c).
    /// </summary>
    public static void RecalcularProyecto(Proyecto proyecto)
    {
        foreach (var sistema in LosasPlus.Services.ProyectoService.EnumerarSistemas(proyecto))
            RecalcularSistema(sistema, proyecto);
    }
}
