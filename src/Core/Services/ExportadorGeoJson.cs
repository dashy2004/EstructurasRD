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
    /// <summary>Altura de entrepiso cuando no hay nivel de referencia (típica residencial RD).</summary>
    public const double AlturaEntrepisoDefecto = 3.0;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string Exportar(Edificio edificio, Georreferencia georreferencia)
    {
        if (georreferencia is null)
            throw new ExportadorModeloException(
                "El proyecto no está georreferenciado: ubícalo en Datos generales antes de exportar GeoJSON.");

        // Altura de extrusión por nivel (M.2a): del piso a la cota del nivel
        // siguiente; el tope hereda la altura del piso anterior (3.0 m si no hay).
        var niveles = new List<Nivel>(edificio.Niveles);
        niveles.Sort((a, b) => a.Cota.CompareTo(b.Cota));
        var alturaPorNivel = new Dictionary<Nivel, double>();
        for (int i = 0; i < niveles.Count; i++)
        {
            double altura = i + 1 < niveles.Count
                ? niveles[i + 1].Cota - niveles[i].Cota
                : (i > 0 ? alturaPorNivel[niveles[i - 1]] : AlturaEntrepisoDefecto);
            alturaPorNivel[niveles[i]] = altura > 0 ? altura : AlturaEntrepisoDefecto;
        }

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
                            BaseHeight = nivel.Cota,
                            Height = nivel.Cota + alturaPorNivel[nivel],
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

    /// <summary>Base y tope del volumen del piso en metros sobre el terreno —
    /// la convención de extrusión que leen Cesium, Mapbox GL y deck.gl (M.2a).</summary>
    [JsonPropertyName("base_height")] public double BaseHeight { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
}
