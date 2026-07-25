# M.2b — Visores 3D del edificio extruido (MapLibre + Cesium)

**Fecha:** 2026-07-25 · **Rama:** `ui/editor-planta` · **Estado:** aprobado por Emil

## Objetivo

Ver el edificio georreferenciado y extruido (M.2a: `base_height`/`height` en el
GeoJSON) sobre un mapa 3D con doble click en un HTML, sin servidor, sin cuenta
y sin token. Cierra el ciclo edificio→ciudad en 3D iniciado en M.1.

## Decisiones de diseño

1. **Dos visores, un formato.** El mismo `.geojson` alimenta ambos:
   - `visor-maplibre.html` — MapLibre GL (CDN), fondo raster OSM, capa
     `fill-extrusion` con `["get","base_height"]` / `["get","height"]` tal
     cual. Ligero y sin token: el visor del día a día.
   - `visor-cesium.html` — CesiumJS (CDN), imagery OSM **sin token**, terreno
     elipsoide. `GeoJsonDataSource` + por entidad
     `polygon.height = base_height`, `polygon.extrudedHeight = height`
     (Cesium no extruye solo desde propiedades). Campo opcional para pegar un
     token de Cesium ion que activa World Terrain — puerta a M.3 (3D Tiles)
     sin obligar a nadie a crear cuenta.
2. **GeoJSON embebido inline** en cada HTML (`const GEOJSON = {...}`), no
   referenciado por ruta: `file://` + `fetch()` muere por CORS; inline abre
   con doble click en cualquier navegador. Se escapa `</` → `<\/` para que un
   nombre de nivel no pueda cerrar el `<script>` (mismo criterio del
   security-review de N.2: robustez ante datos propios raros).
3. **Cámara automática en el edificio**: ambos motores la resuelven en
   runtime (`map.fitBounds` calculado del GeoJSON en JS / `flyTo(dataSource)`)
   — cero geometría nueva en C#.
4. **CDN con SRI**: los `<script>`/`<link>` llevan versión exacta (MapLibre
   4.7.1, Cesium 1.119) + `integrity="sha384-..."` + `crossorigin="anonymous"`
   — un CDN comprometido no puede inyectar código en el visor. Hashes
   verificados contra los CDN el 2026-07-25 (ver plan).
5. **Generador puro, sin I/O**: `GeneradorVisorMapa` (estático, en
   `src/Core/Services`, patrón `ExportadorGeoJson`) con
   `GenerarMapLibre(string geojson)` y `GenerarCesium(string geojson)` →
   HTML completo como string. Plantillas como raw string literals de C#.
6. **UI sin botón nuevo**: el botón existente "🌍 Exportar GeoJSON (mapa)"
   escribe, junto al `.geojson`, `<base>-visor-maplibre.html` y
   `<base>-visor-cesium.html`. El status lista los tres archivos.

## Flujo

```
ExportarGeoJson() [MainViewModel]
  ├── edificio.geojson          (ExportadorGeoJson — sin cambios)
  ├── edificio-visor-maplibre.html  (GeneradorVisorMapa.GenerarMapLibre)
  └── edificio-visor-cesium.html    (GeneradorVisorMapa.GenerarCesium)
```

## Errores

- Generador: geojson null/vacío → `ExportadorModeloException` (misma familia).
- ViewModel: cualquier fallo cae en el `catch` existente → `StatusExportacion`.
- Los visores requieren internet (CDN + teselas OSM); si no hay, el HTML
  muestra el aviso nativo del motor de mapas — no se maneja offline (YAGNI).

## Tests (TDD, ~8 nuevos)

- MapLibre: HTML contiene capa `fill-extrusion` con `base_height`/`height`,
  GeoJSON embebido, referencia CDN de MapLibre.
- Cesium: HTML contiene `GeoJsonDataSource`, imagery OSM, **ningún** token
  hardcodeado, asignación de `extrudedHeight`.
- Ambos: `</` del GeoJSON escapado; null/vacío lanza excepción.
- ViewModel: export escribe los tres archivos (test de integración existente
  del flujo de export como referencia).

## Fuera de alcance (M.3+)

3D Tiles, texturas, columnas/vigas como volúmenes individuales, contexto
urbano (Cesium OSM Buildings), servir por HTTP, modo offline.
