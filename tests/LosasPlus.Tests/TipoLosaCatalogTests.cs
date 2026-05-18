using System.IO;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Cobertura del catálogo de tipos: cualquier tipo presente en un .TXT real producido
/// por el motor (Losas v5.20) debe tener entrada en <see cref="TipoLosa.Catalogo"/>.
/// Si este test rompe al agregar un fixture nuevo, significa que el motor usa un tipo
/// que aún no documentamos — agregarlo al catálogo con la mejor inferencia disponible
/// y marcar como "verificado: false" hasta confirmarlo contra el PDF.
/// </summary>
public class TipoLosaCatalogTests
{
    private const string FIX_ADN = "fixtures/sistema_demo_27_losas.TXT";

    private static ParsedOutput LoadFixture(string relPath)
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, relPath);
        return TxtParser.ParseFile(path);
    }

    [Fact]
    public void Todos_los_tipos_del_fixture_estan_en_el_catalogo()
    {
        var p = LoadFixture(FIX_ADN);
        var tiposEnFixture = p.PorLosa
            .Where(l => l.Tipo.HasValue)
            .Select(l => l.Tipo!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        // Esperados: 10, 21, 22, 31, 32, 40, 51, 52, 60
        Assert.Equal(new[] { 10, 21, 22, 31, 32, 40, 51, 52, 60 }, tiposEnFixture);

        var faltantes = tiposEnFixture.Where(t => !TipoLosa.Catalogo.ContainsKey(t)).ToArray();
        Assert.True(faltantes.Length == 0,
            "Tipos usados por el motor pero no en el catálogo: " + string.Join(", ", faltantes));
    }

    /// <summary>Los 23 tipos permitidos por la regla de negocio (catálogo Pieper-Martens).</summary>
    private static readonly int[] CodigosCanonicos =
    {
        10, 13, 14, 21, 22, 23, 24, 31, 32, 33, 34, 40, 43, 44,
        51, 52, 53, 54, 60, 63, 64, 71, 72,
    };

    [Fact]
    public void Catalogo_tiene_exactamente_los_23_tipos_canonicos()
    {
        // El catálogo debe ser EXACTAMENTE los 23 — ni más ni menos.
        var enCatalogo = TipoLosa.Catalogo.Keys.OrderBy(x => x).ToArray();
        Assert.Equal(CodigosCanonicos.OrderBy(x => x).ToArray(), enCatalogo);
        Assert.Equal(23, TipoLosa.Catalogo.Count);
    }

    [Fact]
    public void Catalogo_NO_incluye_los_tipos_espurios_11_41_50()
    {
        // Regresión: 11, 41 y 50 fueron retirados (aliases legacy / variante
        // no estándar). Nunca deben volver al catálogo.
        Assert.False(TipoLosa.Catalogo.ContainsKey(11));
        Assert.False(TipoLosa.Catalogo.ContainsKey(41));
        Assert.False(TipoLosa.Catalogo.ContainsKey(50));
    }

    [Fact]
    public void EsCodigoValido_acepta_los_23_y_rechaza_espurios_y_desconocidos()
    {
        foreach (var c in CodigosCanonicos)
            Assert.True(TipoLosa.EsCodigoValido(c), $"Tipo {c} debería ser válido");

        // Espurios retirados
        Assert.False(TipoLosa.EsCodigoValido(11));
        Assert.False(TipoLosa.EsCodigoValido(41));
        Assert.False(TipoLosa.EsCodigoValido(50));
        // Códigos arbitrarios
        Assert.False(TipoLosa.EsCodigoValido(0));
        Assert.False(TipoLosa.EsCodigoValido(99));
        Assert.False(TipoLosa.EsCodigoValido(-1));
    }

    [Fact]
    public void NormalizarCodigo_remapea_11_y_50_pero_no_41_ni_validos()
    {
        // Aliases legacy con patrón de bordes idéntico → remapeo seguro.
        Assert.Equal(10, TipoLosa.NormalizarCodigo(11));
        Assert.Equal(60, TipoLosa.NormalizarCodigo(50));
        // 41 NO es alias seguro → se deja tal cual (lo rechaza la validación).
        Assert.Equal(41, TipoLosa.NormalizarCodigo(41));
        // Códigos válidos pasan sin cambios.
        Assert.Equal(40, TipoLosa.NormalizarCodigo(40));
        Assert.Equal(72, TipoLosa.NormalizarCodigo(72));
        // Código desconocido se deja tal cual.
        Assert.Equal(99, TipoLosa.NormalizarCodigo(99));
    }

    [Fact]
    public void Losa_con_tipo_invalido_falla_IDataErrorInfo()
    {
        var losa = new Losa { Lx = 4, Ly = 4, Espesor = 0.12, Carga = 1.0, Rec = 0.02 };
        losa.Tipo = 40;
        Assert.True(losa.TipoEsValido);
        Assert.True(losa.EsValida);

        losa.Tipo = 41;  // espurio retirado
        Assert.False(losa.TipoEsValido);
        Assert.False(losa.EsValida);
        Assert.Contains("no permitido", ((System.ComponentModel.IDataErrorInfo)losa)["Tipo"]);

        losa.Tipo = 99;  // desconocido
        Assert.False(losa.TipoEsValido);
    }

    [Fact]
    public void Cada_entrada_del_catalogo_tiene_descripcion_e_icono()
    {
        foreach (var (cod, t) in TipoLosa.Catalogo)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Descripcion), $"Tipo {cod}: descripción vacía");
            Assert.False(string.IsNullOrWhiteSpace(t.BordesIco), $"Tipo {cod}: icono vacío");
            Assert.InRange(t.BordesContinuos, 0, 4);
        }
    }

    [Fact]
    public void FormatTabla_devuelve_codigo_solo_si_tipo_no_existe()
    {
        Assert.Equal("99", TipoLosa.FormatTabla(99));
        Assert.StartsWith("60 — ", TipoLosa.FormatTabla(60));
    }

    [Theory]
    [InlineData(10, 0)]   // 4 simples → 0 empotramientos
    [InlineData(21, 1)]   // 1 borde
    [InlineData(22, 1)]
    [InlineData(31, 2)]   // 2 paralelos
    [InlineData(32, 2)]
    [InlineData(33, 2)]   // 2 adyacentes (esquina)
    [InlineData(40, 3)]   // 3 bordes
    [InlineData(60, 4)]   // perimetral
    public void Conteo_de_bordes_continuos_es_consistente_con_codigo(int codigo, int empotramientosEsperados)
    {
        Assert.True(TipoLosa.Catalogo.TryGetValue(codigo, out var t), $"Tipo {codigo} no en catálogo");
        Assert.Equal(empotramientosEsperados, t!.BordesContinuos);
    }
}
