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
    }

    /// <summary>Recalcula todas las losas de un sistema.</summary>
    public static void RecalcularSistema(Sistema sistema, Proyecto proyecto)
    {
        foreach (var losa in sistema.Losas)
            RecalcularLosa(losa, sistema, proyecto);
    }

    /// <summary>Recalcula todos los sistemas y losas de un proyecto.</summary>
    public static void RecalcularProyecto(Proyecto proyecto)
    {
        foreach (var sistema in proyecto.Sistemas)
            RecalcularSistema(sistema, proyecto);
    }
}
