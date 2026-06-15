# Spec — Etapa 1b · Losas visibles como superficie en el visor WebXR

**Fecha:** 2026-06-15
**Repos (cross-repo):**
- Motor + visor: `~/Downloads/EstructurasRD-engine/motor-fea` (Python + JS), rama `engine/shell-web-webxr`.
- Exportador .NET: `~/Downloads/EstructurasRD-main` (.NET 8 / Avalonia), rama `ui/editor-planta`.
**Linaje:** continúa el exportador. 1a llevó el **pórtico** (columnas+vigas) al visor; 1b añade las
**losas como paneles**. Decisión tomada en el brainstorming de 1a: "1a primero, luego 1b" (1b cruza al
repo del motor, antes solo-lectura).

---

## 1. Objetivo (definición de "hecho")

Que las losas del edificio se **vean como superficies planas** en el visor WebXR, además del pórtico.
Es **solo geometría** (paneles semi-transparentes en su nivel); no se colorean por resultados.

Hecho cuando:
1. El contrato de modelo del motor acepta una lista **opcional** `losas` (geometría inerte; el FEA la
   ignora) y el modelo sigue resolviendo igual que en 1a.
2. `exportar_escena` emite esas losas en `escena.losas` (hoy `[]` fijo).
3. El visor (`app.js`) dibuja cada losa como un panel plano semi-transparente en su cota, con limpieza
   simétrica al recargar modelo.
4. El exportador .NET emite `losas` (4 esquinas por paño a la cota del nivel) en el JSON del modelo.
5. Tests verdes en ambos repos (pytest del motor: 225 intactos + nuevos; xUnit del .NET: exporter +
   nuevos). El JSON .NET con losas produce `escena.losas` no vacío en el motor real.

**Fuera de alcance (Etapa 2 y posteriores):** colorear losas por deflexión/momento; losas que aporten
rigidez al FEA (placa/shell); picking/etiquetas de losa; losas no rectangulares (el modelo .NET es
rectangular). Cargas reales siguen fuera (siguen `cargas: []`).

---

## 2. Hallazgos que enmarcan el diseño (verificados en código)

### 2.1 El visor no consume `losas` hoy; el patrón de barras es el molde
`viz/escena.py` (≈L64) devuelve `"losas": []` fijo. `app.js::renderEscena` (≈L841) solo recorre
`escena.nodos` y `escena.barras`; `grep escena.losas` = sin uso. El patrón a replicar es `addBarra`
(app.js ≈L111): `new THREE.Mesh(geom, material)` → `scene.add` → push a un arreglo. Import:
`import * as THREE from 'three'`. Material semi-transparente de referencia (plano de corte, ≈L735):
`new THREE.MeshBasicMaterial({ transparent:true, opacity:0.15, side:THREE.DoubleSide, depthWrite:false })`.
`limpiarEscena` (≈L611) hace `scene.remove(mesh); geometry.dispose(); material.dispose()` por cada
barra y por `losaMesh`. **Colisión a evitar:** ya existe un `losaMesh`/`losa` global para el modo de
losa individual (`--disenar-losa`); los paneles del edificio usan nombres nuevos (`losasEscena`).

### 2.2 El contrato ya reserva la clave `losas` (retrocompatible)
`tests/test_contrato.py` (≈L201) ya asserta
`set(d["escena"]) == {"unidades","bbox","nodos","barras","losas"}`, y (≈L208)
`d["escena"] == exportar_escena(modelo)`. Como los modelos de esos tests son solo-frame, `losas`
permanece `[]` y los asserts siguen pasando. Añadir contenido es retrocompatible.

### 2.3 El modelo del motor es solo-FEA; las losas serán inertes
`core/modelo.py::ModeloEstructural` (`@dataclass`) tiene `nodos, materiales, secciones, elementos,
apoyos, cargas`; `n_gdl = len(nodos)*6`; `validar()` revisa integridad referencial. No hay losas.
`contrato.py::modelo_desde_dict` construye el modelo desde el dict. Para que `exportar_escena(modelo)`
emita losas, el modelo debe **transportarlas** — se añade un campo inerte que el FEA no toca.

### 2.4 Geometría real de la losa en .NET
`Core/Models/Sistema.cs::Losa`: `Id, Tipo, Carga (ton/m²), Espesor (m), Lx (m), Ly (m), Rec (m),
CoordenadaX, CoordenadaY (m)`. **`CoordenadaX/Y` = esquina origen**; el paño ocupa
`[X, X+Lx] × [Y, Y+Ly]` (documentado en el modelo). Ruta de propiedad:
`Proyecto.Edificios → Nivel.Sistemas → Sistema.Losas` (todas `ObservableCollection`).
`Render3D/EscenaEdificio.cs` ya dibuja la huella con esquinas
`(X,Cota,Y),(X+Lx,Cota,Y),(X+Lx,Cota,Y+Ly),(X,Cota,Y+Ly)` (su escena es Y-arriba; el exportador usa
Z-arriba como el resto del modelo motor).

---

## 3. Decisiones de diseño (aprobadas)

| # | Decisión | Elección |
|---|---|---|
| D1 | Alcance | **Solo geometría**: paneles planos semi-transparentes, todos los niveles. Sin color por resultados, sin picking. |
| D2 | Las losas en el FEA | **Inertes**: campo passthrough en el modelo; no afectan `validar()`, `n_gdl`, ni la solución (idéntica a 1a). |
| D3 | Contrato de la losa | `{id:int, puntos:[[x,y,z]×4]}` (SI, Z arriba). Mismo shape en entrada del motor y en `escena.losas`. |
| D4 | Geometría (exportador) | 4 esquinas a la cota: `(CX,CY,cota),(CX+Lx,CY,cota),(CX+Lx,CY+Ly,cota),(CX,CY+Ly,cota)`. |
| D5 | Visual | Panel `MeshBasicMaterial` semi-transparente (`opacity 0.30`, `DoubleSide`, `depthWrite:false`), color suave (azulado `0x4488cc`). |
| D6 | Nombres en el visor | `losasEscena` (arreglo) + `addLosaEscena` / cleanup en `limpiarEscena`. No tocar el `losaMesh` del modo individual. |
| D7 | Opcionalidad | `losas` ausente o `[]` ⇒ comportamiento idéntico a 1a en todos lados. |
| D8 | Id de losa | El exportador asigna ids secuenciales por orden de exportación (unicidad para la escena/visor). |

---

## 4. Arquitectura (componentes, cross-repo)

```
.NET  ExportadorModeloMotor ──(JSON: + "losas":[{id,puntos[4]}])──▶  motor --analyze / visor
                                                                         │
Python  modelo_desde_dict ──parse passthrough──▶ ModeloEstructural.losas (inerte)
                                                                         │
        exportar_escena ──emite──▶ escena.losas = [{id, puntos[4]}]
                                                                         │
JS      renderEscena ──addLosaEscena(losa)──▶ panel plano en la escena (THREE.Mesh)
        limpiarEscena ──dispose──▶ losasEscena[]
```

### 4.1 Motor — `core/modelo.py`
- Nuevo `@dataclass LosaViz: id:int; puntos:list` (lista de 4 `[x,y,z]`).
- `ModeloEstructural`: nuevo campo `losas: list[LosaViz] = field(default_factory=list)`. `validar()`
  **sin cambios** (no valida losas); `n_gdl` sin cambios.

### 4.2 Motor — `api/contrato.py::modelo_desde_dict`
- Tras parsear `cargas`, añadir: por cada `l in d.get("losas", [])` → `LosaViz(int(l["id"]),
  [[float(p[0]),float(p[1]),float(p[2])] for p in l["puntos"]])`. Tolerante: si `puntos` no tiene 4,
  se omite la losa (no rompe el análisis).

### 4.3 Motor — `viz/escena.py::exportar_escena`
- Reemplazar `"losas": []` por
  `"losas": [{"id": l.id, "puntos": l.puntos} for l in modelo.losas]`.

### 4.4 Visor — `viz/static/app.js`
- Global `const losasEscena = [];`.
- `function addLosaEscena(l)`: construye `THREE.BufferGeometry` con los 4 puntos (`l.puntos`) como dos
  triángulos (0-1-2, 0-2-3), material `MeshBasicMaterial` (D5); `scene.add(mesh)`;
  `losasEscena.push({ mesh, id: l.id })`.
- `renderEscena`: tras el bucle de barras, `for (const l of escena.losas) addLosaEscena(l);` y
  actualizar el mensaje (`… · ${escena.losas.length} losas`).
- `limpiarEscena`: por cada `losasEscena` → `scene.remove(mesh); mesh.geometry.dispose();
  mesh.material.dispose();` y `losasEscena.length = 0`.

### 4.5 Exportador .NET — `ModeloMotorDto` + `ExportadorModeloMotor`
- `LosaMotor { [JsonPropertyName("id")] int Id; [JsonPropertyName("puntos")] double[][] Puntos }`.
- `ModeloMotorDto`: `[JsonPropertyName("losas")] List<LosaMotor> Losas { get; set; } = new();`.
- `ExportadorModeloMotor.Exportar`: tras los elementos/apoyos, recorrer `Niveles→Sistemas→Losas`; por
  cada losa, `id` secuencial y `puntos` = las 4 esquinas (D4) a `nivel.Cota`. `cargas` siguen vacías.
  La validación de integridad **no** cambia (las losas no referencian nodos).

---

## 5. Flujo de datos

1. El usuario exporta el edificio (botón de 1a). El JSON ahora incluye `losas` si hay paños.
2. Sube el JSON al visor → `/visor` → `modelo_desde_dict` (losas inertes) → `visor_dict` →
   `exportar_escena` emite `escena.losas`.
3. El visor dibuja barras (1a) **y** paneles de losa (1b). La deformada/FEA es idéntica a 1a.

---

## 6. Manejo de errores

- `losas` ausente/`[]` → comportamiento de 1a (sin paneles).
- Losa con `puntos` ≠ 4 → se omite (motor tolerante; exportador siempre emite 4).
- El visor: si `escena.losas` falta (modelos viejos) → no dibuja paneles (guard `escena.losas || []`).

---

## 7. Estrategia de tests

**Motor (pytest):**
1. `test_contrato`: `modelo_desde_dict` con `losas` → `modelo.losas` poblado; round-trip
   `exportar_escena` emite los `puntos`. Los asserts existentes de claves siguen verdes.
2. `test_modelo`: un modelo con `losas` **resuelve igual** (mismo `n_gdl`, `validar()` vacío) que sin
   ellas (las losas no afectan el FEA).
3. `test_escena`: `exportar_escena` con losas → `escena["losas"]` con `{id,puntos}` correctos.

**Exportador .NET (xUnit):**
4. Golden: edificio con 1 losa (CX,CY,Lx,Ly, cota) → `ModeloMotorDto.Losas[0].Puntos` = 4 esquinas
   esperadas; `ToJson` contiene `"losas"`/`"puntos"`. Los tests de 1a siguen verdes (sin losas → lista
   vacía pero presente).

**Integración guardada:** extender el test del motor real para un edificio con losa → exit 0 y, vía
`visor_dict`/`exportar_escena`, `escena.losas` no vacío.

---

## 8. Archivos

**Motor (`motor-fea`):**
- `src/motor_fea/core/modelo.py` (mod): `LosaViz` + campo `losas`.
- `src/motor_fea/api/contrato.py` (mod): parse passthrough de `losas`.
- `src/motor_fea/viz/escena.py` (mod): emitir `losas`.
- `src/motor_fea/viz/static/app.js` (mod): `losasEscena` + `addLosaEscena` + render + cleanup.
- Tests: `tests/test_contrato.py`, `tests/test_modelo.py`, `tests/test_escena.py`.

**Exportador (`EstructurasRD-main`):**
- `src/Core/Services/ModeloMotorModels.cs` (mod): `LosaMotor` + `ModeloMotorDto.Losas`.
- `src/Core/Services/ExportadorModeloMotor.cs` (mod): emitir losas.
- Tests: `tests/LosasPlus.Tests/ExportadorModeloMotorTests.cs` (+ golden de losa).

---

## 9. Limitaciones / gaps (1b)

- Losas **rectangulares** (el modelo .NET lo es); polígonos arbitrarios = trabajo futuro (el contrato
  `puntos` ya lo permitiría con N puntos).
- **Inertes** en el FEA: no aportan rigidez ni reciben/forman parte de resultados.
- Sin color por resultados, sin picking/etiqueta (Etapa 2+).
- Cargas siguen vacías (Etapa 2).

---

## 10. Próximo paso

Invocar `superpowers:writing-plans` para el plan TDD (tareas separadas motor-side y .NET-side).
