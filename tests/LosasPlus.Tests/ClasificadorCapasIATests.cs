using System.Collections.Generic;
using LosasPlus.IA;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Núcleo PURO del clasificador de capas por IA de texto (sin red): que arme el
/// contenido con las capas, que parsee el JSON capa→categoría tolerando
/// texto/markdown, y que la combinación heurística+IA respete la prioridad
/// (la heurística manda; la IA solo rellena lo ambiguo).
/// </summary>
public class ClasificadorCapasIATests
{
    [Fact]
    public void ConstruirContenido_incluye_las_capas_y_la_instruccion()
    {
        var c = ClasificadorCapasIA.ConstruirContenido(new[] { "P1", "ENTREPISO" });

        Assert.Contains("P1", c);
        Assert.Contains("ENTREPISO", c);
        Assert.Contains("JSON", c);
    }

    [Fact]
    public void ParsearRespuesta_mapea_categorias()
    {
        var json = "{\"V-25x50\":\"viga\",\"P1\":\"columna\",\"ENTREPISO\":\"losa\",\"MOBILIARIO\":\"otro\"}";

        var m = ClasificadorCapasIA.ParsearRespuesta(json);

        Assert.Equal(CategoriaEstructural.Viga, m["V-25x50"]);
        Assert.Equal(CategoriaEstructural.Columna, m["P1"]);
        Assert.Equal(CategoriaEstructural.Losa, m["ENTREPISO"]);
        Assert.Equal(CategoriaEstructural.Otro, m["MOBILIARIO"]);
    }

    [Fact]
    public void ParsearRespuesta_tolera_texto_y_markdown()
    {
        var contenido = "Claro:\n```json\n{\"X\":\"columna\"}\n```\nlisto.";

        var m = ClasificadorCapasIA.ParsearRespuesta(contenido);

        Assert.Single(m);
        Assert.Equal(CategoriaEstructural.Columna, m["X"]);
    }

    [Fact]
    public void ParsearRespuesta_sin_json_no_crashea()
        => Assert.Empty(ClasificadorCapasIA.ParsearRespuesta("no hay json"));

    [Fact]
    public void CombinarConHeuristica_la_heuristica_tiene_prioridad()
    {
        // La IA dice (erróneamente) que "VIGAS" es columna; la heurística debe ganar.
        var ia = new Dictionary<string, CategoriaEstructural> { ["VIGAS"] = CategoriaEstructural.Columna };
        var f = ClasificadorCapasIA.CombinarConHeuristica(ia);

        Assert.Equal(CategoriaEstructural.Viga, f("VIGAS"));
    }

    [Fact]
    public void CombinarConHeuristica_la_IA_resuelve_lo_ambiguo()
    {
        // "P1" no lo reconoce la heurística (Otro) → se usa el veredicto de la IA.
        var ia = new Dictionary<string, CategoriaEstructural> { ["P1"] = CategoriaEstructural.Columna };
        var f = ClasificadorCapasIA.CombinarConHeuristica(ia);

        Assert.Equal(CategoriaEstructural.Columna, f("P1"));
        Assert.Equal(CategoriaEstructural.Otro, f("DESCONOCIDA"));   // ni heurística ni IA
    }
}
