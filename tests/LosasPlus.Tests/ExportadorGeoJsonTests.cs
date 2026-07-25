using System.Text.Json;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del exportador GeoJSON (Fase M.1): el edificio georreferenciado como
/// FeatureCollection — cada losa es un Polygon WGS84 con altitud. Es el primer
/// consumidor real de <see cref="Georreferencia.AGeografico"/> y el formato que
/// leen tanto el Leaflet de IncidenciasRD como CesiumJS (mapa 3D urbano).
/// </summary>
public class ExportadorGeoJsonTests
{
    private const double LatOrigen = 18.4700;   // Santo Domingo
    private const double LonOrigen = -69.9400;

    private static Georreferencia Origen() => new()
    {
        Latitud = LatOrigen,
        Longitud = LonOrigen,
        Elevacion = 25.0,
        RotacionNorte = 0.0,
    };

    private static Edificio EdificioConLosa()
    {
        var nivel = new Nivel { Nombre = "Nivel 2", Cota = 3.0 };
        var sistema = new Sistema();
        sistema.Losas.Add(new Losa { CoordenadaX = 10.0, CoordenadaY = 20.0, Lx = 5.0, Ly = 4.0 });
        nivel.Sistemas.Add(sistema);
        var ed = new Edificio();
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Sin_georreferencia_lanza_excepcion()
    {
        Assert.Throws<ExportadorModeloException>(
            () => ExportadorGeoJson.Exportar(EdificioConLosa(), null!));
    }

    [Fact]
    public void Sin_losas_lanza_excepcion()
    {
        var vacio = new Edificio();
        vacio.Niveles.Add(new Nivel { Cota = 0.0 });

        Assert.Throws<ExportadorModeloException>(
            () => ExportadorGeoJson.Exportar(vacio, Origen()));
    }

    [Fact]
    public void Una_losa_se_exporta_como_poligono_wgs84_cerrado()
    {
        string json = ExportadorGeoJson.Exportar(EdificioConLosa(), Origen());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("FeatureCollection", root.GetProperty("type").GetString());

        var features = root.GetProperty("features");
        Assert.Equal(1, features.GetArrayLength());

        var feature = features[0];
        Assert.Equal("Feature", feature.GetProperty("type").GetString());

        var geom = feature.GetProperty("geometry");
        Assert.Equal("Polygon", geom.GetProperty("type").GetString());

        // Un anillo exterior de 5 puntos (cerrado: primero == último).
        var anillo = geom.GetProperty("coordinates")[0];
        Assert.Equal(5, anillo.GetArrayLength());
        Assert.Equal(anillo[0].GetRawText(), anillo[4].GetRawText());

        // GeoJSON manda [lon, lat, alt] — la esquina (10, 20) local con AGeografico.
        var (latEsq, lonEsq) = Origen().AGeografico(10.0, 20.0);
        Assert.Equal(lonEsq, anillo[0][0].GetDouble(), 9);
        Assert.Equal(latEsq, anillo[0][1].GetDouble(), 9);
        Assert.Equal(25.0 + 3.0, anillo[0][2].GetDouble(), 9);   // elevación + cota
    }

    [Fact]
    public void El_anillo_recorre_las_esquinas_en_sentido_antihorario()
    {
        // RFC 7946: el anillo exterior va CCW. Con +X→Este y +Y→Norte, el
        // recorrido (x0,y0)→(x1,y0)→(x1,y1)→(x0,y1) ya es antihorario.
        string json = ExportadorGeoJson.Exportar(EdificioConLosa(), Origen());

        using var doc = JsonDocument.Parse(json);
        var anillo = doc.RootElement.GetProperty("features")[0]
            .GetProperty("geometry").GetProperty("coordinates")[0];

        var (_, lonEsq0) = Origen().AGeografico(10.0, 20.0);
        var (_, lonEsq1) = Origen().AGeografico(15.0, 20.0);
        var (latEsq2, _) = Origen().AGeografico(15.0, 24.0);

        Assert.Equal(lonEsq1, anillo[1][0].GetDouble(), 9);   // →Este
        Assert.Equal(latEsq2, anillo[2][1].GetDouble(), 9);   // →Norte
        Assert.Equal(lonEsq0, anillo[3][0].GetDouble(), 9);   // →Oeste
    }

    [Fact]
    public void Las_propiedades_llevan_nivel_y_cota()
    {
        string json = ExportadorGeoJson.Exportar(EdificioConLosa(), Origen());

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");

        Assert.Equal("losa", props.GetProperty("tipo").GetString());
        Assert.Equal("Nivel 2", props.GetProperty("nivel").GetString());
        Assert.Equal(3.0, props.GetProperty("cota").GetDouble(), 9);
    }

    private static Edificio EdificioDosNiveles()
    {
        var ed = new Edificio();
        foreach (var (nombre, cota) in new[] { ("Nivel 1", 0.0), ("Nivel 2", 3.2) })
        {
            var nivel = new Nivel { Nombre = nombre, Cota = cota };
            var sistema = new Sistema();
            sistema.Losas.Add(new Losa { CoordenadaX = 0, CoordenadaY = 0, Lx = 5, Ly = 4 });
            nivel.Sistemas.Add(sistema);
            ed.Niveles.Add(nivel);
        }
        return ed;
    }

    [Fact]
    public void Cada_losa_lleva_base_height_y_height_para_extrusion()
    {
        // M.2a: convención de extrusión de Cesium/Mapbox/deck.gl — el volumen
        // del piso va de base_height a height (metros sobre el terreno).
        string json = ExportadorGeoJson.Exportar(EdificioDosNiveles(), Origen());

        using var doc = JsonDocument.Parse(json);
        var features = doc.RootElement.GetProperty("features");
        Assert.Equal(2, features.GetArrayLength());

        var p1 = features[0].GetProperty("properties");
        Assert.Equal(0.0, p1.GetProperty("base_height").GetDouble(), 9);
        Assert.Equal(3.2, p1.GetProperty("height").GetDouble(), 9);
    }

    [Fact]
    public void El_ultimo_nivel_se_extruye_con_la_altura_del_piso_anterior()
    {
        string json = ExportadorGeoJson.Exportar(EdificioDosNiveles(), Origen());

        using var doc = JsonDocument.Parse(json);
        var pTope = doc.RootElement.GetProperty("features")[1].GetProperty("properties");

        Assert.Equal(3.2, pTope.GetProperty("base_height").GetDouble(), 9);
        Assert.Equal(6.4, pTope.GetProperty("height").GetDouble(), 9);
    }

    [Fact]
    public void Un_edificio_de_un_solo_nivel_se_extruye_con_altura_por_defecto()
    {
        // Sin piso anterior de referencia: 3.0 m — la altura de entrepiso
        // típica residencial RD.
        string json = ExportadorGeoJson.Exportar(EdificioConLosa(), Origen());

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");

        Assert.Equal(3.0, props.GetProperty("base_height").GetDouble(), 9);
        Assert.Equal(6.0, props.GetProperty("height").GetDouble(), 9);
    }
}
