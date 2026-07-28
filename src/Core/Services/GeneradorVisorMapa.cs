namespace LosasPlus.Services;

/// <summary>
/// Genera visores HTML <b>autocontenidos</b> del edificio extruido (Fase M.2b):
/// el GeoJSON de <see cref="ExportadorGeoJson"/> va embebido inline (con
/// <c>file://</c> un fetch muere por CORS; inline abre con doble click).
/// MapLibre extruye directo con <c>fill-extrusion</c> desde
/// <c>base_height</c>/<c>height</c> (convención M.2a), y añade contexto urbano
/// OpenFreeMap (M.3) con filtro de huella (bbox propio + margen ≈ 5 m).
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
            const M = 0.000045; // margen ~5 m alrededor del edificio propio
            const huella = { type: 'Polygon', coordinates: [[
              [Math.min(...lons) - M, Math.min(...lats) - M],
              [Math.max(...lons) + M, Math.min(...lats) - M],
              [Math.max(...lons) + M, Math.max(...lats) + M],
              [Math.min(...lons) - M, Math.max(...lats) + M],
              [Math.min(...lons) - M, Math.min(...lats) - M]
            ]] };
            const map = new maplibregl.Map({
              container: 'mapa',
              style: {
                version: 8,
                sources: { osm: { type: 'raster',
                  tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                  tileSize: 256, maxzoom: 19, attribution: '© OpenStreetMap' },
                openfreemap: { type: 'vector', url: 'https://tiles.openfreemap.org/planet' } },
                layers: [{ id: 'osm', type: 'raster', source: 'osm' }]
              },
              bounds: [[Math.min(...lons), Math.min(...lats)], [Math.max(...lons), Math.max(...lats)]],
              fitBoundsOptions: { padding: 120, maxZoom: 17.5 },
              maxZoom: 19,
              pitch: 60, bearing: -20
            });
            map.on('load', () => {
              map.addSource('edificio', { type: 'geojson', data: GEOJSON });
              // Capa propia primero: un fallo de contexto (fuente caída, filtro mal evaluado)
              // nunca debe impedir que se vea el entregable principal. Ambas son
              // fill-extrusion con depth test — el orden de adición no afecta lo visual.
              map.addLayer({
                id: 'extrusion', type: 'fill-extrusion', source: 'edificio',
                paint: {
                  'fill-extrusion-base': ['get', 'base_height'],
                  'fill-extrusion-height': ['get', 'height'],
                  'fill-extrusion-color': '#e07b39',
                  'fill-extrusion-opacity': 0.9
                }
              });
              map.addLayer({
                id: 'contexto', type: 'fill-extrusion',
                source: 'openfreemap', 'source-layer': 'building',
                // 'within' de MapLibre solo evalúa Point/LineString — en polígonos de 'building'
                // siempre devuelve false, así que ['!', ['within', huella]] nunca ocultaba nada.
                // 'distance' sí soporta polígonos: da la distancia en metros a 'huella', así que
                // > 0 oculta cualquier vecino que toque o se solape con la huella propia (mejora
                // sobre 'within': antes un vecino parcialmente contenido no se ocultaba).
                // hide_3d excluye el envolvente 2D duplicado de edificios con building:part
                // (si no, hay z-fighting entre el envolvente y sus partes).
                filter: ['all', ['>', ['distance', huella], 0], ['!=', ['get', 'hide_3d'], true]],
                paint: {
                  'fill-extrusion-base': ['coalesce', ['get', 'render_min_height'], 0],
                  'fill-extrusion-height': ['coalesce', ['get', 'render_height'], 8],
                  'fill-extrusion-color': '#c9c4bc',
                  'fill-extrusion-opacity': 0.85
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
    /// de Cesium ion es opcional y activa el terreno con relieve (World Terrain)
    /// más edificios OSM con filtro de huella (M.3 completada). La librería
    /// CesiumJS es open source y no lo necesita.
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
              Token Cesium ion (opcional, activa terreno + edificios OSM):
              <input id="token" size="32" placeholder="pegar token...">
              <button onclick="activarContexto()">Activar</button>
              <span id="estado"></span>
            </div>
            <script>
            const GEOJSON = {{Empotrar(geojson)}};
            const visor = new Cesium.Viewer('globo', {
              baseLayer: new Cesium.ImageryLayer(new Cesium.OpenStreetMapImageryProvider(
                { url: 'https://tile.openstreetmap.org/', maximumLevel: 19 })),
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
            let contextoActivo = false;
            async function activarContexto() {
              if (contextoActivo) return; // evita apilar otro tileset OSM Buildings en clicks repetidos
              const t = document.getElementById('token').value.trim();
              const estado = document.getElementById('estado');
              if (!t) { estado.textContent = ' pega un token ion primero'; return; }
              estado.textContent = ' cargando...';
              try {
                Cesium.Ion.defaultAccessToken = t;
                visor.terrainProvider = await Cesium.createWorldTerrainAsync();
                const edificiosOsm = await Cesium.createOsmBuildingsAsync();
                // huella del edificio propio (bbox + margen ~5 m): el contexto se oculta ahí
                const coords = GEOJSON.features.flatMap(f => f.geometry.coordinates[0]);
                const lons = coords.map(c => c[0]), lats = coords.map(c => c[1]);
                const M = 0.000045;
                const enHuella =
                  "(${feature['cesium#latitude']} >= " + (Math.min(...lats) - M) +
                  " && ${feature['cesium#latitude']} <= " + (Math.max(...lats) + M) +
                  " && ${feature['cesium#longitude']} >= " + (Math.min(...lons) - M) +
                  " && ${feature['cesium#longitude']} <= " + (Math.max(...lons) + M) + ")";
                edificiosOsm.style = new Cesium.Cesium3DTileStyle({ show: "!" + enHuella });
                visor.scene.primitives.add(edificiosOsm);
                contextoActivo = true;
                estado.textContent = ' contexto activo ✓';
              } catch (e) {
                estado.textContent = ' error: ' + (e.message || e);
              }
            }
            </script>
            </body>
            </html>
            """;
    }

    /// <summary>Rutas de los dos visores junto al .geojson (mismo directorio y base).</summary>
    public static (string MapLibre, string Cesium) RutasVisores(string rutaGeojson)
    {
        string dir = System.IO.Path.GetDirectoryName(rutaGeojson) ?? "";
        string baseNombre = System.IO.Path.GetFileNameWithoutExtension(rutaGeojson);
        return (System.IO.Path.Combine(dir, baseNombre + "-visor-maplibre.html"),
                System.IO.Path.Combine(dir, baseNombre + "-visor-cesium.html"));
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
