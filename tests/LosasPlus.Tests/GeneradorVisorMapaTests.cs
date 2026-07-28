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
        Assert.Contains("maxzoom: 19", html);              // tiles OSM no existen más allá de z19
        Assert.Contains("maxZoom: 17.5", html);            // fitBounds no puede pasarse del tope
    }

    [Fact]
    public void Maplibre_contiene_contexto_urbano_openfreemap_con_filtro_de_huella()
    {
        string html = GeneradorVisorMapa.GenerarMapLibre(GeojsonDemo);

        Assert.Contains("tiles.openfreemap.org/planet", html);   // fuente vectorial sin token
        Assert.Contains("'source-layer': 'building'", html);     // capa building del esquema OpenMapTiles
        Assert.Contains("within", html);                          // filtro de huella
        Assert.Contains("coalesce", html);                        // alturas OSM ausentes → default
        Assert.Contains("render_height", html);
        Assert.Contains("#c9c4bc", html);                         // contexto gris neutro
        Assert.Contains("const huella", html);                    // bbox propio con margen
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

    [Fact]
    public void Cesium_contiene_datasource_extrusion_y_geojson_embebido()
    {
        string html = GeneradorVisorMapa.GenerarCesium(GeojsonDemo);

        Assert.Contains("Cesium.js", html);                 // CDN de la librería
        Assert.Contains("GeoJsonDataSource", html);
        Assert.Contains("extrudedHeight", html);            // extrusión por entidad
        Assert.Contains("OpenStreetMapImageryProvider", html); // imagery sin token
        Assert.Contains("const GEOJSON", html);
        Assert.Contains("-69.94", html);
        Assert.Contains("integrity=\"sha384-", html);       // SRI en el CDN
        Assert.Contains("crossorigin=\"anonymous\"", html);
    }

    [Fact]
    public void Cesium_no_lleva_token_hardcodeado()
    {
        string html = GeneradorVisorMapa.GenerarCesium(GeojsonDemo);

        // Los tokens ion son JWT ("eyJ..."); el HTML solo puede leerlo del input.
        Assert.DoesNotContain("eyJ", html);
        Assert.DoesNotContain("defaultAccessToken = \"", html);
    }

    [Fact]
    public void Cesium_escapa_cierre_de_script_y_valida_vacio()
    {
        string malicioso = GeojsonDemo.Replace("Nivel 1", "a</script>b");
        Assert.Contains("a<\\/script>b", GeneradorVisorMapa.GenerarCesium(malicioso));

        Assert.Throws<ExportadorModeloException>(() => GeneradorVisorMapa.GenerarCesium(""));
    }

    [Fact]
    public void Rutas_de_visores_derivan_del_geojson()
    {
        string ruta = System.IO.Path.Combine("salida", "edificio.geojson");

        var (mapLibre, cesium) = GeneradorVisorMapa.RutasVisores(ruta);

        Assert.Equal(System.IO.Path.Combine("salida", "edificio-visor-maplibre.html"), mapLibre);
        Assert.Equal(System.IO.Path.Combine("salida", "edificio-visor-cesium.html"), cesium);
    }
}
