# M.3 — Contexto urbano en los visores 3D (diseño)

**Fecha:** 2026-07-28 · **Rama:** `ui/editor-planta` · **Estado:** aprobado por Emil

## Objetivo

Que el edificio propio conviva con la malla urbana real en los dos visores HTML
generados por `GeneradorVisorMapa` (M.2b): edificios vecinos en 3D alrededor del
modelo extruido. Cierra el ciclo edificio↔ciudad iniciado en M.1/M.2.

## Decisiones de alcance (clarificadas con Emil)

1. **Ambos visores** ganan contexto urbano, cada uno con su fuente nativa:
   MapLibre sin token (OpenFreeMap), Cesium con el token ion opcional que ya
   existe (OSM Buildings).
2. **Solape con OSM**: si OSM ya tiene un edificio en la parcela propia, el
   visor lo **oculta dentro de la huella** del modelo — el modelo naranja es la
   única fuente de verdad en su parcela.
3. **Solo visores**: cero cambios en la UI Avalonia (mismo botón
   "🌍 Exportar GeoJSON"), cero cambios de API en C#. Exportar 3D Tiles del
   edificio propio queda fuera (candidato a M.4 cuando exista caso
   multi-edificio).

## Enfoques considerados

- **1. Fuentes nativas por visor** (elegido) — solo cambian las plantillas HTML;
  M.3 sigue siendo generación pura de strings, testeable estáticamente.
- 2. Overpass API embebido al exportar — descartado: convierte el exportador en
  cliente HTTP (rate limits, contexto congelado) para un beneficio offline que
  no aplica (los visores ya necesitan red para los tiles base).
- 3. Google Photorealistic 3D Tiles — descartado: API key con billing, ToS
  restrictivos, solo beneficiaría a Cesium.

## Diseño

### Arquitectura

`GenerarMapLibre(geojson)` y `GenerarCesium(geojson)` conservan firma y siguen
siendo funciones puras sin I/O. `RutasVisores`, `ExportadorGeoJson` y la UI no
se tocan. **No entra ningún script CDN nuevo**: OpenFreeMap son tiles de datos
consumidos por el MapLibre JS ya cargado → no hay SRI nuevo que verificar.

### Visor MapLibre — contexto sin token

- Fuente vectorial nueva:
  `{ type: 'vector', url: 'https://tiles.openfreemap.org/planet' }`
  (TileJSON de OpenFreeMap; sin token ni registro; el TileJSON declara su
  propio `maxzoom` → no repite el bug del cap de zoom de `0172b27`).
- Capa `contexto` (`fill-extrusion`, `source-layer: 'building'`) **debajo** de
  la capa `extrusion` propia: color gris neutro `#c9c4bc`, opacidad 0.85,
  `base`/`height` desde `render_min_height`/`render_height` con
  `['coalesce', …, 0]` / `['coalesce', …, 8]` para edificios OSM sin altura
  (frecuente en RD).
- **Filtro de huella**: expresión `within` de MapLibre — se construye en JS un
  polígono bbox del edificio (de los `lons/lats` ya calculados, margen ~5 m,
  ≈0.000045°) y la capa contexto lleva `filter: ['!', ['within', bboxPoligono]]`.
  Limitación asumida: un edificio OSM *parcialmente* dentro del bbox no se
  oculta (solo los totalmente contenidos).

### Visor Cesium — OSM Buildings con el token existente

- El panel pasa a "Token Cesium ion (opcional, activa terreno + edificios OSM)".
- `activarTerreno()` además del World Terrain hace
  `visor.scene.primitives.add(await Cesium.createOsmBuildingsAsync())`.
- **Filtro de huella**: `Cesium3DTileStyle` con condición `show` sobre
  `cesium#latitude` / `cesium#longitude` (propiedades de los features de OSM
  Buildings): se oculta todo feature cuyo centro caiga en el rango min/max
  lat/lon del `GEOJSON` embebido.
- Sin token, el visor queda exactamente como hoy.

### Errores y degradación

- MapLibre: OpenFreeMap caído → capa contexto vacía; el raster OSM y el
  edificio propio siguen funcionando.
- Cesium: token inválido o fallo de OSM Buildings → `try/catch` que escribe el
  error en el panel (sin `alert`, que bloquearía el visor); el globo sigue vivo.

### Testing

- Aserciones estáticas nuevas en `GeneradorVisorMapaTests` (patrón M.2b):
  - MapLibre: contiene `openfreemap`, `source-layer`, filtro `within`,
    `coalesce`, color de contexto.
  - Cesium: contiene `createOsmBuildingsAsync`, `Cesium3DTileStyle`,
    `cesium#latitude`, etiqueta nueva del panel.
  - Ambos: sin `</` sin escapar dentro del bloque GeoJSON (regresión M.2b).
- Suite 839 → ~845, todo verde.
- **Verificación en navegador real** (como M.2b): ambos visores en Chrome sobre
  la Av. Winston Churchill con malla urbana visible; capturas a
  `_assets/M3-visor-maplibre.jpg` / `_assets/M3-visor-cesium.jpg` y entrada en
  la bitácora del BRAIN.

## Fuera de alcance

- Export de 3D Tiles del edificio propio (M.4 potencial).
- Cambios en UI Avalonia, exportador GeoJSON o formatos de archivo.
- Persistencia del token ion entre sesiones del visor.
