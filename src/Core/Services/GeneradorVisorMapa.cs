namespace LosasPlus.Services;

/// <summary>
/// Genera visores HTML <b>autocontenidos</b> del edificio extruido (Fase M.2b):
/// el GeoJSON de <see cref="ExportadorGeoJson"/> va embebido inline (con
/// <c>file://</c> un fetch muere por CORS; inline abre con doble click).
/// MapLibre extruye directo con <c>fill-extrusion</c> desde
/// <c>base_height</c>/<c>height</c> (convención M.2a).
///
/// <para>Función pura, sin I/O — mismo patrón que <see cref="ExportadorGeoJson"/>.</para>
/// </summary>
public static class GeneradorVisorMapa
{
    public static string GenerarMapLibre(string geojson)
    {
        Validar(geojson);
        return $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <title>EstructurasRD — visor 3D (MapLibre)</title>
            <script src="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.js"
                    integrity="sha384-SYKAG6cglRMN0RVvhNeBY0r3FYKNOJtznwA0v7B5Vp9tr31xAHsZC0DqkQ/pZDmj"
                    crossorigin="anonymous"></script>
            <link href="https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.css" rel="stylesheet"
                  integrity="sha384-MinO0mNliZ3vwppuPOUnGa+iq619pfMhLVUXfC4LHwSCvF9H+6P/KO4Q7qBOYV5V"
                  crossorigin="anonymous">
            <style>html,body,#mapa{margin:0;height:100%}</style>
            </head>
            <body>
            <div id="mapa"></div>
            <script>
            const GEOJSON = {{Empotrar(geojson)}};
            const coords = GEOJSON.features.flatMap(f => f.geometry.coordinates[0]);
            const lons = coords.map(c => c[0]), lats = coords.map(c => c[1]);
            const map = new maplibregl.Map({
              container: 'mapa',
              style: {
                version: 8,
                sources: { osm: { type: 'raster',
                  tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                  tileSize: 256, attribution: '© OpenStreetMap' } },
                layers: [{ id: 'osm', type: 'raster', source: 'osm' }]
              },
              bounds: [[Math.min(...lons), Math.min(...lats)], [Math.max(...lons), Math.max(...lats)]],
              fitBoundsOptions: { padding: 120 },
              pitch: 60, bearing: -20
            });
            map.on('load', () => {
              map.addSource('edificio', { type: 'geojson', data: GEOJSON });
              map.addLayer({
                id: 'extrusion', type: 'fill-extrusion', source: 'edificio',
                paint: {
                  'fill-extrusion-base': ['get', 'base_height'],
                  'fill-extrusion-height': ['get', 'height'],
                  'fill-extrusion-color': '#e07b39',
                  'fill-extrusion-opacity': 0.9
                }
              });
            });
            </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Visor CesiumJS: globo con imagery OSM <b>sin token</b>. El campo de token
    /// de Cesium ion es opcional y solo activa el terreno con relieve (World
    /// Terrain) — puerta a 3D Tiles en M.3. La librería CesiumJS es open source
    /// y no lo necesita.
    /// </summary>
    public static string GenerarCesium(string geojson)
    {
        Validar(geojson);
        return $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <title>EstructurasRD — visor 3D (Cesium)</title>
            <script src="https://cesium.com/downloads/cesiumjs/releases/1.119/Build/Cesium/Cesium.js"
                    integrity="sha384-19K1W/rKcjyRWzj4BzWH4GVmRVA8OnzNgQ33/vVuXFrRqgZ+Zcm5BNPzyrsYisGU"
                    crossorigin="anonymous"></script>
            <link href="https://cesium.com/downloads/cesiumjs/releases/1.119/Build/Cesium/Widgets/widgets.css" rel="stylesheet"
                  integrity="sha384-ghEeMdcWWzRv/BPeUcX835vcKDGrxvROXisl/Btpv3GeekBUXTSPVcFJpI1Tcrgp"
                  crossorigin="anonymous">
            <style>
            html,body,#globo{margin:0;height:100%}
            #panelToken{position:absolute;top:10px;left:10px;z-index:1;background:#fff;
                        padding:6px 10px;border-radius:4px;font:12px sans-serif}
            </style>
            </head>
            <body>
            <div id="globo"></div>
            <div id="panelToken">
              Token Cesium ion (opcional, activa terreno):
              <input id="token" size="32" placeholder="pegar token...">
              <button onclick="activarTerreno()">Activar</button>
            </div>
            <script>
            const GEOJSON = {{Empotrar(geojson)}};
            const visor = new Cesium.Viewer('globo', {
              baseLayer: new Cesium.ImageryLayer(new Cesium.OpenStreetMapImageryProvider(
                { url: 'https://tile.openstreetmap.org/' })),
              baseLayerPicker: false, geocoder: false, animation: false,
              timeline: false, sceneModePicker: false
            });
            Cesium.GeoJsonDataSource.load(GEOJSON).then(ds => {
              for (const ent of ds.entities.values) {
                const p = ent.properties;
                ent.polygon.height = p.base_height.getValue();
                ent.polygon.extrudedHeight = p.height.getValue();
                ent.polygon.material = Cesium.Color.fromCssColorString('#e07b39');
                ent.polygon.outline = true;
                ent.polygon.outlineColor = Cesium.Color.BLACK;
              }
              visor.dataSources.add(ds);
              visor.flyTo(ds);
            });
            async function activarTerreno() {
              const t = document.getElementById('token').value.trim();
              if (!t) return;
              Cesium.Ion.defaultAccessToken = t;
              visor.terrainProvider = await Cesium.createWorldTerrainAsync();
            }
            </script>
            </body>
            </html>
            """;
    }

    /// <summary>Escapa <c>&lt;/</c> para que ningún texto del GeoJSON pueda cerrar el script.</summary>
    private static string Empotrar(string geojson) => geojson.Replace("</", "<\\/");

    private static void Validar(string geojson)
    {
        if (string.IsNullOrWhiteSpace(geojson))
            throw new ExportadorModeloException(
                "No hay GeoJSON que embeber en el visor: exporta primero el edificio georreferenciado.");
    }
}
