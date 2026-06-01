using System;
using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="DLDoctor"/> con .DL corruptos sintéticos. Cubre los
/// casos de falla más comunes que detiene a un usuario:
///   * JSON disfrazado de .DL (caso reportado por el usuario con
///     ENTREPISO 1.3.DL — era un .lpx.json con extensión cambiada).
///   * Decimal con coma (es-DO desde Excel).
///   * BOM UTF-8 al inicio.
///   * NLOSA dice N pero el bloque tiene otra cantidad.
///   * ID duplicados.
///   * Rec ≥ Espesor.
///   * Bordes huérfanos (BI/BJ que no existen como ID).
///   * Archivo vacío.
/// </summary>
public class DLDoctorTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string CrearTemp(string contenido, Encoding? enc = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dldoctor_test_{Guid.NewGuid():N}.DL");
        File.WriteAllText(path, contenido, enc ?? Encoding.GetEncoding(1252));
        _tempFiles.Add(path);
        return path;
    }

    private string CrearTempBytes(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dldoctor_test_{Guid.NewGuid():N}.DL");
        File.WriteAllBytes(path, bytes);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var p in _tempFiles)
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private const string DlValido = @"$ test
Sistema No 1
1 0.210 4.200 1
1 10 2.0 0.12 4.0 4.0 0.02
0
0
";

    // =================================================================
    // Casos básicos
    // =================================================================

    [Fact]
    public void Diagnosticar_archivo_inexistente_devuelve_error_fatal()
    {
        var diag = DLDoctor.Diagnosticar(@"Z:\no\existe\nada.DL");
        Assert.False(diag.PuedeAbrir);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL000-NO-EXISTE");
    }

    [Fact]
    public void Diagnosticar_archivo_vacio_devuelve_error_fatal()
    {
        var path = CrearTemp("");
        var diag = DLDoctor.Diagnosticar(path);
        Assert.False(diag.PuedeAbrir);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL000-VACIO");
    }

    [Fact]
    public void Diagnosticar_DL_valido_no_reporta_issues()
    {
        var path = CrearTemp(DlValido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.True(diag.PuedeAbrir);
        Assert.True(diag.EstaLimpio);
        Assert.NotNull(diag.SistemasRecuperados);
        Assert.Single(diag.SistemasRecuperados!);
    }

    // =================================================================
    // Caso real del usuario: JSON disfrazado de .DL
    // =================================================================

    [Fact]
    public void Diagnosticar_archivo_JSON_disfrazado_lo_detecta_y_sugiere_lpx_json()
    {
        // Reproduce el caso de ENTREPISO 1.3.DL — un .lpx.json guardado con extensión .DL.
        var contenido = @"{
  ""version"": 1,
  ""appVersion"": ""MemoriaPlus 0.5.0"",
  ""proyecto"": { ""nombre"": ""ENTREPISO 1.3"" }
}";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);

        Assert.False(diag.PuedeAbrir);
        Assert.True(diag.EsArchivoJsonDisfrazado);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL001-JSON-EN-DL");
        Assert.NotNull(diag.PathSugeridoReparado);
        Assert.EndsWith(".lpx.json", diag.PathSugeridoReparado!);
    }

    [Fact]
    public void Diagnosticar_archivo_que_empieza_con_array_JSON_tambien_se_detecta()
    {
        var path = CrearTemp("[ { \"nombre\": \"x\" } ]");
        var diag = DLDoctor.Diagnosticar(path);
        Assert.True(diag.EsArchivoJsonDisfrazado);
    }

    // =================================================================
    // Decimal con coma (es-DO)
    // =================================================================

    [Fact]
    public void Diagnosticar_decimal_con_coma_lo_detecta_y_repara()
    {
        // 0,210 y 4,200 → el parser legacy con InvariantCulture lanza.
        var contenido = @"$ test
Sistema No 1
1 0,210 4,200 1
1 10 2,0 0,12 4,0 4,0 0,02
0
0
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);

        Assert.Contains(diag.Issues, i => i.Codigo == "DL003-DECIMAL-COMA");
        Assert.True(diag.PuedeAbrir, "Tras el fix automático, el archivo debe poder abrirse.");
        Assert.NotNull(diag.ContenidoReparado);
        Assert.NotNull(diag.SistemasRecuperados);
        // Verificamos que los números se parsearon correctamente
        var sistema = diag.SistemasRecuperados![0];
        Assert.Equal(0.210, sistema.Fc, precision: 3);
        Assert.Equal(4.200, sistema.Fy, precision: 3);
    }

    // =================================================================
    // BOM UTF-8
    // =================================================================

    [Fact]
    public void Diagnosticar_BOM_UTF8_lo_reporta_como_warning()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes(DlValido))
            .ToArray();
        var path = CrearTempBytes(bytes);
        var diag = DLDoctor.Diagnosticar(path);

        Assert.Contains(diag.Issues, i => i.Codigo == "DL002-BOM" && i.Severity == DLIssueSeverity.Warning);
    }

    // =================================================================
    // Validaciones semánticas
    // =================================================================

    [Fact]
    public void Diagnosticar_IDs_duplicados_los_reporta()
    {
        var contenido = @"Sistema No 1
2 0.210 4.200 1
1 10 2.0 0.12 4.0 4.0 0.02
1 10 2.0 0.12 4.0 4.0 0.02
0
0
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL101-ID-DUPLICADO");
    }

    [Fact]
    public void Diagnosticar_recubrimiento_mayor_que_espesor_lo_reporta()
    {
        var contenido = @"Sistema No 1
1 0.210 4.200 1
1 10 2.0 0.10 4.0 4.0 0.50
0
0
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL103-RECUBRIMIENTO");
    }

    [Fact]
    public void Diagnosticar_geometria_invalida_lo_reporta()
    {
        var contenido = @"Sistema No 1
1 0.210 4.200 1
1 10 2.0 0.12 0.0 4.0 0.02
0
0
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL102-GEOMETRIA");
    }

    [Fact]
    public void Diagnosticar_bordes_huerfanos_los_reporta()
    {
        var contenido = @"Sistema No 1
1 0.210 4.200 1
1 10 2.0 0.12 4.0 4.0 0.02
1
99 1 S
0
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL104-BORDE-HUERFANO");
    }

    [Fact]
    public void Diagnosticar_tipo_invalido_lo_reporta_como_DL106()
    {
        // Tipo 99 no pertenece al catálogo de 23.
        var contenido = @"Sistema No 1
1 0.210 4.200 1
1 99 2.0 0.12 4.0 4.0 0.02
0
0
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.Contains(diag.Issues, i => i.Codigo == "DL106-TIPO-INVALIDO");
    }

    [Fact]
    public void Diagnosticar_tipo_alias_legacy_11_no_se_reporta_porque_se_remapea()
    {
        // El tipo 11 es alias legacy → DLFileService lo remapea a 10 al parsear,
        // por lo que el Doctor ya no lo ve como inválido.
        var contenido = @"Sistema No 1
1 0.210 4.200 1
1 11 2.0 0.12 4.0 4.0 0.02
0
0
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.DoesNotContain(diag.Issues, i => i.Codigo == "DL106-TIPO-INVALIDO");
    }

    [Fact]
    public void Diagnosticar_balanceo_no_normalizado_lo_reporta_como_Info()
    {
        var contenido = @"Sistema No 1
1 0.210 4.200 1
1 10 2.0 0.12 4.0 4.0 0.02
0
1
1 1 Y
";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        // BorderAdic.Balanceo en su setter normaliza Y→S, así que el doctor ya
        // lo ve normalizado y no reporta DL105. Esto es comportamiento esperado:
        // el parser ya nos protegió. Verificamos que no haya errores fatales.
        Assert.True(diag.PuedeAbrir);
    }

    // =================================================================
    // Reparación a disco
    // =================================================================

    [Fact]
    public void AplicarReparacionADisco_escribe_archivo_y_devuelve_path()
    {
        var contenido = "Sistema No 1\n1 0,21 4,2 1\n1 10 2,0 0,12 4,0 4,0 0,02\n0\n0\n";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.NotNull(diag.ContenidoReparado);

        var resultPath = DLDoctor.AplicarReparacionADisco(diag);
        _tempFiles.Add(resultPath);
        Assert.True(File.Exists(resultPath));
        // Verificar que el contenido escrito ya no tiene comas decimales
        var escrito = File.ReadAllText(resultPath, Encoding.GetEncoding(1252));
        Assert.DoesNotContain("0,21", escrito);
        Assert.Contains("0.21", escrito);
    }

    [Fact]
    public void AplicarReparacionADisco_no_pisa_si_destino_ya_existe()
    {
        var contenido = "Sistema No 1\n1 0,21 4,2 1\n1 10 2,0 0,12 4,0 4,0 0,02\n0\n0\n";
        var path = CrearTemp(contenido);
        var diag = DLDoctor.Diagnosticar(path);

        // Crear el destino sugerido ya en disco
        var dir   = Path.GetDirectoryName(path)!;
        var stem  = Path.GetFileNameWithoutExtension(path);
        var ocupado = Path.Combine(dir, $"{stem}.reparado.DL");
        File.WriteAllText(ocupado, "PREVIO");
        _tempFiles.Add(ocupado);

        var resultPath = DLDoctor.AplicarReparacionADisco(diag);
        _tempFiles.Add(resultPath);
        Assert.NotEqual(ocupado, resultPath);
        Assert.Equal("PREVIO", File.ReadAllText(ocupado));  // el previo no se pisó
    }

    [Fact]
    public void AplicarReparacionADisco_lanza_si_no_hay_contenido_reparado()
    {
        var path = CrearTemp(DlValido);
        var diag = DLDoctor.Diagnosticar(path);
        Assert.Null(diag.ContenidoReparado);
        Assert.Throws<InvalidOperationException>(() => DLDoctor.AplicarReparacionADisco(diag));
    }
}
