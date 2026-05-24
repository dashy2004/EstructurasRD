// =====================================================================
// RefuerzoFormatter — Adapter pragmático lossy entre el dominio
// (que guarda solo ÁREAS de acero, no número/tamaño de varilla) y la
// hoja custom `EstructurasRD_Design` (que el usuario espera ver con
// strings tipo "Adopta 5#5 (As=9.94 cm²)").
//
// Fase INTEROP-I1 (Parte B) del Plan Maestro de Expansión 3D —
// EstructurasRD.
//
// Decisiones de diseño:
//   1. CATÁLOGO ACI 318 IMPERIAL: usamos el set estándar #3..#11
//      (cm² por varilla), que es la convención dominante en RD para
//      concreto armado.
//   2. TAMAÑO POR DEFECTO POR FAMILIA: vigas → #5 (1.99 cm²),
//      columnas → #6 (2.84 cm²), zapatas → #5@20cm (placeholder).
//      Estas elecciones son pedagógicas — no son cálculo de diseño.
//   3. ESTRIBOS: no hay storage de estribos en el dominio. Emitimos
//      strings ACI 318 por defecto ("#3@d/2 ACI §9.6" para vigas,
//      "#3@d/4 ACI §18.7.5" para columnas, "N/A" para zapatas).
//   4. AISLAMIENTO: este helper es `internal static`, sin dependencias
//      del SDK SAF, sin EPPlus, sin WPF. Sólo `System` y
//      `LosasPlus.Columnas` / `LosasPlus.Rc` para los tipos de dominio.
// =====================================================================
using System;
using System.Globalization;
using LosasPlus.Columnas;
using LosasPlus.Rc;

namespace LosasPlus.Interop.SAF;

/// <summary>
/// Convierte áreas de acero del dominio (m²) en strings legibles con
/// número y tamaño de varilla adoptados, usando un catálogo ACI 318
/// imperial determinista. Emite también strings de estribos por
/// defecto ACI 318-19 para cada familia de elemento.
/// </summary>
internal static class RefuerzoFormatter
{
    // -----------------------------------------------------------------
    // Catálogo ACI 318 imperial — área nominal por varilla en cm².
    // Fuente: ACI 318-19 Apéndice A, equivalente métrico.
    // -----------------------------------------------------------------
    private const double AreaCm2Numero3  = 0.71;   // 3/8"
    private const double AreaCm2Numero4  = 1.29;   // 1/2"
    private const double AreaCm2Numero5  = 1.99;   // 5/8"
    private const double AreaCm2Numero6  = 2.84;   // 3/4"

    /// <summary>
    /// Etiqueta legible cuando el dominio no posee área positiva
    /// — caso típico de un tramo sin refuerzo asignado todavía.
    /// </summary>
    private const string SinRefuerzo = "Sin refuerzo asignado";

    // =================================================================
    // VIGAS — Top + Bot por separado
    // =================================================================

    /// <summary>
    /// Formatea el refuerzo longitudinal de un tramo de viga. Como cada
    /// tramo tiene dos caras (top, bot), se emite un combo
    /// "Top: N#5 / Bot: M#5". Tamaño base: #5 (1.99 cm²). Mínimo: 2
    /// barras por cara (ACI §9.6.1.1). Si ambas caras tienen área cero
    /// se devuelve <see cref="SinRefuerzo"/>.
    /// </summary>
    public static string FormatearLongitudinalViga(RefuerzoLongitudinal? refuerzo)
    {
        if (refuerzo is null) return SinRefuerzo;
        double topCm2 = refuerzo.AreaAceroTop * 1e4;
        double botCm2 = refuerzo.AreaAceroBot * 1e4;
        if (topCm2 <= 1e-6 && botCm2 <= 1e-6) return SinRefuerzo;

        int topCount = AdoptarConteo(topCm2, AreaCm2Numero5, minimo: 2);
        int botCount = AdoptarConteo(botCm2, AreaCm2Numero5, minimo: 2);
        double topProv = topCount * AreaCm2Numero5;
        double botProv = botCount * AreaCm2Numero5;

        return string.Create(CultureInfo.InvariantCulture,
            $"Top {topCount}#5 (As={topProv:F2} cm²) / Bot {botCount}#5 (As={botProv:F2} cm²)");
    }

    /// <summary>
    /// Estribos de viga — string ACI 318 §9.6 por defecto. Sin storage
    /// en el dominio, se asume diseño mínimo de cortante.
    /// </summary>
    public static string FormatearEstribosViga() => "#3@d/2 (default ACI 318 §9.6)";

    // =================================================================
    // COLUMNAS — refuerzo perimetral
    // =================================================================

    /// <summary>
    /// Formatea el refuerzo longitudinal de una columna. Se computa el
    /// área total de todas las capas y se distribuye en varillas #6
    /// (2.84 cm²). Mínimo: 4 barras (esquinas, ACI §10.7.3.1).
    /// </summary>
    public static string FormatearLongitudinalColumna(RefuerzoColumna? refuerzo)
    {
        if (refuerzo is null) return SinRefuerzo;
        double totalCm2 = refuerzo.AreaAceroTotal * 1e4;
        if (totalCm2 <= 1e-6) return SinRefuerzo;

        int count = AdoptarConteo(totalCm2, AreaCm2Numero6, minimo: 4);
        double provista = count * AreaCm2Numero6;
        return string.Create(CultureInfo.InvariantCulture,
            $"Adopta {count}#6 perimetrales (As={provista:F2} cm²)");
    }

    /// <summary>
    /// Estribos de columna — string ACI 318 §18.7.5 (confinamiento
    /// sísmico) por defecto.
    /// </summary>
    public static string FormatearEstribosColumna() => "#3@d/4 (default ACI 318 §18.7.5)";

    // =================================================================
    // ZAPATAS — refuerzo ortogonal (sin storage en dominio)
    // =================================================================

    /// <summary>
    /// El dominio v3 de zapatas no almacena refuerzo. Se emite el
    /// requerimiento mínimo ACI 318-19 §13.3.1 como placeholder
    /// documental.
    /// </summary>
    public static string FormatearLongitudinalZapata()
        => "Adopta #5@20cm en ambos sentidos (As_min ACI 318 §13.3.1)";

    /// <summary>Las zapatas no requieren estribos en el modelo estándar.</summary>
    public static string FormatearEstribosZapata() => "N/A";

    // =================================================================
    // HELPER
    // =================================================================

    /// <summary>
    /// Adopta el número mínimo de varillas de área <paramref name="areaUnitariaCm2"/>
    /// que suman al menos <paramref name="areaRequeridaCm2"/>, respetando
    /// un piso de <paramref name="minimo"/> barras. Implementa la
    /// "redondeo a la alza" estándar del diseño en concreto armado.
    /// </summary>
    private static int AdoptarConteo(double areaRequeridaCm2, double areaUnitariaCm2, int minimo)
    {
        if (areaUnitariaCm2 <= 0.0) return minimo;
        int conteoCalculado = (int)Math.Ceiling(areaRequeridaCm2 / areaUnitariaCm2);
        return Math.Max(minimo, conteoCalculado);
    }
}
