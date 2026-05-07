using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LosasPlus.Generation;
using LosasPlus.Models;
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
