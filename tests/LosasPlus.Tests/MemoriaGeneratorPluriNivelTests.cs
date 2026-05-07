using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LosasPlus.Calculo;
using LosasPlus.Generation;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del render plurinivel de <see cref="MemoriaGenerator"/>: el bloque
/// entre <c>{{NIVEL_BLOQUE_INICIO}}</c> y <c>{{NIVEL_BLOQUE_FIN}}</c> debe
/// clonarse una vez por <see cref="Sistema"/> del proyecto, con sus
/// placeholders <c>{{NIVEL_NOMBRE}}</c>, <c>{{NIVEL_USO}}</c>, etc. sustituidos.
///
/// <para>
/// Los tests usan fixtures <c>.docx</c> creados programáticamente por
/// <see cref="ConstruirPlantillaConMarkers"/> (no por la plantilla real del
/// ingeniero, que actualmente no tiene markers). Esto mantiene el test
/// auto-contenido y permite cubrir edge cases (0 sistemas, 1 sistema, 5
/// sistemas) sin depender de archivos externos.
/// </para>
/// </summary>
public class MemoriaGeneratorPluriNivelTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* ignorar */ }
        }
    }

    private string TempFile(string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), $"memoria_test_{Guid.NewGuid():N}.{ext}");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Construye una plantilla mínima con bloque plurinivel. Estructura:
    /// <list type="number">
    ///   <item>Portada: <c>"Proyecto: {{NOMBRE_PROYECTO}}"</c></item>
    ///   <item>Marker inicio: <c>{{NIVEL_BLOQUE_INICIO}}</c></item>
    ///   <item>Cabecera nivel: <c>"## Nivel: {{NIVEL_NOMBRE}}"</c></item>
    ///   <item>Detalle nivel: <c>"Uso: {{NIVEL_USO}}, Cota: {{NIVEL_COTA}}, Losas: {{NIVEL_NUMERO_LOSAS}}, Idx: {{NIVEL_NUMERO}}"</c></item>
    ///   <item>Marker fin: <c>{{NIVEL_BLOQUE_FIN}}</c></item>
    ///   <item>Cierre: <c>"Fin del documento"</c></item>
    /// </list>
    /// </summary>
    private string ConstruirPlantillaConMarkers()
    {
        var path = TempFile("docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            P("Proyecto: {{NOMBRE_PROYECTO}}"),
            P("{{NIVEL_BLOQUE_INICIO}}"),
            P("## Nivel: {{NIVEL_NOMBRE}}"),
            P("Uso: {{NIVEL_USO}}, Cota: {{NIVEL_COTA}}, Losas: {{NIVEL_NUMERO_LOSAS}}, Idx: {{NIVEL_NUMERO}}"),
            P("{{NIVEL_BLOQUE_FIN}}"),
            P("Fin del documento")
        ));
        main.Document.Save();
        return path;

        static Paragraph P(string text) => new Paragraph(new Run(new Text(text)));
    }

    /// <summary>Plantilla minimal sin markers (solo portada y cierre).</summary>
    private string ConstruirPlantillaSinMarkers()
    {
        var path = TempFile("docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("Proyecto: {{NOMBRE_PROYECTO}}"))),
            new Paragraph(new Run(new Text("Sin niveles aquí")))
        ));
        main.Document.Save();
        return path;
    }

    private static Proyecto ProyectoCon(params (string nombre, SistemaUso uso, double cota, int losas)[] sistemas)
    {
        var p = new Proyecto { Nombre = "Test PluriNivel" };
        foreach (var s in sistemas)
        {
            var sis = new Sistema { Nombre = s.nombre, Uso = s.uso, CotaMetros = s.cota };
            for (int i = 0; i < s.losas; i++)
                sis.Losas.Add(new Losa { Id = i + 1, Lx = 4, Ly = 4 });
            p.Sistemas.Add(sis);
        }
        return p;
    }

    private static string ExtraerTexto(string docxPath)
    {
        using var doc = WordprocessingDocument.Open(docxPath, isEditable: false);
        var sb = new System.Text.StringBuilder();
        if (doc.MainDocumentPart?.Document.Body is { } body)
            foreach (var p in body.Descendants<Paragraph>())
                sb.AppendLine(string.Concat(p.Descendants<Text>().Select(t => t.Text ?? "")));
        return sb.ToString();
    }

    // =================================================================
    // Render plurinivel: clone count
    // =================================================================

    [Fact]
    public void RenderearNiveles_clona_template_una_vez_por_sistema()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(
            ("E1",    SistemaUso.Entrepiso, 2.80, 5),
            ("E2",    SistemaUso.Entrepiso, 5.60, 3),
            ("Techo", SistemaUso.Techo,     8.40, 2));

        var rep = new MemoriaGenerator().Generar(p, plantilla, output);

        Assert.Equal(3, rep.NivelesRenderizados);

        var texto = ExtraerTexto(output);
        Assert.Contains("## Nivel: E1",    texto);
        Assert.Contains("## Nivel: E2",    texto);
        Assert.Contains("## Nivel: Techo", texto);
    }

    [Fact]
    public void RenderearNiveles_inserta_los_clones_en_orden()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(
            ("E1",    SistemaUso.Entrepiso, 2.80, 5),
            ("E2",    SistemaUso.Entrepiso, 5.60, 3),
            ("Techo", SistemaUso.Techo,     8.40, 2));

        new MemoriaGenerator().Generar(p, plantilla, output);

        var texto = ExtraerTexto(output);
        int posE1    = texto.IndexOf("## Nivel: E1");
        int posE2    = texto.IndexOf("## Nivel: E2");
        int posTecho = texto.IndexOf("## Nivel: Techo");
        int posFin   = texto.IndexOf("Fin del documento");

        Assert.True(posE1 < posE2);
        Assert.True(posE2 < posTecho);
        Assert.True(posTecho < posFin);
    }

    [Fact]
    public void RenderearNiveles_remueve_los_markers_del_output()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(("E1", SistemaUso.Entrepiso, 2.80, 1));

        new MemoriaGenerator().Generar(p, plantilla, output);

        var texto = ExtraerTexto(output);
        Assert.DoesNotContain("{{NIVEL_BLOQUE_INICIO}}", texto);
        Assert.DoesNotContain("{{NIVEL_BLOQUE_FIN}}",    texto);
    }

    // =================================================================
    // Sustitucion por nivel
    // =================================================================

    [Fact]
    public void RenderearNiveles_sustituye_NIVEL_USO_por_sistema()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(
            ("E1",    SistemaUso.Entrepiso, 2.80, 5),
            ("Techo", SistemaUso.Techo,     8.40, 2));

        new MemoriaGenerator().Generar(p, plantilla, output);

        var texto = ExtraerTexto(output);
        // El primer nivel debe tener "Uso: Entrepiso", el segundo "Uso: Techo".
        Assert.Matches(@"## Nivel: E1\s*Uso: Entrepiso",  texto);
        Assert.Matches(@"## Nivel: Techo\s*Uso: Techo",   texto);
    }

    [Fact]
    public void RenderearNiveles_formatea_NIVEL_COTA_con_signo_y_dos_decimales()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(("E1", SistemaUso.Entrepiso, 2.8, 5));

        new MemoriaGenerator().Generar(p, plantilla, output);

        var texto = ExtraerTexto(output);
        Assert.Contains("Cota: +2.80 m", texto);
    }

    [Fact]
    public void RenderearNiveles_inyecta_NIVEL_NUMERO_LOSAS_correcto_por_sistema()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(
            ("E1",    SistemaUso.Entrepiso, 2.80, 5),
            ("Techo", SistemaUso.Techo,     8.40, 2));

        new MemoriaGenerator().Generar(p, plantilla, output);

        var texto = ExtraerTexto(output);
        // E1 → 5 losas; Techo → 2 losas. Cada NUMERO_LOSAS aparece una vez.
        var matches = Regex.Matches(texto, @"Losas: (\d+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "5", "2" }, matches);
    }

    [Fact]
    public void RenderearNiveles_inyecta_NIVEL_NUMERO_uno_based()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(
            ("E1",    SistemaUso.Entrepiso, 2.80, 1),
            ("E2",    SistemaUso.Entrepiso, 5.60, 1),
            ("E3",    SistemaUso.Entrepiso, 8.40, 1));

        new MemoriaGenerator().Generar(p, plantilla, output);

        var texto = ExtraerTexto(output);
        var idxs = Regex.Matches(texto, @"Idx: (\d+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "1", "2", "3" }, idxs);
    }

    // =================================================================
    // Edge cases
    // =================================================================

    [Fact]
    public void RenderearNiveles_con_0_sistemas_remueve_todo_el_bloque()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Sin niveles" };  // Sistemas vacio

        var rep = new MemoriaGenerator().Generar(p, plantilla, output);

        Assert.Equal(0, rep.NivelesRenderizados);
        var texto = ExtraerTexto(output);
        Assert.DoesNotContain("## Nivel:",                 texto);
        Assert.DoesNotContain("{{NIVEL_BLOQUE_INICIO}}",    texto);
        Assert.DoesNotContain("{{NIVEL_BLOQUE_FIN}}",       texto);
        Assert.Contains("Proyecto: Sin niveles",            texto);
        Assert.Contains("Fin del documento",                texto);
    }

    [Fact]
    public void RenderearNiveles_con_1_sistema_renderea_uno()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(("Único", SistemaUso.Entrepiso, 0.0, 1));

        var rep = new MemoriaGenerator().Generar(p, plantilla, output);

        Assert.Equal(1, rep.NivelesRenderizados);
        var texto = ExtraerTexto(output);
        Assert.Single(Regex.Matches(texto, @"## Nivel: Único").Cast<Match>());
    }

    [Fact]
    public void RenderearNiveles_sin_markers_es_no_op()
    {
        var plantilla = ConstruirPlantillaSinMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(
            ("E1", SistemaUso.Entrepiso, 2.80, 1),
            ("E2", SistemaUso.Entrepiso, 5.60, 1));

        var rep = new MemoriaGenerator().Generar(p, plantilla, output);

        Assert.Equal(0, rep.NivelesRenderizados);
        var texto = ExtraerTexto(output);
        // La plantilla sin markers no genera niveles, pero los placeholders
        // de portada SI deben sustituirse.
        Assert.Contains("Proyecto: Test PluriNivel", texto);
        Assert.Contains("Sin niveles aquí",          texto);
    }

    [Fact]
    public void Plantilla_real_del_ingeniero_no_tiene_markers_y_genera_sin_renderizar_niveles()
    {
        // Plantilla actual no tiene markers — debe generar OK con
        // NivelesRenderizados=0 y placeholders de portada todos cubiertos.
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Compat Test" };

        var rep = new MemoriaGenerator().Generar(p, "fixtures/Memoria_Losas_PLANTILLA.docx", output);

        Assert.Equal(0, rep.NivelesRenderizados);
        Assert.True(rep.SustitucionesAplicadas > 0,
            "La plantilla real debería tener sustituciones de portada.");
    }

    // =================================================================
    // Reporte combinado (portada + plurinivel)
    // =================================================================

    // =================================================================
    // {{TABLA_LOSAS}} — tabla OpenXml por nivel
    // =================================================================

    /// <summary>
    /// Plantilla minimal con bloque NIVEL que incluye un placeholder
    /// <c>{{TABLA_LOSAS}}</c> en su propio párrafo.
    /// </summary>
    private string ConstruirPlantillaConTablaLosas()
    {
        var path = TempFile("docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("Proyecto: {{NOMBRE_PROYECTO}}"))),
            new Paragraph(new Run(new Text("{{NIVEL_BLOQUE_INICIO}}"))),
            new Paragraph(new Run(new Text("Nivel: {{NIVEL_NOMBRE}}"))),
            new Paragraph(new Run(new Text("{{TABLA_LOSAS}}"))),
            new Paragraph(new Run(new Text("Fin del nivel."))),
            new Paragraph(new Run(new Text("{{NIVEL_BLOQUE_FIN}}"))),
            new Paragraph(new Run(new Text("Cierre")))
        ));
        main.Document.Save();
        return path;
    }

    private static (Proyecto proyecto, Sistema sistema) ProyectoConLosasReales()
    {
        var p = new Proyecto { Nombre = "Tabla Test", FyKgCm2 = 4200 };
        var s = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso };
        s.Losas.Add(new Losa { Id = 1, Lx = 6.45, Ly = 5.40, MampO = 1.78 });
        s.Losas.Add(new Losa { Id = 2, Lx = 4.90, Ly = 4.45, MampO = 7.67 });
        s.Losas.Add(new Losa { Id = 3, Lx = 4.90, Ly = 4.40 });
        p.Sistemas.Add(s);
        CalculoEngine.RecalcularProyecto(p);
        return (p, s);
    }

    [Fact]
    public void TablaLosas_remueve_el_placeholder_y_inserta_una_Table()
    {
        var plantilla = ConstruirPlantillaConTablaLosas();
        var output = TempFile("docx");
        var (p, _) = ProyectoConLosasReales();

        new MemoriaGenerator().Generar(p, plantilla, output);

        // El texto extraido NO debe contener el placeholder literal.
        var texto = ExtraerTexto(output);
        Assert.DoesNotContain("{{TABLA_LOSAS}}", texto);

        // Debe haber al menos una Table en el body.
        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablas = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();
        Assert.NotEmpty(tablas);
    }

    [Fact]
    public void TablaLosas_tiene_header_mas_una_fila_por_losa()
    {
        var plantilla = ConstruirPlantillaConTablaLosas();
        var output = TempFile("docx");
        var (p, sistema) = ProyectoConLosasReales();

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tabla = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().Single();

        var rows = tabla.Elements<TableRow>().ToList();
        // 1 header + 3 losas
        Assert.Equal(1 + sistema.Losas.Count, rows.Count);
    }

    [Fact]
    public void TablaLosas_inyecta_los_valores_computados_de_cada_losa()
    {
        var plantilla = ConstruirPlantillaConTablaLosas();
        var output = TempFile("docx");
        var (p, sistema) = ProyectoConLosasReales();

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tabla = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().Single();
        var rows = tabla.Elements<TableRow>().ToList();

        // Fila Losa 1: ID=1, Lx=6.45, Ly=5.40, Cond=2D, h=0.150, qu≈1.089
        var fila1 = string.Concat(rows[1].Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.Contains("6.45",    fila1);
        Assert.Contains("5.40",    fila1);
        Assert.Contains("2D",      fila1);
        Assert.Contains("0.150",   fila1);
        Assert.Contains("1.089",   fila1);   // Qu losa 1 del .xls Neapolis

        // Fila Losa 3 (sin mamposteria): qu = 0.883
        var fila3 = string.Concat(rows[3].Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.Contains("0.883", fila3);
    }

    [Fact]
    public void TablaLosas_genera_una_tabla_por_sistema()
    {
        var plantilla = ConstruirPlantillaConTablaLosas();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Multi", FyKgCm2 = 4200 };
        var e1 = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso };
        e1.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        e1.Losas.Add(new Losa { Id = 2, Lx = 5, Ly = 4 });
        var techo = new Sistema { Nombre = "Techo", Uso = SistemaUso.Techo };
        techo.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        p.Sistemas.Add(e1);
        p.Sistemas.Add(techo);
        CalculoEngine.RecalcularProyecto(p);

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablas = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();

        // Una tabla por sistema (E1 con 2 losas, Techo con 1).
        Assert.Equal(2, tablas.Count);
        Assert.Equal(1 + 2, tablas[0].Elements<TableRow>().Count());  // header + 2 losas
        Assert.Equal(1 + 1, tablas[1].Elements<TableRow>().Count());  // header + 1 losa
    }

    [Fact]
    public void TablaLosas_sin_recalcular_muestra_dash_en_celdas_computadas()
    {
        var plantilla = ConstruirPlantillaConTablaLosas();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Sin recalc" };
        var s = new Sistema { Nombre = "E1" };
        s.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        p.Sistemas.Add(s);
        // NO llamamos CalculoEngine.RecalcularProyecto — Qd/Ql/Qu quedan en null.

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tabla = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().Single();
        var fila1 = string.Concat(tabla.Elements<TableRow>().ElementAt(1).Descendants<Text>().Select(t => t.Text ?? ""));
        // Las 3 celdas de Qd, Ql, Qu deben mostrar "—".
        Assert.True(fila1.Split('—').Length >= 4,
            $"Esperaba al menos 3 dashes en fila 1, contenido: {fila1}");
    }

    [Fact]
    public void TablaLosas_header_contiene_los_titulos_correctos()
    {
        var plantilla = ConstruirPlantillaConTablaLosas();
        var output = TempFile("docx");
        var (p, _) = ProyectoConLosasReales();

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tabla = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().Single();
        var header = string.Concat(tabla.Elements<TableRow>().First()
                                        .Descendants<Text>().Select(t => t.Text ?? ""));

        Assert.Contains("ID",       header);
        Assert.Contains("Lx",       header);
        Assert.Contains("Ly",       header);
        Assert.Contains("Cond",     header);
        Assert.Contains("h",        header);
        Assert.Contains("qd",       header);
        Assert.Contains("ql",       header);
        Assert.Contains("qu",       header);
    }

    // =================================================================
    // {{TABLA_MOMENTOS}} / {{TABLA_ARMADURAS_X}} / Y / {{TABLA_APOYOS}}
    // =================================================================

    /// <summary>
    /// Plantilla minimal con bloque NIVEL que incluye los 4 placeholders de
    /// salida F. Perdomo (cada uno en su propio párrafo).
    /// </summary>
    private string ConstruirPlantillaConSalidaPerdomo()
    {
        var path = TempFile("docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("Proyecto: {{NOMBRE_PROYECTO}}"))),
            new Paragraph(new Run(new Text("{{NIVEL_BLOQUE_INICIO}}"))),
            new Paragraph(new Run(new Text("Nivel: {{NIVEL_NOMBRE}}"))),
            new Paragraph(new Run(new Text("Momentos:"))),
            new Paragraph(new Run(new Text("{{TABLA_MOMENTOS}}"))),
            new Paragraph(new Run(new Text("Armaduras X:"))),
            new Paragraph(new Run(new Text("{{TABLA_ARMADURAS_X}}"))),
            new Paragraph(new Run(new Text("Armaduras Y:"))),
            new Paragraph(new Run(new Text("{{TABLA_ARMADURAS_Y}}"))),
            new Paragraph(new Run(new Text("Apoyos:"))),
            new Paragraph(new Run(new Text("{{TABLA_APOYOS}}"))),
            new Paragraph(new Run(new Text("{{NIVEL_BLOQUE_FIN}}"))),
            new Paragraph(new Run(new Text("Cierre")))
        ));
        main.Document.Save();
        return path;
    }

    private static Sistema SistemaConSalidaReal()
    {
        var s = new Sistema { Nombre = "E1", Uso = SistemaUso.Entrepiso };
        // Para cubrir las 27 losas del fixture real:
        for (int i = 1; i <= 27; i++) s.Losas.Add(new Losa { Id = i });
        s.SalidaPerdomo = SalidaPerdomoAdapter.FromFile(
            "fixtures/ADN_TORRE_PS2_S_SEMISOTANO.TXT",
            s.Losas.Select(l => l.Id));
        return s;
    }

    [Fact]
    public void TablaMomentos_se_inserta_con_27_losas_del_fixture_real()
    {
        var plantilla = ConstruirPlantillaConSalidaPerdomo();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Test" };
        p.Sistemas.Add(SistemaConSalidaReal());

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablas = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();
        // 4 tablas (momentos + armaduras X + armaduras Y + apoyos) — TABLA_LOSAS
        // no esta en esta plantilla.
        Assert.Equal(4, tablas.Count);

        // La primera tabla es de momentos: 1 header + 27 losas = 28 filas.
        var tablaMomentos = tablas[0];
        Assert.Equal(1 + 27, tablaMomentos.Elements<TableRow>().Count());

        // Header tiene "LOSA TIPO CARGA H Lx Ly Mfx Mfy".
        var header = string.Concat(tablaMomentos.Elements<TableRow>().First()
                                                .Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.Contains("LOSA",   header);
        Assert.Contains("TIPO",   header);
        Assert.Contains("CARGA",  header);
        Assert.Contains("Mfx",    header);
        Assert.Contains("Msx",    header);
    }

    [Fact]
    public void TablaMomentos_inyecta_los_valores_de_la_losa_1_del_fixture()
    {
        var plantilla = ConstruirPlantillaConSalidaPerdomo();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Test" };
        p.Sistemas.Add(SistemaConSalidaReal());

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablaMomentos = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().First();

        // Fila 1 (datos primera losa) debe contener los valores conocidos del .txt.
        var fila1 = string.Concat(tablaMomentos.Elements<TableRow>().ElementAt(1)
                                               .Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.Contains("60",    fila1);   // TIPO
        Assert.Contains("1.550", fila1);   // CARGA
        Assert.Contains("4.000", fila1);   // Lx
        Assert.Contains("7.800", fila1);   // Ly
        Assert.Contains("1.603", fila1);   // Mfx
        Assert.Contains("1.933", fila1);   // -Msx
    }

    [Fact]
    public void TablaArmadurasX_y_Y_se_pueblan_independientes()
    {
        var plantilla = ConstruirPlantillaConSalidaPerdomo();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Test" };
        var s = SistemaConSalidaReal();
        p.Sistemas.Add(s);

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablas = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();

        var armX = tablas[1];
        var armY = tablas[2];

        Assert.Equal(1 + s.SalidaPerdomo!.ArmadurasXCentro.Count, armX.Elements<TableRow>().Count());
        Assert.Equal(1 + s.SalidaPerdomo!.ArmadurasYCentro.Count, armY.Elements<TableRow>().Count());

        // Headers tienen "dX" / "MuX" para X y "dY" / "MuY" para Y.
        var headerX = string.Concat(armX.Elements<TableRow>().First().Descendants<Text>().Select(t => t.Text ?? ""));
        var headerY = string.Concat(armY.Elements<TableRow>().First().Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.Contains("dX", headerX);
        Assert.Contains("MuX", headerX);
        Assert.Contains("dY", headerY);
        Assert.Contains("MuY", headerY);
    }

    [Fact]
    public void TablaApoyos_combina_apoyos_X_y_Y_en_una_sola_tabla_con_columna_Dir()
    {
        var plantilla = ConstruirPlantillaConSalidaPerdomo();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Test" };
        var s = SistemaConSalidaReal();
        p.Sistemas.Add(s);

        new MemoriaGenerator().Generar(p, plantilla, output);

        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablas = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();
        var tablaApoyos = tablas[3];

        var totalApoyos = s.SalidaPerdomo!.ArmadurasXApoyos.Count + s.SalidaPerdomo!.ArmadurasYApoyos.Count;
        Assert.Equal(1 + totalApoyos, tablaApoyos.Elements<TableRow>().Count());

        // Header debe tener "Dir.".
        var header = string.Concat(tablaApoyos.Elements<TableRow>().First().Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.Contains("Dir", header);
        Assert.Contains("MuI", header);

        // Las primeras filas son de dirección X, las últimas de Y.
        var primeraDataRow = string.Concat(tablaApoyos.Elements<TableRow>().ElementAt(1).Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.StartsWith("X", primeraDataRow.TrimStart());

        var ultimaDataRow = string.Concat(tablaApoyos.Elements<TableRow>().Last().Descendants<Text>().Select(t => t.Text ?? ""));
        Assert.StartsWith("Y", ultimaDataRow.TrimStart());
    }

    [Fact]
    public void Sin_SalidaPerdomo_los_4_placeholders_se_reemplazan_por_nota_informativa()
    {
        var plantilla = ConstruirPlantillaConSalidaPerdomo();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Sin txt" };
        var s = new Sistema { Nombre = "E1" };
        s.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        p.Sistemas.Add(s);
        // SalidaPerdomo queda en null (no se importó .txt).

        new MemoriaGenerator().Generar(p, plantilla, output);

        // No debe haber tablas (los placeholders se reemplazan por parrafos
        // informativos, no por Table).
        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablas = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();
        Assert.Empty(tablas);

        var texto = ExtraerTexto(output);
        Assert.DoesNotContain("{{TABLA_MOMENTOS}}",    texto);
        Assert.DoesNotContain("{{TABLA_ARMADURAS_X}}", texto);
        Assert.DoesNotContain("{{TABLA_ARMADURAS_Y}}", texto);
        Assert.DoesNotContain("{{TABLA_APOYOS}}",      texto);

        // 4 menciones de la nota informativa.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            texto, @"\(\.txt no importado para este nivel\)").Count;
        Assert.Equal(4, matches);
    }

    [Fact]
    public void Plurinivel_con_solo_un_sistema_que_tiene_txt_y_otro_que_no()
    {
        var plantilla = ConstruirPlantillaConSalidaPerdomo();
        var output = TempFile("docx");
        var p = new Proyecto { Nombre = "Mixto" };
        // Sistema 1 con .txt importado.
        p.Sistemas.Add(SistemaConSalidaReal());
        // Sistema 2 sin .txt.
        var s2 = new Sistema { Nombre = "E2" };
        s2.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        p.Sistemas.Add(s2);

        new MemoriaGenerator().Generar(p, plantilla, output);

        // Sistema 1: 4 tablas reales. Sistema 2: 4 párrafos informativos.
        using var doc = WordprocessingDocument.Open(output, isEditable: false);
        var tablas = doc.MainDocumentPart!.Document.Body!.Descendants<Table>().ToList();
        Assert.Equal(4, tablas.Count);  // Solo del sistema 1.

        var notas = System.Text.RegularExpressions.Regex.Matches(
            ExtraerTexto(output), @"\(\.txt no importado para este nivel\)").Count;
        Assert.Equal(4, notas);  // Sistema 2 → 4 placeholders sin datos.
    }

    [Fact]
    public void Reporte_combina_correctamente_sustituciones_de_portada_y_plurinivel()
    {
        var plantilla = ConstruirPlantillaConMarkers();
        var output = TempFile("docx");
        var p = ProyectoCon(
            ("E1",    SistemaUso.Entrepiso, 2.80, 5),
            ("Techo", SistemaUso.Techo,     8.40, 2));

        var rep = new MemoriaGenerator().Generar(p, plantilla, output);

        // El reporte cuenta sustituciones de portada (1: NOMBRE_PROYECTO en
        // el primer parrafo) + 5 placeholders × 2 niveles = 10 → 11 total.
        Assert.Equal(11, rep.SustitucionesAplicadas);
        Assert.Equal(2,  rep.NivelesRenderizados);
        Assert.Empty(rep.PlaceholdersNoSustituidos);
    }
}
