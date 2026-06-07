using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Clasificador heurístico de capas DXF → categoría estructural. Verifica el
/// reconocimiento por palabra clave, insensible a mayúsculas y acentos, y el
/// fallback a <see cref="CategoriaEstructural.Otro"/> (lo que dispara la IA).
/// </summary>
public class ClasificadorCapasTests
{
    [Theory]
    [InlineData("VIGAS", CategoriaEstructural.Viga)]
    [InlineData("Viga 25x50", CategoriaEstructural.Viga)]
    [InlineData("S-BEAM", CategoriaEstructural.Viga)]
    [InlineData("COL", CategoriaEstructural.Columna)]
    [InlineData("COLUMNAS DISEÑO", CategoriaEstructural.Columna)]
    [InlineData("Pilares", CategoriaEstructural.Columna)]
    [InlineData("LOSA-2", CategoriaEstructural.Losa)]
    [InlineData("PAÑOS", CategoriaEstructural.Losa)]
    [InlineData("Placa maciza", CategoriaEstructural.Losa)]
    [InlineData("EJES", CategoriaEstructural.Eje)]
    [InlineData("GRID", CategoriaEstructural.Eje)]
    [InlineData("MUROS", CategoriaEstructural.Otro)]
    [InlineData("0", CategoriaEstructural.Otro)]
    [InlineData("", CategoriaEstructural.Otro)]
    [InlineData("   ", CategoriaEstructural.Otro)]
    public void Clasificar_reconoce_la_categoria(string capa, CategoriaEstructural esperada)
        => Assert.Equal(esperada, ClasificadorCapas.Clasificar(capa));

    [Fact]
    public void Clasificar_es_insensible_a_acentos_y_mayusculas()
    {
        Assert.Equal(CategoriaEstructural.Losa, ClasificadorCapas.Clasificar("paño"));
        Assert.Equal(CategoriaEstructural.Losa, ClasificadorCapas.Clasificar("PAÑO"));
    }

    [Fact]
    public void Ambiguas_devuelve_solo_las_no_reconocidas()
    {
        var capas = new[] { "VIGAS", "COL", "MUROS", "ESCALERA", "LOSA" };
        var ambiguas = ClasificadorCapas.Ambiguas(capas);

        Assert.Equal(2, ambiguas.Count);
        Assert.Contains("MUROS", ambiguas);
        Assert.Contains("ESCALERA", ambiguas);
    }

    [Fact]
    public void ClasificarTodas_deduplica_y_mapea()
    {
        var capas = new[] { "VIGAS", "VIGAS", "COL" };
        var mapa = ClasificadorCapas.ClasificarTodas(capas);

        Assert.Equal(2, mapa.Count);
        Assert.Equal(CategoriaEstructural.Viga, mapa["VIGAS"]);
        Assert.Equal(CategoriaEstructural.Columna, mapa["COL"]);
    }
}
