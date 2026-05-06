using System.IO;
using System.Linq;
using LosasPlus.Services;
using Xunit;
using static LosasPlus.Services.TxtLineClassifier.LineKind;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del clasificador de líneas del .TXT — base del resaltado del visor.
/// </summary>
public class TxtLineClassifierTests
{
    [Theory]
    [InlineData("",                                          Empty)]
    [InlineData("   \t  ",                                   Empty)]
    [InlineData("$ comentario",                              Comment)]
    [InlineData("    $   NLOSA       FC        FY",          Comment)]
    [InlineData("---------------------------------",         Separator)]
    [InlineData("================",                          Separator)]
    [InlineData("   CÁLCULO DE MOMENTOS",                    SectionHeader)]
    [InlineData("   ARMADURAS SEGÚN \"X\"",                  SectionHeader)]
    [InlineData("   DISEÑO DE SISTEMAS DE LOSAS",            SectionHeader)]
    [InlineData("    1    60   1.550  25.0  4.000  7.800",   DataRow)]
    [InlineData("   11- 12  0.466  0.503  0.484  21.8",      DataRow)]
    public void Clasifica_lineas_basicas(string line, TxtLineClassifier.LineKind expected)
    {
        Assert.Equal(expected, TxtLineClassifier.Classify(line));
    }

    [Fact]
    public void Header_de_columnas_detecta_LOSA_TIPO_etc()
    {
        var line = "      LOSA  TIPO  CARGA    H    Lx     Ly      Mfx     Mfy    -Msx    -Msy";
        Assert.Equal(ColumnHeader, TxtLineClassifier.Classify(line));
    }

    [Fact]
    public void Linea_de_unidades_se_distingue_de_columnas()
    {
        var line = "                 ton/m²   cm     m      m    [          ton.m/m           ]";
        Assert.Equal(Units, TxtLineClassifier.Classify(line));
    }

    [Fact]
    public void Linea_meta_archivo_de_datos()
    {
        var line = "   Archivo de datos: ADN TORRE PS2 S SEMISOTANO.DL";
        Assert.Equal(Meta, TxtLineClassifier.Classify(line));
    }

    [Fact]
    public void Linea_meta_F_Perdomo_version()
    {
        var line = "   F. Perdomo   Ver. 5.20";
        Assert.Equal(Meta, TxtLineClassifier.Classify(line));
    }

    [Fact]
    public void Linea_de_armadura_cuenta_como_DataRow()
    {
        var line = "            1   21.75   1.603   4.500     Ø3/8\" @ 15 cm      4.733";
        Assert.Equal(DataRow, TxtLineClassifier.Classify(line));
    }

    [Fact]
    public void Texto_no_estructurado_cae_en_Plain()
    {
        var line = "comentario suelto sin formato";
        Assert.Equal(Plain, TxtLineClassifier.Classify(line));
    }

    [Fact]
    public void Aplica_resaltado_a_todas_las_lineas_del_fixture_real_sin_explotar()
    {
        // Smoke test contra el .TXT real: clasifica todas sus líneas y verifica que
        // detecta las secciones esperadas al menos una vez.
        var path = Path.Combine(System.AppContext.BaseDirectory, "fixtures", "ADN_TORRE_PS2_S_SEMISOTANO.TXT");
        Assert.True(File.Exists(path), "Fixture no disponible");
        var lines = File.ReadAllLines(path, System.Text.Encoding.GetEncoding(1252));

        var counts = lines
            .Select(l => TxtLineClassifier.Classify(l))
            .GroupBy(k => k)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.True(counts.GetValueOrDefault(SectionHeader) >= 4,  // 4+ secciones (Momentos, Arm X, Arm Y, Apoyos)
            $"Esperaba al menos 4 SectionHeader, obtuve {counts.GetValueOrDefault(SectionHeader)}");
        Assert.True(counts.GetValueOrDefault(DataRow) >= 50,        // 27 momentos + 27 arm X + 27 arm Y + 22 apoyos
            $"Esperaba al menos 50 DataRow, obtuve {counts.GetValueOrDefault(DataRow)}");
        Assert.True(counts.GetValueOrDefault(Separator) >= 3,
            $"Esperaba al menos 3 Separator, obtuve {counts.GetValueOrDefault(Separator)}");
    }
}
