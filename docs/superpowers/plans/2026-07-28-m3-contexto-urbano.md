# M.3 — Contexto urbano en visores 3D: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Los dos visores HTML generados (MapLibre y Cesium) muestran la malla urbana 3D real alrededor del edificio propio, ocultando los edificios OSM dentro de su huella.

**Architecture:** Todo M.3 vive en las plantillas HTML de `GeneradorVisorMapa` (funciones puras, sin I/O, sin cambio de firma). MapLibre suma una fuente vectorial OpenFreeMap (sin token) con capa `fill-extrusion` gris filtrada por `within`; Cesium suma OSM Buildings (`createOsmBuildingsAsync`) al panel de token ion existente, con `Cesium3DTileStyle` que oculta la huella propia. Spec: `docs/superpowers/specs/2026-07-28-m3-contexto-urbano-design.md`.

**Tech Stack:** C# (.NET 8, raw strings `$$"""`), xUnit, MapLibre GL 4.7.1, CesiumJS 1.119, OpenFreeMap (tiles vectoriales, esquema OpenMapTiles).

## Global Constraints

- .NET 8 instalado en `~/.dotnet` — ejecutar con `~/.dotnet/dotnet` (o `dotnet` si ya está en PATH).
- Repo: `/home/gdc/dev/estructurasrd/main`, rama `ui/editor-planta`. Suite actual: 839/839.
- Identificadores, comentarios y mensajes en **español** (convención del proyecto).
- Las plantillas usan raw strings `$$"""..."""`: `{` y `}` sencillas son literales (JS las usa), `{{...}}` es interpolación C#. No romper esto.
- **Ningún script/CSS CDN nuevo** (OpenFreeMap son tiles de datos): los bloques SRI existentes no se tocan.
- El GeoJSON embebido sigue pasando por `Empotrar()` (escape de `</`). No introducir texto dinámico fuera de ese camino.
- TDD estricto: test rojo → implementación mínima → verde → commit.

---

### Task 1: MapLibre — capa de contexto urbano OpenFreeMap con filtro de huella

**Files:**
- Modify: `src/Core/Services/GeneradorVisorMapa.cs:33-62` (bloque `<script>` de `GenerarMapLibre`)
- Test: `tests/LosasPlus.Tests/GeneradorVisorMapaTests.cs`

**Interfaces:**
- Consumes: `GeneradorVisorMapa.GenerarMapLibre(string geojson)` — firma intacta.
- Produces: HTML MapLibre con fuente `openfreemap`, capa `contexto` (`fill-extrusion` sobre `source-layer: 'building'`) y constante JS `huella` (Polygon GeoJSON del bbox propio con margen `M = 0.000045` ≈ 5 m). Task 3 verifica este HTML en navegador.

- [ ] **Step 1: Write the failing test**

Añadir a `GeneradorVisorMapaTests.cs` (después de `Maplibre_contiene_capa_de_extrusion_y_geojson_embebido`):

```csharp
[Fact]
public void Maplibre_contiene_contexto_urbano_openfreemap_con_filtro_de_huella()
{
    string html = GeneradorVisorMapa.GenerarMapLibre(GeojsonDemo);

    Assert.Contains("tiles.openfreemap.org/planet", html);   // fuente vectorial sin token
    Assert.Contains("'source-layer': 'building'", html);     // capa building del esquema OpenMapTiles
    Assert.Contains("within", html);                          // filtro de huella
    Assert.Contains("coalesce", html);                        // alturas OSM ausentes → default
    Assert.Contains("render_height", html);
    Assert.Contains("#c9c4bc", html);                         // contexto gris neutro
    Assert.Contains("const huella", html);                    // bbox propio con margen
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `~/.dotnet/dotnet test tests/LosasPlus.Tests --filter "FullyQualifiedName~Maplibre_contiene_contexto_urbano" 2>&1 | tail -20`
Expected: FAIL — `Assert.Contains("tiles.openfreemap.org/planet", ...)` no encontrado.

- [ ] **Step 3: Write minimal implementation**

En `GenerarMapLibre`, tres ediciones dentro del `<script>`:

**(a)** Tras la línea `const lons = ..., lats = ...;` añadir la huella:

```js
const M = 0.000045; // margen ~5 m alrededor del edificio propio
const huella = { type: 'Polygon', coordinates: [[
  [Math.min(...lons) - M, Math.min(...lats) - M],
  [Math.max(...lons) + M, Math.min(...lats) - M],
  [Math.max(...lons) + M, Math.max(...lats) + M],
  [Math.min(...lons) - M, Math.max(...lats) + M],
  [Math.min(...lons) - M, Math.min(...lats) - M]
]] };
```

**(b)** En `style.sources`, junto a `osm`, añadir la fuente vectorial:

```js
openfreemap: { type: 'vector', url: 'https://tiles.openfreemap.org/planet' }
```

(el TileJSON declara su propio `maxzoom` — no repite el bug del cap de zoom de `0172b27`).

**(c)** Dentro de `map.on('load', ...)`, **antes** del `addLayer` de `extrusion`, añadir la capa de contexto (los vecinos totalmente dentro de la huella se ocultan):

```js
map.addLayer({
  id: 'contexto', type: 'fill-extrusion',
  source: 'openfreemap', 'source-layer': 'building',
  filter: ['!', ['within', huella]],
  paint: {
    'fill-extrusion-base': ['coalesce', ['get', 'render_min_height'], 0],
    'fill-extrusion-height': ['coalesce', ['get', 'render_height'], 8],
    'fill-extrusion-color': '#c9c4bc',
    'fill-extrusion-opacity': 0.85
  }
});
```

Actualizar también el doc-comment de `GenerarMapLibre` (línea ~3-11 de la clase) mencionando el contexto urbano M.3.

- [ ] **Step 4: Run tests to verify they pass (suite completa)**

Run: `~/.dotnet/dotnet test 2>&1 | tail -5`
Expected: PASS — 840/840 (839 previos + 1 nuevo). Los tests M.2b existentes (`maxzoom: 19`, SRI, escape `</`) deben seguir verdes.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/GeneradorVisorMapa.cs tests/LosasPlus.Tests/GeneradorVisorMapaTests.cs
git commit -m "feat(M.3): contexto urbano OpenFreeMap en visor MapLibre, con filtro de huella"
```

---

### Task 2: Cesium — OSM Buildings en el panel de token, con estilo de huella

**Files:**
- Modify: `src/Core/Services/GeneradorVisorMapa.cs:96-129` (panel y `<script>` de `GenerarCesium`)
- Test: `tests/LosasPlus.Tests/GeneradorVisorMapaTests.cs`

**Interfaces:**
- Consumes: `GeneradorVisorMapa.GenerarCesium(string geojson)` — firma intacta.
- Produces: HTML Cesium cuya función `activarContexto()` (renombrada desde `activarTerreno()`) activa World Terrain **y** OSM Buildings con `Cesium3DTileStyle` de huella; `<span id="estado">` para mensajes. Task 3 verifica este HTML en navegador.

- [ ] **Step 1: Write the failing test**

Añadir a `GeneradorVisorMapaTests.cs` (después de `Cesium_no_lleva_token_hardcodeado`):

```csharp
[Fact]
public void Cesium_activa_osm_buildings_con_filtro_de_huella()
{
    string html = GeneradorVisorMapa.GenerarCesium(GeojsonDemo);

    Assert.Contains("createOsmBuildingsAsync", html);        // malla urbana de Cesium ion
    Assert.Contains("Cesium3DTileStyle", html);              // estilo con condición show
    Assert.Contains("cesium#latitude", html);                // filtro por centro del feature
    Assert.Contains("cesium#longitude", html);
    Assert.Contains("terreno + edificios OSM", html);        // etiqueta nueva del panel
    Assert.Contains("id=\"estado\"", html);                  // errores al panel, sin alert
    Assert.DoesNotContain("alert(", html);                   // alert bloquearía el visor
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `~/.dotnet/dotnet test tests/LosasPlus.Tests --filter "FullyQualifiedName~Cesium_activa_osm_buildings" 2>&1 | tail -20`
Expected: FAIL — `Assert.Contains("createOsmBuildingsAsync", ...)` no encontrado.

- [ ] **Step 3: Write minimal implementation**

En `GenerarCesium`, dos ediciones:

**(a)** Reemplazar el `<div id="panelToken">` por:

```html
<div id="panelToken">
  Token Cesium ion (opcional, activa terreno + edificios OSM):
  <input id="token" size="32" placeholder="pegar token...">
  <button onclick="activarContexto()">Activar</button>
  <span id="estado"></span>
</div>
```

**(b)** Reemplazar la función `activarTerreno()` completa por (nota: dentro del raw
string `$$"""` las llaves sencillas de JS son literales; la condición `show` de
Cesium usa la sintaxis `${feature[...]}` **como string JS por concatenación** —
nunca como template literal, para no chocar con la interpolación C#):

```js
async function activarContexto() {
  const t = document.getElementById('token').value.trim();
  const estado = document.getElementById('estado');
  if (!t) return;
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
      "(${feature['cesium#latitude']} >= "  + (Math.min(...lats) - M) +
      " && ${feature['cesium#latitude']} <= " + (Math.max(...lats) + M) +
      " && ${feature['cesium#longitude']} >= " + (Math.min(...lons) - M) +
      " && ${feature['cesium#longitude']} <= " + (Math.max(...lons) + M) + ")";
    edificiosOsm.style = new Cesium.Cesium3DTileStyle({ show: "!" + enHuella });
    visor.scene.primitives.add(edificiosOsm);
    estado.textContent = ' contexto activo ✓';
  } catch (e) {
    estado.textContent = ' error: ' + (e.message || e);
  }
}
```

Actualizar el doc-comment de `GenerarCesium` (líneas 69-74): el token ahora
activa terreno + OSM Buildings (M.3 cumplida, ya no "puerta a").

- [ ] **Step 4: Run tests to verify they pass (suite completa)**

Run: `~/.dotnet/dotnet test 2>&1 | tail -5`
Expected: PASS — 841/841. En particular `Cesium_no_lleva_token_hardcodeado`
sigue verde (el token sigue llegando solo por el input).

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/GeneradorVisorMapa.cs tests/LosasPlus.Tests/GeneradorVisorMapaTests.cs
git commit -m "feat(M.3): OSM Buildings en visor Cesium — token ion activa terreno + contexto, huella propia oculta"
```

---

### Task 3: Verificación en navegador real + bitácora + push

**Files:**
- Create: `/tmp/claude-1000/-home-gdc/e1f1dded-9907-4f6e-87d9-6cc56fc7abe3/scratchpad/genera-visores/Program.cs` (proyecto console desechable, patrón de verificación M.2b)
- Modify: `~/BRAIN/03_APPS/EstructurasRD/Bitacora de desarrollo.md` (entrada nueva), `~/BRAIN/00_MOC/Estado actual.md` (línea de foco)

**Interfaces:**
- Consumes: los HTML de Task 1 y Task 2 vía `ExportadorGeoJson.Exportar(proyecto)` + `GeneradorVisorMapa.GenerarMapLibre/GenerarCesium/RutasVisores`.
- Produces: capturas `_assets/M3-visor-maplibre.jpg` y `_assets/M3-visor-cesium.jpg` en el BRAIN; commits pusheados en ambos repos.

- [ ] **Step 1: Generar visores reales con proyecto console desechable**

Como en M.2b: proyecto console en el scratchpad que referencia `src/Core`, crea el edificio demo de 3 niveles georreferenciado en Santo Domingo (origen Av. Winston Churchill, rotación 30°), llama a `ExportadorGeoJson.Exportar` y escribe `edificio.geojson` + los dos visores con `GeneradorVisorMapa`. Verificación estática previa: `grep -c "openfreemap\|createOsmBuildingsAsync"` sobre los HTML generados > 0, y ningún `</` sin escapar dentro del bloque `const GEOJSON`.

- [ ] **Step 2: Verificar en Chrome (MapLibre)**

Servir el directorio por localhost (`python3 -m http.server`) y abrir `edificio-visor-maplibre.html`. Criterio PASS: edificios vecinos grises extruidos alrededor del edificio naranja; parcela propia sin edificio OSM interpenetrado; sin errores en consola (los 404 aislados de tiles no cuentan). Captura → `~/BRAIN/03_APPS/EstructurasRD/_assets/M3-visor-maplibre.jpg`.

- [ ] **Step 3: Verificar en Chrome (Cesium)**

Abrir `edificio-visor-cesium.html`. Sin token: el visor se ve como en M.2b (regresión OK). Con token ion de Emil pegado: terreno + OSM Buildings cargan, `estado` muestra "contexto activo ✓", huella propia limpia. Si Emil no tiene token a mano, se registra la verificación sin-token como PASS y la de OSM Buildings queda pendiente anotada en la bitácora. Captura → `_assets/M3-visor-cesium.jpg`.

- [ ] **Step 4: Bitácora + Estado actual + push**

Entrada "## 2026-07-28 — Fase M.3: contexto urbano en visores (commits ...)" en la bitácora con qué/cómo/decisión/próximo paso natural (M.4: 3D Tiles propios multi-edificio, cuando haya caso). Actualizar la línea de foco de `Estado actual.md`. Luego:

```bash
cd /home/gdc/dev/estructurasrd/main && git push
cd ~/BRAIN && git add -A && git commit -m "EstructurasRD M.3: contexto urbano en visores" && git push
```

---

## Self-review (hecho al escribir el plan)

- **Cobertura del spec**: fuente OpenFreeMap (T1), filtro `within` (T1), coalesce alturas (T1), OSM Buildings + panel (T2), estilo de huella Cesium (T2), errores al panel sin alert (T2), sin SRI nuevo (constraint global), verificación Chrome + bitácora (T3). Sin huecos.
- **Placeholders**: ninguno — todo el código está inline.
- **Consistencia de tipos/nombres**: `huella`/`M` solo viven dentro de cada HTML; `activarContexto` aparece igual en botón y función; firmas C# intactas en ambos tasks.
