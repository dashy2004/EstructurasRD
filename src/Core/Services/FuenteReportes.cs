using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Fuente de reportes de IncidenciasRD en GeoJSON (Fase N.2). Patrón
/// <b>mock-first</b> del ecosistema: la app corre end-to-end sin backend
/// encendido, y el modo real se activa por entorno.
///
/// <para>
/// <c>REPORTES_MODE</c> = <c>mock</c> (default) | <c>http</c>.
/// En http, <c>INCIDENCIAS_API_URL</c> apunta al backend (p. ej.
/// <c>http://localhost:8000</c>) y se consume <c>GET /api/export/geojson</c>
/// (endpoint público con rate-limit de invitado).
/// </para>
/// </summary>
public interface IFuenteReportes
{
    /// <summary>De dónde vienen los datos ("mock" o la URL base) — para el status de la UI.</summary>
    string Descripcion { get; }

    Task<string> ObtenerGeoJsonAsync(CancellationToken ct = default);
}

/// <summary>
/// Reportes de demostración alrededor del origen del proyecto: material
/// suficiente para ver la capa de pines sin IncidenciasRD encendido.
/// </summary>
public sealed class FuenteReportesMock : IFuenteReportes
{
    private readonly Georreferencia _geo;

    public FuenteReportesMock(Georreferencia geo) => _geo = geo;

    public string Descripcion => "mock";

    public Task<string> ObtenerGeoJsonAsync(CancellationToken ct = default)
    {
        // Reportes típicos de la taxonomía RD, a metros del origen local.
        (double x, double y, string titulo, string cat, string estado)[] demo =
        {
            (8.0, 5.0, "Bache profundo en la vía", "calzada_infraestructura", "in_progress"),
            (-12.0, 14.0, "Filtración en acera", "aceras_peatonales", "received"),
            (20.0, -9.0, "Luminaria apagada", "servicios_electricos", "resolved"),
            (3.0, -16.0, "Imbornal obstruido", "alcantarillado_pluvial", "received"),
        };

        var features = new System.Text.StringBuilder();
        for (int i = 0; i < demo.Length; i++)
        {
            var (x, y, titulo, cat, estado) = demo[i];
            var (lat, lon) = _geo.AGeografico(x, y);
            if (i > 0) features.Append(',');
            features.Append(
                "{ \"type\": \"Feature\", \"geometry\": { \"type\": \"Point\", \"coordinates\": [" +
                lon.ToString("R", CultureInfo.InvariantCulture) + ", " +
                lat.ToString("R", CultureInfo.InvariantCulture) + "] }, \"properties\": { " +
                "\"title\": \"" + titulo + "\", \"category\": \"" + cat + "\", " +
                "\"status\": \"" + estado + "\" } }");
        }

        return Task.FromResult(
            "{ \"type\": \"FeatureCollection\", \"features\": [" + features + "] }");
    }
}

/// <summary>Consume el endpoint real <c>GET /api/export/geojson</c> de IncidenciasRD.</summary>
public sealed class FuenteReportesHttp : IFuenteReportes
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public FuenteReportesHttp(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public string Descripcion => _baseUrl;

    public async Task<string> ObtenerGeoJsonAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetStringAsync(_baseUrl + "/api/export/geojson", ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ImportadorReportesException(
                $"No se pudo consultar IncidenciasRD en {_baseUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new ImportadorReportesException(
                $"IncidenciasRD en {_baseUrl} no respondió a tiempo.", ex);
        }
    }
}

/// <summary>Resuelve la fuente según el entorno (mock-first).</summary>
public static class FuenteReportesFactory
{
    private static readonly HttpClient _httpCompartido = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public static IFuenteReportes Crear(Georreferencia geo)
    {
        string modo = Environment.GetEnvironmentVariable("REPORTES_MODE")?.Trim().ToLowerInvariant() ?? "mock";
        if (modo is "" or "mock")
            return new FuenteReportesMock(geo);

        if (modo != "http")
            throw new ImportadorReportesException(
                $"REPORTES_MODE '{modo}' no reconocido: usa 'mock' o 'http'.");

        string? url = Environment.GetEnvironmentVariable("INCIDENCIAS_API_URL");
        if (string.IsNullOrWhiteSpace(url))
            throw new ImportadorReportesException(
                "REPORTES_MODE=http requiere INCIDENCIAS_API_URL (p. ej. http://localhost:8000).");

        return new FuenteReportesHttp(_httpCompartido, url);
    }
}
