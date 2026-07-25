using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la fuente de reportes de IncidenciasRD (Fase N.2): patrón
/// mock-first del ecosistema (<c>REPORTES_MODE</c> = mock | http). El modo
/// http consume <c>GET /api/export/geojson</c> del backend real.
/// </summary>
public class FuenteReportesTests
{
    private static Georreferencia Origen() => new()
    {
        Latitud = 18.4700,
        Longitud = -69.9400,
        Elevacion = 25.0,
    };

    // ---- Mock ----

    [Fact]
    public async Task El_mock_genera_reportes_que_caen_cerca_del_origen()
    {
        var geo = Origen();
        string json = await new FuenteReportesMock(geo).ObtenerGeoJsonAsync();

        var reportes = ImportadorReportesGeoJson.Importar(json, geo, radioMetros: 100.0);

        Assert.True(reportes.Count >= 3, "el mock debe dar material de demo suficiente");
        Assert.All(reportes, r => Assert.True(
            Math.Abs(r.XLocal) < 100 && Math.Abs(r.YLocal) < 100));
        Assert.Contains(reportes, r => r.Estado == "resolved");
        Assert.All(reportes, r => Assert.False(string.IsNullOrEmpty(r.Titulo)));
    }

    // ---- Factory (REPORTES_MODE) ----

    [Fact]
    public void Sin_variables_de_entorno_la_factory_da_el_mock()
    {
        ConEntorno(null, null, () =>
            Assert.IsType<FuenteReportesMock>(FuenteReportesFactory.Crear(Origen())));
    }

    [Fact]
    public void Modo_http_sin_url_lanza_excepcion_clara()
    {
        ConEntorno("http", null, () =>
        {
            var ex = Assert.Throws<ImportadorReportesException>(
                () => FuenteReportesFactory.Crear(Origen()));
            Assert.Contains("INCIDENCIAS_API_URL", ex.Message);
        });
    }

    [Fact]
    public void Modo_http_con_url_da_la_fuente_http()
    {
        ConEntorno("http", "http://localhost:8000", () =>
        {
            var fuente = FuenteReportesFactory.Crear(Origen());
            Assert.IsType<FuenteReportesHttp>(fuente);
            Assert.Contains("localhost:8000", fuente.Descripcion);
        });
    }

    private static void ConEntorno(string? modo, string? url, Action prueba)
    {
        string? modoPrevio = Environment.GetEnvironmentVariable("REPORTES_MODE");
        string? urlPrevia = Environment.GetEnvironmentVariable("INCIDENCIAS_API_URL");
        try
        {
            Environment.SetEnvironmentVariable("REPORTES_MODE", modo);
            Environment.SetEnvironmentVariable("INCIDENCIAS_API_URL", url);
            prueba();
        }
        finally
        {
            Environment.SetEnvironmentVariable("REPORTES_MODE", modoPrevio);
            Environment.SetEnvironmentVariable("INCIDENCIAS_API_URL", urlPrevia);
        }
    }

    // ---- Http ----

    private sealed class StubHandler : HttpMessageHandler
    {
        public Uri? UltimaUri;
        private readonly string _respuesta;
        public StubHandler(string respuesta) => _respuesta = respuesta;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            UltimaUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_respuesta),
            });
        }
    }

    [Fact]
    public async Task La_fuente_http_llama_al_endpoint_de_export_geojson()
    {
        var stub = new StubHandler("{ \"type\": \"FeatureCollection\", \"features\": [] }");
        var fuente = new FuenteReportesHttp(new HttpClient(stub), "http://localhost:8000");

        string json = await fuente.ObtenerGeoJsonAsync();

        Assert.NotNull(stub.UltimaUri);
        Assert.Equal("/api/export/geojson", stub.UltimaUri!.AbsolutePath);
        Assert.Contains("FeatureCollection", json);
    }

    [Fact]
    public async Task La_fuente_http_tolera_base_url_con_barra_final()
    {
        var stub = new StubHandler("{ \"type\": \"FeatureCollection\", \"features\": [] }");
        var fuente = new FuenteReportesHttp(new HttpClient(stub), "http://localhost:8000/");

        await fuente.ObtenerGeoJsonAsync();

        Assert.Equal("/api/export/geojson", stub.UltimaUri!.AbsolutePath);
    }
}
