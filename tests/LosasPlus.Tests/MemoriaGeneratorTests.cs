using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LosasPlus.Generation;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="MemoriaGenerator"/> contra la plantilla real
/// <c>Memoria_Losas_PLANTILLA.docx</c> (copiada a fixtures/ del test project).
///
/// <para>
/// Estrategia: cada test corre el generador en un archivo temporal único
/// (<see cref="Path.GetTempFileName"/>) y verifica con <see cref="WordprocessingDocument"/>
/// que el contenido del <c>.docx</c> generado tiene los placeholders
/// sustituidos correctamente. Los archivos temporales se limpian con
/// <see cref="IDisposable"/>.
/// </para>
/// </summary>
public class MemoriaGeneratorTests : IDisposable
{
    private const string PlantillaPath = "fixtures/Memoria_Losas_PLANTILLA.docx";
    private readonly string _outputPath;

    public MemoriaGeneratorTests()
    {
        // Ruta de output unica por test (tempfile + extension .docx).
        _outputPath = Path.Combine(Path.GetTempPath(),
            $"memoria_test_{Guid.NewGuid():N}.docx");
    }

    public void Dispose()
    {
        if (File.Exists(_outputPath))
        {
            try { File.Delete(_outputPath); } catch { /* ignorar */ }
        }
    }

    private static Proyecto BuildProyectoFixture() => new Proyecto
    {
        Nombre                  = "Torre Test",
        Ciudad                  = "Santo Domingo",
        MesAno                  = "12/2025",
        UbicacionCompleta       = "Av. Test #123",
        Uso                     = "Residencial",
        CantidadNiveles         = 8,
        SistemaEstructural      = "Aporticado",
        Autor                   = "Ing. Test García",
        Codia                   = "99999",
        TelefonoFijo            = "(809) 000-0000",
        TelefonoCelular         = "(809) 000-0001",
        DisenadorArquitectonico = "Arq. Test López",
        TipoFundaciones         = "Zapatas aisladas",
        EsfuerzoAdmisible       = 3.5,
        ProfundidadDesplante    = 2.0,
        OtrosParametros         = "Suelo tipo B según R-001.",
    };

    // =================================================================
    // Plantilla y fixture sanos
    // =================================================================

    [Fact]
    public void Plantilla_existe_en_fixtures()
    {
        Assert.True(File.Exists(PlantillaPath),
            $"Plantilla no copiada al output del test: {Path.GetFullPath(PlantillaPath)}");
    }

    [Fact]
    public void Plantilla_no_se_modifica_al_generar()
    {
        var hashAntes = HashFile(PlantillaPath);
        var gen = new MemoriaGenerator();

        gen.Generar(BuildProyectoFixture(), PlantillaPath, _outputPath);

        var hashDespues = HashFile(PlantillaPath);
        Assert.Equal(hashAntes, hashDespues);
    }

    // =================================================================
    // Generación: sustituciones reales
    // =================================================================

    [Fact]
    public void Generar_produce_un_docx_valido_y_no_vacio()
    {
        var gen = new MemoriaGenerator();
        var reporte = gen.Generar(BuildProyectoFixture(), PlantillaPath, _outputPath);

        Assert.True(File.Exists(_outputPath));
        Assert.True(new FileInfo(_outputPath).Length > 0);
        Assert.True(reporte.SustitucionesAplicadas > 0);

        // El archivo debe ser un .docx valido (apertura limpia con OpenXml).
        using var doc = WordprocessingDocument.Open(_outputPath, isEditable: false);
        Assert.NotNull(doc.MainDocumentPart);
    }

    [Fact]
    public void Generar_sustituye_todos_los_placeholders_conocidos()
    {
        var gen = new MemoriaGenerator();
        var reporte = gen.Generar(BuildProyectoFixture(), PlantillaPath, _outputPath);

        // No deben quedar placeholders huerfanos (todos los de la plantilla
        // deben estar cubiertos por la tabla del generador).
        Assert.True(reporte.Exito,
            "Quedaron placeholders sin sustituir: " +
            string.Join(", ", reporte.PlaceholdersNoSustituidos));

        // El output no debe contener ningun {{...}} en su texto.
        var textoCompleto = ExtraerTodoElTexto(_outputPath);
        Assert.DoesNotMatch(@"\{\{[^}]+\}\}", textoCompleto);
    }

    [Fact]
    public void Generar_inyecta_los_valores_del_proyecto_en_el_docx()
    {
        var p = BuildProyectoFixture();
        var gen = new MemoriaGenerator();
        gen.Generar(p, PlantillaPath, _outputPath);

        var texto = ExtraerTodoElTexto(_outputPath);

        Assert.Contains(p.Nombre,                  texto);
        Assert.Contains(p.UbicacionCompleta,       texto);
        Assert.Contains(p.Ciudad,                  texto);
        Assert.Contains(p.MesAno,                  texto);
        Assert.Contains(p.Uso,                     texto);
        Assert.Contains(p.CantidadNiveles.ToString(), texto);
        Assert.Contains(p.SistemaEstructural,      texto);
        Assert.Contains(p.Autor,                   texto);
        Assert.Contains(p.Codia,                   texto);
        Assert.Contains(p.TelefonoFijo,            texto);
        Assert.Contains(p.TelefonoCelular,         texto);
        Assert.Contains(p.DisenadorArquitectonico, texto);
        Assert.Contains(p.TipoFundaciones,         texto);
        Assert.Contains("3.50",                    texto);  // EsfuerzoAdmisible 3.5 → "3.50"
        Assert.Contains("2.00",                    texto);  // ProfundidadDesplante 2.0 → "2.00"
        Assert.Contains(p.OtrosParametros,         texto);
    }

    [Fact]
    public void Generar_aplica_la_fecha_de_calculo_personalizada()
    {
        var p = BuildProyectoFixture();
        var gen = new MemoriaGenerator();

        var fecha = new DateTime(2024, 11, 15);
        gen.Generar(p, PlantillaPath, _outputPath, fechaCalculo: fecha);

        var texto = ExtraerTodoElTexto(_outputPath);
        Assert.Contains("15/11/2024", texto);
    }

    [Fact]
    public void Generar_con_fecha_default_usa_fecha_actual()
    {
        var p = BuildProyectoFixture();
        var gen = new MemoriaGenerator();

        var antes = DateTime.Now.Date;
        gen.Generar(p, PlantillaPath, _outputPath);
        var despues = DateTime.Now.Date;

        var texto = ExtraerTodoElTexto(_outputPath);
        // Debe estar la fecha de hoy formateada dd/MM/yyyy.
        var hoy = antes.ToString("dd/MM/yyyy");
        var hoy2 = despues.ToString("dd/MM/yyyy");
        Assert.True(texto.Contains(hoy) || texto.Contains(hoy2),
            $"No se encontró ni '{hoy}' ni '{hoy2}' en el documento.");
    }

    // =================================================================
    // Edge cases
    // =================================================================

    [Fact]
    public void Generar_lanza_si_plantilla_no_existe()
    {
        var gen = new MemoriaGenerator();
        Assert.Throws<FileNotFoundException>(() =>
            gen.Generar(BuildProyectoFixture(), "fixtures/no-existe.docx", _outputPath));
    }

    [Fact]
    public void Generar_lanza_si_proyecto_es_null()
    {
        var gen = new MemoriaGenerator();
        Assert.Throws<ArgumentNullException>(() =>
            gen.Generar(null!, PlantillaPath, _outputPath));
    }

    [Fact]
    public void Generar_sobrescribe_output_existente()
    {
        // Crear un archivo dummy en outputPath.
        File.WriteAllText(_outputPath, "contenido viejo");
        var sizeViejo = new FileInfo(_outputPath).Length;

        var gen = new MemoriaGenerator();
        gen.Generar(BuildProyectoFixture(), PlantillaPath, _outputPath);

        var sizeNuevo = new FileInfo(_outputPath).Length;
        Assert.NotEqual(sizeViejo, sizeNuevo);  // El docx generado pesa mas que "contenido viejo"
    }

    [Fact]
    public void Generar_funciona_con_strings_vacios()
    {
        var p = new Proyecto { Nombre = "Solo nombre" };  // todo lo demás vacío/default
        var gen = new MemoriaGenerator();
        var reporte = gen.Generar(p, PlantillaPath, _outputPath);

        Assert.True(reporte.Exito);
        var texto = ExtraerTodoElTexto(_outputPath);
        Assert.Contains("Solo nombre", texto);
    }

    [Fact]
    public void Reporte_PlaceholdersNoSustituidos_es_vacio_para_plantilla_estandar()
    {
        var gen = new MemoriaGenerator();
        var reporte = gen.Generar(BuildProyectoFixture(), PlantillaPath, _outputPath);

        Assert.Empty(reporte.PlaceholdersNoSustituidos);
    }

    // =================================================================
    // Helpers
    // =================================================================

    private static string HashFile(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var s = File.OpenRead(path);
        return Convert.ToBase64String(sha.ComputeHash(s));
    }

    /// <summary>
    /// Extrae todo el texto del documento (body + headers + footers) concatenado.
    /// Usado para asserts de "el valor X aparece en el .docx".
    /// </summary>
    private static string ExtraerTodoElTexto(string docxPath)
    {
        using var doc = WordprocessingDocument.Open(docxPath, isEditable: false);
        var sb = new System.Text.StringBuilder();

        var main = doc.MainDocumentPart!;
        if (main.Document.Body != null)
        {
            foreach (var t in main.Document.Body.Descendants<Text>())
                sb.AppendLine(t.Text ?? "");
        }
        foreach (var hp in main.HeaderParts)
            if (hp.Header != null)
                foreach (var t in hp.Header.Descendants<Text>())
                    sb.AppendLine(t.Text ?? "");
        foreach (var fp in main.FooterParts)
            if (fp.Footer != null)
                foreach (var t in fp.Footer.Descendants<Text>())
                    sb.AppendLine(t.Text ?? "");

        return sb.ToString();
    }
}
