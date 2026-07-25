using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del generador de visores 3D (Fase M.2b): HTML autocontenidos
/// (MapLibre GL / CesiumJS) con el GeoJSON extruido de M.2a embebido inline.
/// </summary>
public class GeneradorVisorMapaTests
{
    /// <summary>GeoJSON mínimo con las propiedades de extrusión de M.2a (Santo Domingo).</summary>
    private const string GeojsonDemo = """
        {"type":"FeatureCollection","features":[{"type":"Feature",
         "geometry":{"type":"Polygon","coordinates":[[[-69.94,18.47,25.0],[-69.9399,18.47,25.0],[-69.9399,18.4701,25.0],[-69.94,18.4701,25.0],[-69.94,18.47,25.0]]]},
         "properties":{"tipo":"losa","nivel":"Nivel 1","cota":0.0,"base_height":0.0,"height":3.0}}]}
        """;

    [Fact]
    public void Maplibre_contiene_capa_de_extrusion_y_geojson_embebido()
    {
        string html = GeneradorVisorMapa.GenerarMapLibre(GeojsonDemo);

        Assert.Contains("maplibre-gl", html);              // CDN de la librería
        Assert.Contains("fill-extrusion-base", html);      // consume base_height
        Assert.Contains("fill-extrusion-height", html);    // consume height
        Assert.Contains("base_height", html);
        Assert.Contains("const GEOJSON", html);            // embebido inline
        Assert.Contains("-69.94", html);                   // datos reales dentro
        Assert.Contains("openstreetmap.org", html);        // fondo OSM sin token
        Assert.Contains("integrity=\"sha384-", html);      // SRI en el CDN
        Assert.Contains("crossorigin=\"anonymous\"", html);
    }

    [Fact]
    public void Maplibre_escapa_cierre_de_script_del_geojson()
    {
        string malicioso = GeojsonDemo.Replace("Nivel 1", "a</script>b");

        string html = GeneradorVisorMapa.GenerarMapLibre(malicioso);

        Assert.Contains("a<\\/script>b", html);
        Assert.DoesNotContain("a</script>b", html);
    }

    [Fact]
    public void Maplibre_geojson_nulo_o_vacio_lanza_excepcion()
    {
        Assert.Throws<ExportadorModeloException>(() => GeneradorVisorMapa.GenerarMapLibre(null!));
        Assert.Throws<ExportadorModeloException>(() => GeneradorVisorMapa.GenerarMapLibre("  "));
    }
}
