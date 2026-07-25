using System;
using System.Collections.Generic;
using System.Text.Json;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Excepción al importar reportes GeoJSON inválidos o sin georreferencia.</summary>
public sealed class ImportadorReportesException : Exception
{
    public ImportadorReportesException(string mensaje) : base(mensaje) { }
    public ImportadorReportesException(string mensaje, Exception inner) : base(mensaje, inner) { }
}

/// <summary>
/// Un reporte de IncidenciasRD ya proyectado al plano local del proyecto:
/// coordenadas en metros desde el origen de la <see cref="Georreferencia"/>.
/// </summary>
public sealed record ReporteEnPlanta(
    double XLocal,
    double YLocal,
    double Latitud,
    double Longitud,
    string Titulo,
    string Categoria,
    string Estado);

/// <summary>
/// Importa reportes de <b>IncidenciasRD</b> (GeoJSON, Points WGS84) y los
/// proyecta al plano local con <see cref="Georreferencia.ALocal"/> — Fase N.1:
/// el gemelo digital habla en ambas direcciones. Función pura, sin I/O.
///
/// <para>
/// Acepta cualquier FeatureCollection de puntos (el mapa de IncidenciasRD,
/// QGIS, o un archivo hecho a mano); las features que no son <c>Point</c> se
/// ignoran en silencio. Propiedades con alias inglés/español
/// (<c>title/titulo</c>, <c>category/categoria</c>, <c>status/estado</c>).
/// </para>
/// </summary>
public static class ImportadorReportesGeoJson
{
    public static List<ReporteEnPlanta> Importar(
        string geojson, Georreferencia georreferencia, double? radioMetros = null)
    {
        if (georreferencia is null)
            throw new ImportadorReportesException(
                "El proyecto no está georreferenciado: ubícalo en Datos generales antes de importar reportes.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(geojson); }
        catch (JsonException ex)
        {
            throw new ImportadorReportesException("El archivo no es GeoJSON válido: " + ex.Message, ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
                throw new ImportadorReportesException(
                    "El GeoJSON no tiene 'features': se espera una FeatureCollection de puntos.");

            var reportes = new List<ReporteEnPlanta>();
            foreach (var f in features.EnumerateArray())
            {
                // El GeoJSON puede venir de un servidor remoto (modo http):
                // cualquier malformación estructural sale como
                // ImportadorReportesException, nunca como KeyNotFound/Invalid-
                // Operation que crashearía el handler async void de la UI.
                if (f.ValueKind != JsonValueKind.Object
                    || !f.TryGetProperty("geometry", out var g)
                    || g.ValueKind != JsonValueKind.Object
                    || !g.TryGetProperty("type", out var gt)
                    || gt.ValueKind != JsonValueKind.String)
                    throw new ImportadorReportesException(
                        "GeoJSON malformado: cada feature necesita geometry.type.");

                if (gt.GetString() != "Point")
                    continue;   // polígonos, líneas: no son reportes puntuales

                if (!g.TryGetProperty("coordinates", out var coords)
                    || coords.ValueKind != JsonValueKind.Array
                    || coords.GetArrayLength() < 2
                    || coords[0].ValueKind != JsonValueKind.Number
                    || coords[1].ValueKind != JsonValueKind.Number)
                    throw new ImportadorReportesException(
                        "GeoJSON malformado: un Point necesita coordinates [lon, lat] numéricas.");

                double lon = coords[0].GetDouble();
                double lat = coords[1].GetDouble();

                var (x, y) = georreferencia.ALocal(lat, lon);
                if (radioMetros is double radio && Math.Sqrt(x * x + y * y) > radio)
                    continue;

                f.TryGetProperty("properties", out var props);
                reportes.Add(new ReporteEnPlanta(
                    x, y, lat, lon,
                    Prop(props, "title", "titulo", "nombre", "description", "descripcion"),
                    Prop(props, "category", "categoria"),
                    Prop(props, "status", "estado")));
            }
            return reportes;
        }
    }

    private static string Prop(JsonElement props, params string[] alias)
    {
        if (props.ValueKind != JsonValueKind.Object) return "";
        foreach (var a in alias)
            if (props.TryGetProperty(a, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        return "";
    }
}
