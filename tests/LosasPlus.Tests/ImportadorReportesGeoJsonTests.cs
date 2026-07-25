using System.Globalization;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del importador de reportes GeoJSON (Fase N.1): puntos WGS84 de
/// IncidenciasRD proyectados al plano local del proyecto con
/// <see cref="Georreferencia.ALocal"/> — el gemelo digital empieza a hablar en
/// ambas direcciones.
/// </summary>
public class ImportadorReportesGeoJsonTests
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

    private static string Inv(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string PuntoFeature(double lat, double lon, string props = "{}") =>
        "{ \"type\": \"Feature\", \"geometry\": { \"type\": \"Point\", \"coordinates\": [" +
        Inv(lon) + ", " + Inv(lat) + "] }, \"properties\": " + props + " }";

    private static string Coleccion(params string[] features) =>
        "{ \"type\": \"FeatureCollection\", \"features\": [" + string.Join(",", features) + "] }";

    /// <summary>GeoJSON con un reporte en el punto local (x, y) del proyecto.</summary>
    private static string ReporteEn(double x, double y, string props = "{}")
    {
        var (lat, lon) = Origen().AGeografico(x, y);
        return Coleccion(PuntoFeature(lat, lon, props));
    }

    [Fact]
    public void Convierte_un_punto_wgs84_al_plano_local()
    {
        var reportes = ImportadorReportesGeoJson.Importar(ReporteEn(10.0, 20.0), Origen());

        var r = Assert.Single(reportes);
        Assert.Equal(10.0, r.XLocal, 3);
        Assert.Equal(20.0, r.YLocal, 3);
    }

    [Fact]
    public void Lee_las_propiedades_del_reporte_con_alias()
    {
        // IncidenciasRD emite title/category/status; también aceptamos las
        // variantes en español para archivos preparados a mano.
        string props = "{ \"title\": \"Bache profundo\", \"category\": \"bache\", \"status\": \"in_progress\" }";

        var r = ImportadorReportesGeoJson.Importar(ReporteEn(0, 0, props), Origen()).Single();

        Assert.Equal("Bache profundo", r.Titulo);
        Assert.Equal("bache", r.Categoria);
        Assert.Equal("in_progress", r.Estado);
    }

    [Fact]
    public void Lee_las_propiedades_en_espanol()
    {
        string props = "{ \"titulo\": \"Fuga de agua\", \"categoria\": \"fuga\", \"estado\": \"resolved\" }";

        var r = ImportadorReportesGeoJson.Importar(ReporteEn(0, 0, props), Origen()).Single();

        Assert.Equal("Fuga de agua", r.Titulo);
        Assert.Equal("fuga", r.Categoria);
        Assert.Equal("resolved", r.Estado);
    }

    [Fact]
    public void Usa_description_como_titulo_cuando_no_hay_title()
    {
        // El export real de IncidenciasRD (/api/export/geojson) no trae
        // 'title': el texto visible del reporte viaja en 'description'.
        string props = "{ \"description\": \"Bache frente al colmado\", \"category\": \"bache\" }";

        var r = ImportadorReportesGeoJson.Importar(ReporteEn(0, 0, props), Origen()).Single();

        Assert.Equal("Bache frente al colmado", r.Titulo);
    }

    [Fact]
    public void Filtra_por_radio_desde_el_origen_local()
    {
        var (latLejos, lonLejos) = Origen().AGeografico(5000.0, 0.0);
        string dos = Coleccion(
            PuntoFeature(LatOrigen, LonOrigen),
            PuntoFeature(latLejos, lonLejos));

        var cercanos = ImportadorReportesGeoJson.Importar(dos, Origen(), radioMetros: 500.0);

        Assert.Single(cercanos);
        Assert.True(System.Math.Abs(cercanos[0].XLocal) < 1.0);
    }

    [Fact]
    public void Ignora_features_que_no_son_puntos()
    {
        string mixto = Coleccion(
            PuntoFeature(LatOrigen, LonOrigen),
            "{ \"type\": \"Feature\", \"geometry\": { \"type\": \"Polygon\", " +
            "\"coordinates\": [[[0,0],[1,0],[1,1],[0,0]]] }, \"properties\": {} }");

        Assert.Single(ImportadorReportesGeoJson.Importar(mixto, Origen()));
    }

    [Fact]
    public void Sin_georreferencia_lanza_excepcion()
    {
        Assert.Throws<ImportadorReportesException>(
            () => ImportadorReportesGeoJson.Importar(ReporteEn(0, 0), null!));
    }

    [Fact]
    public void Json_invalido_lanza_excepcion_clara()
    {
        Assert.Throws<ImportadorReportesException>(
            () => ImportadorReportesGeoJson.Importar("esto no es json", Origen()));
    }
}
