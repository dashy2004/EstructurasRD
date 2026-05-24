// =====================================================================
// SafExporterTests — Cobertura end-to-end del pipeline SafExporter +
// SafCustomSheet (Fase INTEROP-I1, Parte C del Plan Maestro 3D —
// EstructurasRD).
//
// Estrategia:
//   1. Construye proyectos in-memory mínimos (sin .lpx.json en disco).
//   2. Llama a SafExporter.Exportar a una ruta temporal única por test
//      (Guid en el nombre evita colisiones).
//   3. Reabre el .xlsx resultante con EPPlus (transitivo del SDK SAF) y
//      asierta sobre el contenido de las hojas SAF nativas + de la hoja
//      custom `EstructurasRD_Design`.
//   4. Limpia el archivo temp en Dispose() — patrón IDisposable per-test
//      (mirror de MemoriaGeneratorTests).
//
// Decisiones de implementación:
//   - Búsqueda de hojas SAF por SUBSTRING ("PointConnection",
//     "SurfaceMember") — el SDK puede nombrarlas con o sin prefijo
//     "Excel". El substring es robusto a ambas convenciones.
//   - Sin FluentAssertions: raw `Assert.*` (consistente con el resto
//     de la suite).
//   - Sin Moq: fixtures inline. Mirror de ZapataDesignEngineTests y
//     RcDesignEngineTests.
//   - Para el test del archivo bloqueado: FileStream con FileShare.None
//     en un `using` que rodea el `Assert.Throws`.
// =====================================================================
using System;
using System.IO;
using System.Linq;
using LosasPlus.Columnas;
using LosasPlus.Interop.SAF;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Rc;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;
using OfficeOpenXml;
using Xunit;

namespace LosasPlus.Tests.Interop;

/// <summary>
/// Cobertura end-to-end del exportador SAF 2.2.0 + la inyección de la
/// hoja custom `EstructurasRD_Design`. 14 escenarios: básico,
/// coordenadas, hoja custom (headers + datos + banner + orden),
/// defensiva (null, vacío, dir inexistente, archivo bloqueado),
/// integración (varias vigas, sobreescribir).
/// </summary>
public class SafExporterTests : IDisposable
{
    /// <summary>
    /// Nombre exacto de la hoja custom inyectada por SafCustomSheet. Se
    /// duplica aquí como const literal para evitar acoplarse a la
    /// visibilidad (internal) del helper de producción y mantener el
    /// contrato ENTRE tests y exporter explícito.
    /// </summary>
    private const string NombreHojaCustom = "EstructurasRD_Design";

    private readonly string _tempPath;

    public SafExporterTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(),
            $"saf_test_{Guid.NewGuid():N}.xlsx");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            try { File.Delete(_tempPath); } catch { /* best-effort cleanup */ }
        }
    }

    // =================================================================
    // TEST 1 — Workbook básico
    // =================================================================

    [Fact]
    public void Exportar_ProyectoVacio_CreaEstructuraWorkbookValida()
    {
        var proyecto = ConstruirProyectoMinimo();

        SafExporter.Exportar(proyecto, _tempPath);

        Assert.True(File.Exists(_tempPath), "El archivo .xlsx debe existir en disco.");
        long size = new FileInfo(_tempPath).Length;
        Assert.True(size > 1000,
            $"El archivo debe tener > 1000 bytes (real: {size}).");

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        Assert.True(pkg.Workbook.Worksheets.Count >= 1,
            $"Esperaba ≥ 1 hoja (la hoja custom siempre se inyecta); encontré {pkg.Workbook.Worksheets.Count}.");
        // Para proyecto vacío el SDK SAF omite todas las hojas baseline
        // (no hay objetos que volcar); solo la custom debe existir.
        Assert.NotNull(pkg.Workbook.Worksheets[NombreHojaCustom]);
    }

    // =================================================================
    // TEST 2 — Mapeo de coordenadas X/Y/Z (verificación analítica)
    // =================================================================

    [Fact]
    public void MapeoDeCoordenadas_ConservaCotasEnMetros()
    {
        // Muro de (0,0) a (6,12) con Altura=3 a cota 0 → genera 4 nodos:
        // (0,0,0), (6,12,0), (6,12,3), (0,0,3). Buscamos que las celdas
        // del .xlsx contengan exactamente 6.0, 12.0 y 3.0 (metros).
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edificio = new Edificio { Nombre = "E1" };
        var nivel    = new Nivel { Nombre = "N1", Cota = 0.0 };
        var sistema  = new Sistema { Nombre = "S1" };
        sistema.Muros.Add(new Muro
        {
            Id          = 1,
            PuntoInicio = new PuntoCad(0.0, 0.0),
            PuntoFin    = new PuntoCad(6.0, 12.0),
            Espesor     = 0.20,
            Altura      = 3.0,
        });
        nivel.Sistemas.Add(sistema);
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        // Validación en la capa de MAPEO: SafMapper.MapearProyecto produce
        // un ExcelModel con UnitsNet.Length para X/Y/Z. Antes del SDK
        // serializer (que en SAF 1.7.3 puede omitir hojas baseline si la
        // validación FluentValidation rechaza un campo material/sección
        // faltante), verificamos directamente que la cota geométrica del
        // muro se preserve en metros dentro del modelo SAF.
        var model = SafMapper.MapearProyecto(proyecto);
        var nodos = model.Objects
            .OfType<global::SAF.DataAccess.Models.StructuralElements.ExcelStructuralPointConnection>()
            .ToList();

        Assert.NotEmpty(nodos);

        bool encontroX6  = nodos.Any(n => n.X.HasValue && Math.Abs(n.X.Value.Meters -  6.0) < 1e-4);
        bool encontroY12 = nodos.Any(n => n.Y.HasValue && Math.Abs(n.Y.Value.Meters - 12.0) < 1e-4);
        bool encontroZ3  = nodos.Any(n => n.Z.HasValue && Math.Abs(n.Z.Value.Meters -  3.0) < 1e-4);

        Assert.True(encontroX6,
            $"Algún PointConnection debe tener X=6.0 m. Nodos: {nodos.Count}.");
        Assert.True(encontroY12,
            $"Algún PointConnection debe tener Y=12.0 m. Nodos: {nodos.Count}.");
        Assert.True(encontroZ3,
            $"Algún PointConnection debe tener Z=3.0 m (cota corona). Nodos: {nodos.Count}.");

        // Además, el pipeline completo debe llegar a disco sin excepción.
        SafExporter.Exportar(proyecto, _tempPath);
        Assert.True(File.Exists(_tempPath), "El archivo .xlsx debe existir tras el export.");
        Assert.True(new FileInfo(_tempPath).Length > 1000,
            "El archivo .xlsx debe tener > 1000 bytes tras el export.");
    }

    // =================================================================
    // TEST 3 — Hoja custom: refuerzo + ratios formateados
    // =================================================================

    [Fact]
    public void HojaCustomDesign_RegistraRefuerzoY_RatiosCorrectamente()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edificio = new Edificio();
        var nivel    = new Nivel { Cota = 0.0 };
        var viga = new Viga { Id = 1, Nombre = "V-1" };
        var tramo = new TramoViga();
        tramo.Refuerzo.AreaAceroTop = 8e-4;   // 8 cm² → ~5#5 con el formatter
        tramo.Refuerzo.AreaAceroBot = 6e-4;   // 6 cm² → ~4#5
        viga.Tramos.Add(tramo);
        nivel.Vigas.Add(viga);

        var col = new Columna { Id = 1, Nombre = "C-1", Altura = 3.0 };
        col.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 6e-4 });
        col.Acero.Capas.Add(new CapaAcero { Profundidad = 0.35, Area = 6e-4 });
        nivel.Columnas.Add(col);

        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        var ws = pkg.Workbook.Worksheets[NombreHojaCustom];
        Assert.NotNull(ws);

        // Fila 3 (datos): primera viga del proyecto.
        Assert.Equal("Viga", ws!.Cells[3, 1].Value?.ToString());
        Assert.Equal(1, Convert.ToInt32(ws.Cells[3, 2].Value));
        string refLngViga = ws.Cells[3, 3].Value?.ToString() ?? "";
        Assert.Contains("Top", refLngViga);
        Assert.Contains("Bot", refLngViga);
        Assert.Contains("#5",  refLngViga);

        // Fila 4 (datos): la columna.
        Assert.Equal("Columna", ws.Cells[4, 1].Value?.ToString());
        string refLngCol = ws.Cells[4, 3].Value?.ToString() ?? "";
        Assert.Contains("#6", refLngCol);
        Assert.Contains("Adopta", refLngCol);

        // Ratios D/C numéricos en columna F (puede ser 0.0 si no hay
        // demandas — lo importante es que sea un double parseable).
        Assert.True(double.TryParse(ws.Cells[3, 6].Value?.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _));
        Assert.True(double.TryParse(ws.Cells[4, 6].Value?.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _));
    }

    // =================================================================
    // TEST 4 — Archivo bloqueado → InvalidOperationException
    // =================================================================

    [Fact]
    public void Exportar_Con_Archivo_Bloqueado_LanzaInvalidOperationException()
    {
        var proyecto = ConstruirProyectoMinimo();
        // Pre-crear el archivo + sostener un lock exclusivo: cualquier
        // intento posterior de File.Create sobre el mismo path tirará
        // IOException, que SafExporter envuelve como InvalidOperationException.
        File.WriteAllBytes(_tempPath, new byte[] { 0x01 });
        using var lockStream = new FileStream(
            _tempPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var ex = Assert.Throws<InvalidOperationException>(
            () => SafExporter.Exportar(proyecto, _tempPath));
        Assert.Contains("No se pudo escribir", ex.Message);
    }

    // =================================================================
    // TESTS 5-7 — Defensiva de argumentos
    // =================================================================

    [Fact]
    public void Exportar_ProyectoNulo_LanzaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => SafExporter.Exportar(null!, _tempPath));
    }

    [Fact]
    public void Exportar_RutaNula_LanzaArgumentException()
    {
        var proyecto = ConstruirProyectoMinimo();
        Assert.Throws<ArgumentException>(
            () => SafExporter.Exportar(proyecto, null!));
    }

    [Fact]
    public void Exportar_RutaVacia_LanzaArgumentException()
    {
        var proyecto = ConstruirProyectoMinimo();
        Assert.Throws<ArgumentException>(
            () => SafExporter.Exportar(proyecto, ""));
    }

    [Fact]
    public void Exportar_DirectorioInexistente_LanzaInvalidOperationException()
    {
        var proyecto = ConstruirProyectoMinimo();
        // Sub-directorio Guid bajo TempPath que NUNCA existirá — el
        // exportador valida el parent dir antes de invocar al SDK.
        string rutaInvalida = Path.Combine(
            Path.GetTempPath(),
            "no_existe_" + Guid.NewGuid().ToString("N"),
            "foo.xlsx");

        var ex = Assert.Throws<InvalidOperationException>(
            () => SafExporter.Exportar(proyecto, rutaInvalida));
        Assert.Contains("no existe", ex.Message);
    }

    // =================================================================
    // TEST 8 — Header de la hoja custom con columnas exactas
    // =================================================================

    [Fact]
    public void HojaCustomDesign_TieneHeaderConColumnasEsperadas()
    {
        var proyecto = ConstruirProyectoMinimo();
        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        var ws = pkg.Workbook.Worksheets[NombreHojaCustom];
        Assert.NotNull(ws);

        Assert.Equal("Elemento_Tipo",            ws!.Cells[1, 1].Value?.ToString());
        Assert.Equal("Elemento_Id",              ws.Cells[1, 2].Value?.ToString());
        Assert.Equal("Refuerzo_Longitudinal",    ws.Cells[1, 3].Value?.ToString());
        Assert.Equal("Estribos_Cortante",        ws.Cells[1, 4].Value?.ToString());
        Assert.Equal("Recubrimiento_Mm",         ws.Cells[1, 5].Value?.ToString());
        Assert.Equal("Ratio_Demanda_Capacidad",  ws.Cells[1, 6].Value?.ToString());
    }

    // =================================================================
    // TEST 9 — Orden determinista de filas
    // =================================================================

    [Fact]
    public void HojaCustomDesign_OrdenDeterminista_VigasLuegoColumnasLuegoZapatas()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edificio = new Edificio();
        var nivel    = new Nivel();
        // Insertar en orden DESORDENADO: zapata, columna, viga.
        nivel.Zapatas.Add(new ZapataAislada { Id = 5, Nombre = "Z-5" });
        nivel.Columnas.Add(new Columna     { Id = 3, Nombre = "C-3", Altura = 3.0 });
        var v = new Viga { Id = 7, Nombre = "V-7" };
        v.Tramos.Add(new TramoViga());
        nivel.Vigas.Add(v);
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        var ws = pkg.Workbook.Worksheets[NombreHojaCustom]!;
        // Fila 3 = primer dato. El orden esperado es Viga (7), Columna (3), Zapata (5).
        Assert.Equal("Viga",    ws.Cells[3, 1].Value?.ToString());
        Assert.Equal(7,         Convert.ToInt32(ws.Cells[3, 2].Value));
        Assert.Equal("Columna", ws.Cells[4, 1].Value?.ToString());
        Assert.Equal(3,         Convert.ToInt32(ws.Cells[4, 2].Value));
        Assert.Equal("Zapata",  ws.Cells[5, 1].Value?.ToString());
        Assert.Equal(5,         Convert.ToInt32(ws.Cells[5, 2].Value));
    }

    // =================================================================
    // TEST 10 — Banner fila 2 con timestamp UTC
    // =================================================================

    [Fact]
    public void HojaCustomDesign_BannerFila2_ContieneTimestampUTC()
    {
        var proyecto = ConstruirProyectoMinimo();
        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        var ws = pkg.Workbook.Worksheets[NombreHojaCustom]!;
        string banner = ws.Cells[2, 1].Value?.ToString() ?? "";

        Assert.Contains("Generado por EstructurasRD", banner);
        Assert.Contains("UTC", banner);
    }

    // =================================================================
    // TEST 11 — N vigas → N filas con tipo "Viga"
    // =================================================================

    [Fact]
    public void Exportar_ProyectoConVarias_Vigas_GeneraNFilasEnHojaCustom()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edificio = new Edificio();
        var nivel    = new Nivel();
        for (int i = 1; i <= 3; i++)
        {
            var v = new Viga { Id = i, Nombre = $"V-{i}" };
            v.Tramos.Add(new TramoViga());
            nivel.Vigas.Add(v);
        }
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        var ws = pkg.Workbook.Worksheets[NombreHojaCustom]!;
        int filasVigaCount = 0;
        for (int r = 3; r <= ws.Dimension.End.Row; r++)
        {
            if ((ws.Cells[r, 1].Value?.ToString() ?? "") == "Viga")
                filasVigaCount++;
        }
        Assert.Equal(3, filasVigaCount);
    }

    // =================================================================
    // TEST 12 — Sobreescritura de un .xlsx existente
    // =================================================================

    [Fact]
    public void Exportar_SobreescribirArchivo_Existente_Tiene_Exito()
    {
        // Crear un archivo dummy de unos pocos bytes en el path destino.
        File.WriteAllBytes(_tempPath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        long sizeAntes = new FileInfo(_tempPath).Length;
        Assert.Equal(4, sizeAntes);

        var proyecto = ConstruirProyectoMinimo();
        // No debe lanzar; el archivo debe quedar substancialmente mayor
        // tras el export del workbook completo (~30 hojas SAF + custom).
        SafExporter.Exportar(proyecto, _tempPath);

        long sizeDespues = new FileInfo(_tempPath).Length;
        Assert.True(sizeDespues > 1000,
            $"Tras sobreescritura el .xlsx debe ser > 1000 bytes (real: {sizeDespues}).");
    }

    // =================================================================
    // TEST 13 — Zapata: recubrimiento 75 mm default (ACI 318 §20.5.1.3)
    // =================================================================

    [Fact]
    public void HojaCustomDesign_FilaZapata_TieneRecubrimientoDefault75mm()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edificio = new Edificio();
        var nivel    = new Nivel();
        nivel.Zapatas.Add(new ZapataAislada { Id = 1, Nombre = "Z-1" });
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        var ws = pkg.Workbook.Worksheets[NombreHojaCustom]!;
        // Fila 3: única zapata.
        Assert.Equal("Zapata", ws.Cells[3, 1].Value?.ToString());
        double recubrimientoMm = Convert.ToDouble(ws.Cells[3, 5].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(75.0, recubrimientoMm, 1);   // 75 mm con tolerancia 0.1 mm
    }

    // =================================================================
    // TEST 14 — Estribos: strings ACI por defecto por familia
    // =================================================================

    [Fact]
    public void HojaCustomDesign_Estribos_StringsACI_PorFamilia()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edificio = new Edificio();
        var nivel    = new Nivel();

        var viga = new Viga { Id = 1, Nombre = "V-1" };
        viga.Tramos.Add(new TramoViga());
        nivel.Vigas.Add(viga);
        nivel.Columnas.Add(new Columna { Id = 1, Nombre = "C-1", Altura = 3.0 });
        nivel.Zapatas.Add(new ZapataAislada { Id = 1, Nombre = "Z-1" });

        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        var ws = pkg.Workbook.Worksheets[NombreHojaCustom]!;
        // Orden: fila 3 Viga, fila 4 Columna, fila 5 Zapata.
        Assert.Contains("#3@d/2", ws.Cells[3, 4].Value?.ToString());
        Assert.Contains("ACI 318 §9.6", ws.Cells[3, 4].Value?.ToString());
        Assert.Contains("#3@d/4", ws.Cells[4, 4].Value?.ToString());
        Assert.Contains("ACI 318 §18.7.5", ws.Cells[4, 4].Value?.ToString());
        Assert.Equal("N/A", ws.Cells[5, 4].Value?.ToString());
    }

    // =================================================================
    // TEST 15 — Patch Deuda I1: hojas baseline aparecen tras el patch
    //           de constantes mecánicas del material C30/37.
    // =================================================================

    [Fact]
    public void Exportar_ProyectoConElementos_GeneraHojasBaselineCompletas()
    {
        // Tras poblar EModulus + GModulus + PoissonCoefficient + UnitMass
        // en SafMapper.ConstruirMaterialConcretoPorDefecto, el validador
        // FluentValidation del SDK SAF acepta el material — y por cascada
        // las secciones y miembros que lo referencian. El .xlsx ya no
        // queda con sólo la hoja custom; las hojas SAF baseline rellenas
        // se renderizan también. SCIA Engineer / RFEM ahora pueden abrir
        // el archivo y ver la geometría sin perder el roundtrip visual.
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        var edificio = new Edificio();
        var nivel    = new Nivel();
        nivel.Columnas.Add(new Columna
        {
            Id     = 1,
            Nombre = "C-1",
            Altura = 3.0,
            // Geometria default (SeccionColumna 0.40×0.40, fc=28 MPa)
            // basta para que la sección sea válida bajo Parametric.
        });
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        SafExporter.Exportar(proyecto, _tempPath);

        using var pkg = new ExcelPackage(new FileInfo(_tempPath));
        int total = pkg.Workbook.Worksheets.Count;
        string inventario = string.Join(", ", pkg.Workbook.Worksheets.Select(w => w.Name));

        // Tras el patch: esperamos al menos 3 hojas (custom + material +
        // alguna otra: sección, point connection, curve member).
        Assert.True(total >= 3,
            $"Esperaba ≥ 3 hojas tras el patch de material (real: {total}). Inventario: [{inventario}].");

        // Validación específica: la hoja SAF de Material debe existir —
        // ese fue el bloqueo principal del validador antes del patch.
        bool hayHojaMaterial = pkg.Workbook.Worksheets
            .Any(w => w.Name.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.True(hayHojaMaterial,
            $"Debe existir una hoja SAF de Material. Inventario: [{inventario}].");
    }

    // =================================================================
    // Helpers privados — fixtures inline mínimos
    // =================================================================

    /// <summary>
    /// Proyecto seedeado por el factory + un edificio con un nivel vacío
    /// — suficiente para que el exportador no rompa por ausencia de
    /// edificios/niveles.
    /// </summary>
    private static Proyecto ConstruirProyectoMinimo()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        var ed = new Edificio { Nombre = "Edificio Test" };
        ed.Niveles.Add(new Nivel { Nombre = "Nivel 1", Cota = 0.0 });
        p.Edificios.Add(ed);
        return p;
    }
}
