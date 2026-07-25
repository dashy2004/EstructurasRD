using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Exporta el edificio georreferenciado como <b>GeoJSON</b> (RFC 7946): una
/// FeatureCollection donde cada losa es un Polygon WGS84 con altitud
/// (elevación del origen + cota del nivel). Fase M.1 del mapa 3D urbano.
///
/// <para>
/// Es el primer consumidor real de <see cref="Georreferencia.AGeografico"/> y
/// el formato puente hacia el ecosistema: lo leen el Leaflet de IncidenciasRD,
/// QGIS y CesiumJS (del que saldrán los 3D Tiles en fases M posteriores).
/// </para>
///
/// <para>Función pura, sin I/O — mismo patrón que <see cref="ExportadorModeloMotor"/>.</para>
/// </summary>
public static class ExportadorGeoJson
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string Exportar(Edificio edificio, Georreferencia georreferencia)
    {
        if (georreferencia is null)
            throw new ExportadorModeloException(
                "El proyecto no está georreferenciado: ubícalo en Datos generales antes de exportar GeoJSON.");

        var features = new List<FeatureGeoJson>();
        foreach (var nivel in edificio.Niveles)
            foreach (var sistema in nivel.Sistemas)
                foreach (var losa in sistema.Losas)
                {
                    double x0 = losa.CoordenadaX, y0 = losa.CoordenadaY;
                    double x1 = x0 + losa.Lx, y1 = y0 + losa.Ly;
                    double alt = georreferencia.Elevacion + nivel.Cota;

                    // Anillo exterior antihorario y cerrado (RFC 7946 §3.1.6).
                    var anillo = new[]
                    {
                        Punto(georreferencia, x0, y0, alt),
                        Punto(georreferencia, x1, y0, alt),
                        Punto(georreferencia, x1, y1, alt),
                        Punto(georreferencia, x0, y1, alt),
                        Punto(georreferencia, x0, y0, alt),
                    };

                    features.Add(new FeatureGeoJson
                    {
                        Geometry = new GeometriaGeoJson { Coordinates = new[] { anillo } },
                        Properties = new PropiedadesGeoJson
                        {
                            Tipo = "losa",
                            Nivel = nivel.Nombre,
                            Cota = nivel.Cota,
                        },
                    });
                }

        if (features.Count == 0)
            throw new ExportadorModeloException(
                "El edificio no tiene losas que exportar: el GeoJSON quedaría vacío.");

        return JsonSerializer.Serialize(new FeatureCollectionGeoJson { Features = features }, JsonOpts);
    }

    private static double[] Punto(Georreferencia geo, double x, double y, double alt)
    {
        var (lat, lon) = geo.AGeografico(x, y);
        return new[] { lon, lat, alt };   // GeoJSON: [lon, lat, alt]
    }
}

public sealed class FeatureCollectionGeoJson
{
    [JsonPropertyName("type")] public string Type { get; set; } = "FeatureCollection";
    [JsonPropertyName("features")] public List<FeatureGeoJson> Features { get; set; } = new();
}

public sealed class FeatureGeoJson
{
    [JsonPropertyName("type")] public string Type { get; set; } = "Feature";
    [JsonPropertyName("geometry")] public GeometriaGeoJson Geometry { get; set; } = new();
    [JsonPropertyName("properties")] public PropiedadesGeoJson Properties { get; set; } = new();
}

public sealed class GeometriaGeoJson
{
    [JsonPropertyName("type")] public string Type { get; set; } = "Polygon";
    [JsonPropertyName("coordinates")] public double[][][] Coordinates { get; set; }
        = System.Array.Empty<double[][]>();
}

public sealed class PropiedadesGeoJson
{
    [JsonPropertyName("tipo")] public string Tipo { get; set; } = "";
    [JsonPropertyName("nivel")] public string Nivel { get; set; } = "";
    [JsonPropertyName("cota")] public double Cota { get; set; }
}
