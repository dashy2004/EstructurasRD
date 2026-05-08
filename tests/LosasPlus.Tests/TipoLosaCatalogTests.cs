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

    [Fact]
    public void Catalogo_incluye_tipos_basicos_de_la_grilla_del_programa()
    {
        // Basados en la grilla visible en la GUI de Losas.exe (Ver. 5.20 Abr 2013):
        // Fila 1: 10 21 31 40 51 60 71
        // Fila 2: 22 32 52 72
        // Fila 3 / 4: variantes 13/14/23/24/33/34/43/44/53/54/63/64
        int[] esperados = { 10, 21, 22, 31, 32, 40, 51, 52, 60, 71, 72,
                            13, 14, 23, 24, 33, 34, 43, 44, 53, 54, 63, 64 };
        var faltantes = esperados.Where(t => !TipoLosa.Catalogo.ContainsKey(t)).ToArray();
        Assert.True(faltantes.Length == 0,
            "Tipos visibles en la GUI no presentes en el catálogo: " + string.Join(", ", faltantes));
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
