using System;
using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Cargas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="ParserDzpCez"/> (Fase 2, Iteración 3) sobre los
/// archivos `.DZP`/`.CEZ` reales de DISEST copiados a <c>tests/fixtures/</c>.
/// </summary>
public class ParserDzpCezTests
{
    /// <summary>Lee un fixture con la codificación Windows-1252 de DISEST.</summary>
    private static string Leer(string fixture)
        => File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", fixture), Encoding.Latin1);

    [Fact]
    public void Parsea_Combinaciones_DZP_con_11_casos_y_172_combinaciones()
    {
        var cp = ParserDzpCez.Parsear(Leer("Combinaciones.DZP"), FormatoCombinaciones.Dzp);

        Assert.Equal(11, cp.Casos.Count);
        Assert.Equal(172, cp.Combinaciones.Count);   // 86 servicio + 86 últimas
        Assert.Equal(86, cp.Combinaciones.Count(c => c.Tipo == TipoCombinacion.Servicio));
        Assert.Equal(86, cp.Combinaciones.Count(c => c.Tipo == TipoCombinacion.Ultima));
    }

    [Fact]
    public void Parsea_los_codigos_y_tipos_de_caso_del_DZP()
    {
        var cp = ParserDzpCez.Parsear(Leer("Combinaciones.DZP"), FormatoCombinaciones.Dzp);

        Assert.Equal("D", cp.Casos[0].Codigo);
        Assert.Equal("Carga Muerta", cp.Casos[0].Nombre);
        Assert.Equal(TipoCasoCarga.Muerta, cp.Casos[0].Tipo);
        Assert.False(cp.Casos[0].Fsis);

        Assert.Equal("Ex1", cp.Casos[3].Codigo);
        Assert.Equal(TipoCasoCarga.Sismo, cp.Casos[3].Tipo);
        Assert.True(cp.Casos[3].Fsis);   // Sismo X1 → FSIS = 1
        Assert.True(cp.Casos[3].Fce);    // Sismo X1 → FCE = 1
    }

    [Fact]
    public void Parsea_los_factores_de_las_combinaciones_ultimas()
    {
        var cp = ParserDzpCez.Parsear(Leer("Combinaciones.DZP"), FormatoCombinaciones.Dzp);
        var ultimas = cp.Combinaciones.Where(c => c.Tipo == TipoCombinacion.Ultima).ToList();

        // §3.2 combinación 1 = 1.4D
        Assert.Equal("1.4D", ultimas[0].Nombre);
        Assert.Single(ultimas[0].Terminos);
        Assert.Equal("D", ultimas[0].Terminos[0].CodigoCaso);
        Assert.Equal(1.4, ultimas[0].Terminos[0].Factor);

        // §3.2 combinación 2 = 1.2D + 1.6L + 1.6Lr
        Assert.Equal(3, ultimas[1].Terminos.Count);
        Assert.Equal(1.2, ultimas[1].Terminos.Single(t => t.CodigoCaso == "D").Factor);
        Assert.Equal(1.6, ultimas[1].Terminos.Single(t => t.CodigoCaso == "L").Factor);
    }

    [Fact]
    public void Parsea_Combinaciones_CEZ_solo_con_servicio()
    {
        var cp = ParserDzpCez.Parsear(Leer("Combinaciones.CEZ"), FormatoCombinaciones.Cez);

        Assert.Equal(8, cp.Casos.Count);
        Assert.Equal(34, cp.Combinaciones.Count);    // .CEZ no trae el bloque de últimas
        Assert.All(cp.Combinaciones, c => Assert.Equal(TipoCombinacion.Servicio, c.Tipo));
        Assert.Equal("D",  cp.Casos[0].Codigo);      // del SIMB
        Assert.Equal("Ex", cp.Casos[2].Codigo);
    }

    [Fact]
    public void Parsea_un_segundo_DZP_con_distinto_NCC()
    {
        var cp = ParserDzpCez.Parsear(Leer("Viento Sobre DT.DZP"), FormatoCombinaciones.Dzp);

        Assert.Equal(10, cp.Casos.Count);
        Assert.Equal(180, cp.Combinaciones.Count);   // 90 servicio + 90 últimas
    }

    [Fact]
    public void Texto_invalido_lanza_CombinacionesParseException()
    {
        Assert.Throws<CombinacionesParseException>(
            () => ParserDzpCez.Parsear("esto no es un archivo de combinaciones", FormatoCombinaciones.Dzp));
    }
}
