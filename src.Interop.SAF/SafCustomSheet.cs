// =====================================================================
// SafCustomSheet — Inyecta una hoja `EstructurasRD_Design` en el
// archivo .xlsx generado por el SDK SAF, vía EPPlus 4.5.3.3 (LGPL,
// transitivo del SDK SAF). La hoja contiene metadatos lossy de
// refuerzo + ratio D/C que no caben en el esquema SAF nativo.
//
// Fase INTEROP-I1 (Parte B) del Plan Maestro de Expansión 3D —
// EstructurasRD.
//
// Decisiones de diseño:
//   1. PIPELINE EN 2 ETAPAS: primero el SDK SAF escribe las ~30 hojas
//      estándar del spec 2.2.0. Luego este helper reabre el .xlsx con
//      EPPlus, agrega UNA hoja extra `EstructurasRD_Design` y guarda.
//      SCIA Engineer / RFEM ignorarán esta hoja (no está en el spec);
//      EstructurasRD la leerá para recuperar el diseño completo en el
//      roundtrip — esa lectura es alcance de la Fase INTEROP-I2.
//
//   2. ESTILO CORPORATIVO: header oscuro `#FF333333` con texto blanco
//      negrita; banner italic gris claro con timestamp; freeze panes
//      en la fila 3. AutoFitColumns al final para que las columnas
//      queden legibles.
//
//   3. ORDENAMIENTO DETERMINISTA: filas ordenadas por (TipoElemento,
//      Id ascendente). Vigas primero, luego Columnas, luego Zapatas —
//      mismo orden lógico de las fases de diseño 3→4→5→6.
//
//   4. EPPlus 4.5.3.3 LGPL: NO requiere LicenseContext (eso es EPPlus
//      5+). Usamos los namespaces estándar OfficeOpenXml y
//      OfficeOpenXml.Style.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Rc;
using LosasPlus.Topologia;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace LosasPlus.Interop.SAF;

/// <summary>
/// Inyector de la hoja custom `EstructurasRD_Design` en un .xlsx SAF
/// ya escrito por el SDK. Operación in-place: abre, modifica, guarda.
/// </summary>
internal static class SafCustomSheet
{
    /// <summary>Nombre exacto de la hoja custom — leído por el importer en la futura Fase INTEROP-I2.</summary>
    public const string NombreHoja = "EstructurasRD_Design";

    /// <summary>Headers de la hoja — orden y nombre fijados por contrato.</summary>
    private static readonly string[] Headers =
    {
        "Elemento_Tipo",
        "Elemento_Id",
        "Refuerzo_Longitudinal",
        "Estribos_Cortante",
        "Recubrimiento_Mm",
        "Ratio_Demanda_Capacidad",
    };

    /// <summary>Recubrimiento por defecto para zapatas, mm (ACI 318 §20.5.1.3 — concreto contra terreno).</summary>
    private const double RecubrimientoZapataMm = 75.0;

    /// <summary>Recubrimiento por defecto cuando la Viga o Columna no expone uno explícito.</summary>
    private const double RecubrimientoDefaultMm = 40.0;

    /// <summary>
    /// Agrega la hoja `EstructurasRD_Design` al package abierto. NO
    /// llama a <c>Save()</c> — el caller es responsable de sellar el
    /// package para liberar los handles de Windows.
    /// </summary>
    /// <param name="package">EPPlus package abierto sobre el .xlsx SAF.</param>
    /// <param name="proyecto">Proyecto de origen — fuente de los datos lossy.</param>
    /// <param name="ratios">Mapa DomainKey → ratio D/C precomputado por <see cref="RatioCalculator"/>.</param>
    public static void Inyectar(
        ExcelPackage package,
        Proyecto proyecto,
        IReadOnlyDictionary<DomainKey, double> ratios)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(proyecto);
        ArgumentNullException.ThrowIfNull(ratios);

        var ws = package.Workbook.Worksheets.Add(NombreHoja);

        EscribirEncabezado(ws);
        EscribirBanner(ws);
        int siguienteFila = EscribirFilas(ws, proyecto, ratios);

        AplicarEstiloVisual(ws, siguienteFila);
    }

    // =================================================================
    // 1) Encabezado (fila 1)
    // =================================================================

    private static void EscribirEncabezado(ExcelWorksheet ws)
    {
        for (int i = 0; i < Headers.Length; i++)
            ws.Cells[1, i + 1].Value = Headers[i];

        var header = ws.Cells[1, 1, 1, Headers.Length];
        header.Style.Font.Bold = true;
        header.Style.Font.Color.SetColor(System.Drawing.Color.White);
        header.Style.Fill.PatternType = ExcelFillStyle.Solid;
        // #FF333333 — gris oscuro corporativo de EstructurasRD.
        header.Style.Fill.BackgroundColor.SetColor(
            System.Drawing.Color.FromArgb(0xFF, 0x33, 0x33, 0x33));
        header.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        header.Style.VerticalAlignment   = ExcelVerticalAlignment.Center;
    }

    // =================================================================
    // 2) Banner metadata (fila 2) — fusionada A2:F2
    // =================================================================

    private static void EscribirBanner(ExcelWorksheet ws)
    {
        // Formateamos timestamp con culture-invariant antes de concatenar
        // para evitar resolver overloads ambiguos de string.Create.
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string textoBanner =
            "Generado por EstructurasRD · " + timestamp + " UTC · "
            + "Adopciones de varilla son deterministas según RefuerzoFormatter — no son cálculo de diseño.";

        var banner = ws.Cells[2, 1, 2, Headers.Length];
        banner.Merge = true;
        ws.Cells[2, 1].Value = textoBanner;
        banner.Style.Font.Italic = true;
        banner.Style.Fill.PatternType = ExcelFillStyle.Solid;
        // #FFF0F0F0 — gris claro neutro.
        banner.Style.Fill.BackgroundColor.SetColor(
            System.Drawing.Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0));
        banner.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
    }

    // =================================================================
    // 3) Filas de datos (desde fila 3)
    // =================================================================

    /// <summary>
    /// Vuelca una fila por cada Viga / Columna / Zapata del proyecto,
    /// ordenadas por tipo (Viga → Columna → Zapata) y luego por Id
    /// ascendente. Devuelve la fila siguiente disponible (útil para el
    /// freeze panes y el AutoFitColumns).
    /// </summary>
    private static int EscribirFilas(
        ExcelWorksheet ws, Proyecto proyecto,
        IReadOnlyDictionary<DomainKey, double> ratios)
    {
        // Materializamos las listas para poder ordenarlas de forma
        // determinista — el orden de iteración de las ObservableCollection
        // del dominio refleja el orden de inserción del usuario, que NO
        // queremos perder. Ordenamos por Id ASC dentro de cada familia.
        var vigas    = new List<(Viga V, string NivelNombre, double RecMm)>();
        var columnas = new List<(Columna C, string NivelNombre, double RecMm)>();
        var zapatas  = new List<(ZapataAislada Z, string NivelNombre)>();

        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                string nivelNombre = nivel.Nombre ?? "Nivel ?";
                foreach (var v in nivel.Vigas)
                    vigas.Add((v, nivelNombre, RecubrimientoVigaMm(v)));
                foreach (var c in nivel.Columnas)
                    columnas.Add((c, nivelNombre, RecubrimientoColumnaMm(c)));
                foreach (var z in nivel.Zapatas)
                    zapatas.Add((z, nivelNombre));
            }
        }

        vigas.Sort((a, b) => a.V.Id.CompareTo(b.V.Id));
        columnas.Sort((a, b) => a.C.Id.CompareTo(b.C.Id));
        zapatas.Sort((a, b) => a.Z.Id.CompareTo(b.Z.Id));

        int fila = 3;
        foreach (var (v, _, recMm) in vigas)
        {
            // El refuerzo longitudinal de viga vive en el primer tramo —
            // EstructurasRD muestra los refuerzos por tramo en su UI,
            // pero la hoja custom usa el primer tramo como representativo.
            var tramo = v.Tramos.Count > 0 ? v.Tramos[0] : null;
            EscribirFila(ws, fila,
                tipo:   "Viga",
                id:     v.Id,
                refLng: RefuerzoFormatter.FormatearLongitudinalViga(tramo?.Refuerzo),
                estrib: RefuerzoFormatter.FormatearEstribosViga(),
                recMm:  recMm,
                ratio:  GetRatio(ratios, TipoElemento.Viga, v.Id));
            fila++;
        }

        foreach (var (c, _, recMm) in columnas)
        {
            EscribirFila(ws, fila,
                tipo:   "Columna",
                id:     c.Id,
                refLng: RefuerzoFormatter.FormatearLongitudinalColumna(c.Acero),
                estrib: RefuerzoFormatter.FormatearEstribosColumna(),
                recMm:  recMm,
                ratio:  GetRatio(ratios, TipoElemento.Columna, c.Id));
            fila++;
        }

        foreach (var (z, _) in zapatas)
        {
            EscribirFila(ws, fila,
                tipo:   "Zapata",
                id:     z.Id,
                refLng: RefuerzoFormatter.FormatearLongitudinalZapata(),
                estrib: RefuerzoFormatter.FormatearEstribosZapata(),
                recMm:  RecubrimientoZapataMm,
                ratio:  GetRatio(ratios, TipoElemento.Zapata, z.Id));
            fila++;
        }

        return fila;
    }

    private static void EscribirFila(
        ExcelWorksheet ws, int fila,
        string tipo, int id, string refLng, string estrib, double recMm, double ratio)
    {
        ws.Cells[fila, 1].Value = tipo;
        ws.Cells[fila, 2].Value = id;
        ws.Cells[fila, 3].Value = refLng;
        ws.Cells[fila, 4].Value = estrib;
        ws.Cells[fila, 5].Value = recMm;
        ws.Cells[fila, 6].Value = ratio;

        // Formato numérico explícito para D/C (3 decimales) y recubrimiento
        // (1 decimal) — evita que Excel los muestre con notación científica.
        ws.Cells[fila, 5].Style.Numberformat.Format = "0.0";
        ws.Cells[fila, 6].Style.Numberformat.Format = "0.000";

        // Resaltado de filas con ratio fuera de capacidad: fondo rojo
        // tenue (#FFFFE0E0) para que el ingeniero las detecte al abrir.
        if (ratio > 1.0)
        {
            var rng = ws.Cells[fila, 1, fila, Headers.Length];
            rng.Style.Fill.PatternType = ExcelFillStyle.Solid;
            rng.Style.Fill.BackgroundColor.SetColor(
                System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xE0, 0xE0));
        }
    }

    // =================================================================
    // 4) Estilo final (freeze panes + autofit)
    // =================================================================

    private static void AplicarEstiloVisual(ExcelWorksheet ws, int filasFinales)
    {
        // Freeze panes en la fila 3 — el header y el banner siempre visibles.
        ws.View.FreezePanes(3, 1);

        // AutoFit sólo si hubo filas — el caso vacío deja columnas con
        // ancho mínimo y evita un NullReferenceException de EPPlus 4.5.x
        // cuando Dimension es null.
        if (filasFinales > 3 && ws.Dimension is not null)
        {
            // EPPlus 4.5.x: parámetros posicionales, sin nombre.
            // (MinimumWidth=10) — evita que columnas estrechas (Id, ratio)
            // queden ilegibles.
            ws.Cells[ws.Dimension.Address].AutoFitColumns(10.0);
        }
    }

    // =================================================================
    // Helpers para recubrimiento
    // =================================================================

    /// <summary>
    /// Recubrimiento de una viga, mm. Se toma del primer tramo con
    /// Seccion válida; default <see cref="RecubrimientoDefaultMm"/> si
    /// la viga no tiene tramos o sus secciones son nulas.
    /// </summary>
    private static double RecubrimientoVigaMm(Viga viga)
    {
        foreach (var t in viga.Tramos)
        {
            if (t.Seccion is SeccionRC s)
                return s.Recubrimiento * 1000.0;
        }
        return RecubrimientoDefaultMm;
    }

    /// <summary>
    /// Recubrimiento de una columna, mm. El dominio NO lo almacena
    /// explícitamente — se infiere del primer capa de acero como
    /// <c>Profundidad − Ø/2</c>, asumiendo varilla #6 (Ø = 19 mm).
    /// Si no hay capas o el cálculo da un valor irrazonable, se usa
    /// el default 40 mm.
    /// </summary>
    private static double RecubrimientoColumnaMm(Columna col)
    {
        const double DiametroVarillaNumero6Mm = 19.0;
        if (col.Acero?.Capas.Count > 0)
        {
            double profundidadMm = col.Acero.Capas[0].Profundidad * 1000.0;
            double inferido = profundidadMm - DiametroVarillaNumero6Mm * 0.5;
            if (inferido > 10.0 && inferido < 150.0) return inferido;
        }
        return RecubrimientoDefaultMm;
    }

    private static double GetRatio(
        IReadOnlyDictionary<DomainKey, double> ratios, TipoElemento tipo, int id)
        => ratios.TryGetValue(new DomainKey(tipo, id), out var r) ? r : 0.0;
}
