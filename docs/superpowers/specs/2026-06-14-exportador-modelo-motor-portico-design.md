# Spec — Exportador de modelo al motor · Etapa 1a (pórtico → visor WebXR)

**Fecha:** 2026-06-14
**Repo:** `~/Downloads/EstructurasRD-main` (suite .NET 8 / Avalonia 11.3), rama `ui/editor-planta`.
**Motor/visor (otro repo, solo-lectura en 1a):** `~/Downloads/EstructurasRD-engine/motor-fea` (Python),
rama `engine/shell-web-webxr`.
**Linaje:** cierra la **brecha crítica** detectada tras #5a — la app dibuja el sistema estructural
pero no sabe exportarlo al JSON que el visor WebXR consume. Complementa a #5a (que *consume*
resultados del motor); este exportador *produce* la entrada del motor/visor.

---

## 1. Objetivo (definición de "hecho")

Añadir un camino por el cual el escritorio **exporte el pórtico (columnas + vigas) del edificio
activo al JSON de modelo del motor**, de modo que ese archivo, cargado en el visor WebXR, **muestre
la geometría 3D del edificio** (las mismas barras que el usuario dibujó).

La Etapa 1a está **hecha** cuando:
1. Existe un comando "Exportar modelo para visor (FEA)" que recorre el `Edificio` activo y produce un
   archivo `.json` con el contrato de modelo del motor (`nodos, materiales, secciones, elementos,
   apoyos, cargas`).
2. El JSON es **válido y resoluble**: pasa integridad referencial (sin IDs colgantes) y tiene apoyos
   suficientes para que el análisis del visor no sea singular.
3. Cargado en el visor (subida de modelo propio → `/visor`), el edificio se ve en 3D/VR con la misma
   geometría que el 3D de escritorio.
4. Errores (sin columnas/vigas, sección inválida) se reportan sin tumbar la app.
5. Tests .NET verdes (`dotnet test`), incluyendo síntesis de nodos, propiedades de sección, cálculo de
   E, apoyos, y un export golden validado contra integridad referencial.

**Fuera de alcance (Etapa 2 y posteriores, gaps documentados):**
- **Cargas reales** (peso de losa por área tributaria, peso propio) → en 1a `cargas: []`.
- **Losas como superficie** en el visor (requiere tocar el repo del motor) → Etapa 1b.
- **Entrega en vivo** (POST a `/visor` + abrir navegador) → en 1a se exporta a archivo.
- **Muros** (no se exportan en 1a).
- Apoyos parciales/resortes; materiales múltiples por elemento; aligerado.

---

## 2. Hallazgos que enmarcan el diseño (verificados en código)

### 2.1 El visor consume `visor_dict`, no la salida cruda de `--analyze`
El visor WebXR (`motor-fea/src/motor_fea/viz/static/app.js`) sube el modelo a `/visor`, que devuelve un
paquete de tres partes vía `visor_dict(modelo_dict)`
(`motor-fea/src/motor_fea/api/contrato.py`): `{escena, resultados, esfuerzos}`. `escena`
(`viz/escena.py::exportar_escena`) es la **geometría** que se renderiza. `resultados`/`esfuerzos`
provienen del análisis FEA. Por tanto el modelo exportado debe ser **resoluble** (no solo geométrico):
`exportar_escena` llama `modelo.validar()` y lanza si el modelo es inválido.

### 2.2 El contrato de modelo del motor (entrada, SI, Z arriba)
`motor-fea/src/motor_fea/api/contrato.py::modelo_desde_dict` (L47–71) deserializa:
- `nodos`: `{id:int, x:float, y:float, z:float=0.0}` (m).
- `materiales`: `{id, E:float (Pa), nu:float=0.2, densidad:float=2400.0 (kg/m³)}`.
- `secciones`: `{id, area (m²), inercia_y (m⁴), inercia_z (m⁴), constante_torsion (m⁴)}`.
- `elementos`: `{id, nodo_i:int, nodo_j:int, material_id, seccion_id, vector_referencia:[3]float=[0,0,1]}`.
- `apoyos`: `{nodo_id, ux,uy,uz,rx,ry,rz : bool=false}`.
- `cargas`: `{nodo_id, fx,fy,fz,mx,my,mz : float=0.0}` (**solo nodales**; no hay carga distribuida).
- Validación (`core/modelo.py::validar`): sin IDs de nodo duplicados; todo `nodo_i/nodo_j/material_id/
  seccion_id` debe existir; ningún elemento conecta un nodo consigo mismo; `apoyo.nodo_id` y
  `carga.nodo_id` deben existir.
- Coordenadas: **Z vertical (arriba)**, X–Y horizontal; unidades SI (m, N, Pa, kg/m³).

### 2.3 El visor NO dibuja losas hoy (motiva el corte 1a/1b)
`viz/escena.py:64` fija `"losas": []`; `app.js::renderEscena` (L841) solo recorre `escena.nodos` y
`escena.barras`. El código `losa*` de `app.js` es el modo de losa suelta (`--disenar-losa`), no paños
del edificio. Mostrar losas como superficie exige cambios en el repo del motor → **Etapa 1b**.

### 2.4 El visor deriva b×h de la sección (consistencia con el exportador)
`viz/escena.py::_dimensiones` recupera (b, h) desde `area` e `inercia_z` de la sección
(`h=√(12·Iz/A)`, `b=A/h`) y `_clasificar` marca `columna`/`viga` según domine Δz. Es decir: si el
exportador escribe `area` e `inercia_z` coherentes con una sección rectangular real, el visor
**reconstruye y dibuja** el grosor correcto sin más metadatos.

### 2.5 El modelo .NET no tiene frame explícito; sí geometría 2D + cotas
- `src/LosasPlus/Models/Columna.cs`: `CoordenadaX, CoordenadaY (m), Base, Peralte (m, sección),
  Altura (m), Zapata?`.
- `src/LosasPlus/Models/Viga.cs`: `OrigenX, OrigenY (m), AnguloGrados`, `Tramos` (con `Longitud`),
  `LongitudTotal`, `ExtremoX/ExtremoY` (computados), `Apoyos`.
- `Edificio.cs::Nivel`: `Cota (m)` + colecciones `Sistemas, Vigas, Columnas`.
- `src/Core/Models/Sistema.cs`: `Fc, Fy` (ton/cm²). **No guarda E.**
- `src/Core/Render3D/EscenaEdificio.cs::Construir` ya recorre niveles y dibuja columna como segmento
  vertical (`Cota → Cota+Altura`) y viga de `Origen` a `Extremo` en `Cota`. **Convención a reproducir.**

---

## 3. Decisiones de diseño (aprobadas con el usuario)

| # | Decisión | Elección |
|---|---|---|
| D1 | Alcance 1a | **Solo geometría del pórtico** (columnas + vigas). Cargas y losas-superficie fuera. |
| D2 | Enfoque | **Exportador dedicado** que recorre miembros reales, **alineado a las convenciones de coordenadas de `EscenaEdificio`** (no reusa su salida, que mezcla masa/huellas no estructurales). |
| D3 | Nodos | Sintetizados de extremos de columna/viga, **deduplicados por tolerancia** (1 mm). |
| D4 | Convención columna | Barra vertical `Cota → Cota+Altura` (igual que `EscenaEdificio`). |
| D5 | Convención viga | Barra horizontal `(OrigenX,OrigenY,Cota) → (ExtremoX,ExtremoY,Cota)`. |
| D6 | Secciones | Rectangular real desde b×h: `A=b·h`, `Iy=h·b³/12`, `Iz=b·h³/12`, `J` (torsión rect.). Columna: `Base×Peralte`. Viga: su sección; si falta → **default 0.30×0.50 m** (gap). |
| D7 | Materiales | Uno por `f'c` distinto. **E = 4700·√(f'c[MPa]) MPa** (ACI 318), `ν=0.2`, `densidad=2400 kg/m³`. f'c de `Sistema.Fc` (ton/cm² → Pa). |
| D8 | Apoyos | **Empotramiento (6 GDL)** en las bases de columna del nivel de fundación (las que tienen `Zapata`; si ninguna columna tiene zapata, las del nivel de menor `Cota`). |
| D9 | Cargas | **`cargas: []`** en 1a (modelo se resuelve trivialmente; geometría visible). |
| D10 | Entrega | **Exportar a archivo `.json`**; el usuario lo sube al visor. POST en vivo = Etapa 2. |
| D11 | Unidades/ejes | Geometría m→m; `f'c` ton/cm²→Pa para E; **Z arriba** (la `Cota` del nivel mapea a `z`). |

---

## 4. Arquitectura (componentes nuevos + reuso)

Tres piezas nuevas en `Core` + un comando en el ViewModel; el resto se reutiliza.

```
Edificio (Niveles → Columnas + Vigas)
   │
   ▼
[1] SintetizadorFrame  (Core/Services)
      recorre miembros reales → NodosFrame únicos (dedup por tolerancia)
      + ElementosFrame (barra por columna y por viga, con conectividad i/j)
   │
   ▼
[2] ExportadorModeloMotor  (Core/Services)
      arma el ModeloMotorDto completo:
        nodos  ← del sintetizador
        secciones ← b×h de cada miembro → {area, inercia_y, inercia_z, constante_torsion}
        materiales ← un material por f'c → {E (ACI), nu=0.2, densidad=2400}
        elementos ← barras + ref a material_id/seccion_id + vector_referencia
        apoyos ← bases de columna empotradas
        cargas ← []   (Etapa 1a)
      aplica conversiones de unidades (app → SI); valida integridad referencial.
   │
   ▼
[3] (System.Text.Json) → archivo .json del modelo del motor
   │
   ▼
   Usuario sube el .json al visor (subida de modelo propio → /visor) → ve el pórtico en 3D/VR
```

### 4.1 `SintetizadorFrame` (Core/Services)
Función pura `Edificio → (List<NodoFrame>, List<ElementoFrame>)`. Responsabilidad única: **topología**.
- Por cada `Columna` de cada `Nivel`: nodo inferior `(X, Y, Cota)`, nodo superior `(X, Y, Cota+Altura)`.
- Por cada `Viga` de cada `Nivel`: nodo inicio `(OrigenX, OrigenY, Cota)`, nodo fin `(ExtremoX,
  ExtremoY, Cota)`.
- **Dedup:** dos nodos a ≤ 1 mm en las 3 coordenadas son el mismo nodo (un solo `id`). Así una columna
  y una viga que comparten extremo quedan conectadas.
- Asigna `id` secuenciales a nodos y elementos. Cada `ElementoFrame` lleva su origen (columna/viga) y
  sus b×h para que [2] derive la sección.

### 4.2 `ExportadorModeloMotor` (Core/Services)
Función pura `Edificio → ModeloMotorDto`. Llama a `SintetizadorFrame`, luego:
- **Secciones:** dedup por (b, h); calcula `area, inercia_y, inercia_z, constante_torsion`.
- **Materiales:** dedup por `f'c`; `E = 4700·√(f'c_MPa)` MPa → Pa; `nu=0.2`, `densidad=2400`.
- **Elementos:** referencian `material_id`/`seccion_id`; **`vector_referencia` según orientación** para
  no degenerar los ejes locales: si el eje del elemento (i→j) es **vertical** (columna) → `[1,0,0]`;
  en cualquier otro caso → `[0,0,1]`. (El vector de referencia nunca debe ser colineal con el eje del
  elemento; un `[0,0,1]` fijo en columnas verticales haría singular/NaN el resolver.)
- **Apoyos:** bases de columna empotradas (D8).
- **Cargas:** `[]`.
- Valida integridad referencial (espejo de `core/modelo.py::validar`) antes de devolver.

### 4.3 DTOs del modelo motor (Core/Models)
`ModeloMotorDto` con `Nodos, Materiales, Secciones, Elementos, Apoyos, Cargas` y sus sub-DTOs, con
atributos `[JsonPropertyName]` que casen **exactamente** con el contrato del motor (`nodos`,
`nodo_i`, `inercia_y`, `constante_torsion`, `vector_referencia`, etc.).

### 4.4 Comando en el ViewModel (`MemoriaPlus/ViewModels/MainViewModel.cs`)
`ExportarModeloMotorAsync`: toma el `Edificio` activo → `ExportadorModeloMotor` → serializa →
guarda en archivo (file-picker "Modelo motor (*.json)"). Reporta éxito ("N nodos, M barras
exportados") o error claro. Botón "Exportar modelo para visor (FEA)" en la vista.

---

## 5. Flujo de datos (una exportación)

1. Usuario abre un edificio con columnas/vigas y pulsa **"Exportar modelo para visor (FEA)"**.
2. `SintetizadorFrame` recorre niveles → nodos únicos + barras.
3. `ExportadorModeloMotor` arma secciones, materiales, elementos, apoyos; `cargas=[]`.
4. Se valida integridad referencial. Si falla → error, no se escribe.
5. Se serializa a JSON y se guarda en el archivo elegido.
6. Status: "Exportado: N nodos, M barras → <archivo>".
7. (Manual) El usuario sube el `.json` al visor → ve el pórtico.

---

## 6. Conversión de unidades

| Magnitud | App | Motor (SI) | Conversión |
|---|---|---|---|
| Coordenadas X/Y, Cota, Altura | m | m | sin cambio |
| Sección b, h | m | m | sin cambio |
| `A` | — | m² | `b·h` |
| `Iy`, `Iz` | — | m⁴ | `h·b³/12`, `b·h³/12` |
| `J` (torsión rect.) | — | m⁴ | `β·h·b³` (b≤h), β de la razón h/b |
| `f'c` | ton/cm² | Pa (para E) | ton/cm² → MPa: `×98.0665`; luego `E=4700√(f'c_MPa)` MPa → Pa `×1e6` |
| `densidad` | — | kg/m³ | constante `2400` |

> 1 ton/cm² = 98.0665 MPa (tonf/cm²; tonf=1000 kgf, 1 kgf=9.80665 N, 1 cm²=1e-4 m²).
> Constante de torsión rectangular: `J = β·h·b³` con `b≤h` y `β≈⅓ − 0.21·(b/h)·(1 − (b/h)⁴/12)`.

---

## 7. Manejo de errores

- **Sin columnas ni vigas:** mensaje claro ("el edificio no tiene pórtico que exportar"); no escribe.
- **Sección inválida** (b o h ≤ 0): se usa la sección default `0.30×0.50 m` y se registra una
  advertencia (el miembro **no** se omite), sin caer.
- **f'c ausente/≤0:** default RD `f'c=21 MPa (210 kg/cm²)`.
- **Fallo de integridad referencial:** error con el detalle (espejo de `validar()`); no escribe.
- **Fallo de escritura de archivo:** error de I/O claro; la app no cae.

---

## 8. Estrategia de tests (`dotnet test`)

1. **`SintetizadorFrameTests`** — pórtico conocido (p. ej. 1 vano × 1 piso: 4 columnas + 4 vigas) →
   nodos esperados con dedup correcto (esquinas compartidas = 1 nodo) y conectividad i/j correcta.
2. **`SeccionRectangularTests`** — b×h → `A, Iy, Iz, J` con valores exactos (incluye J por la fórmula β).
3. **`MaterialMotorTests`** — `f'c` → `E` (ACI) + conversión de unidades (ton/cm² → Pa).
4. **`ApoyosFrameTests`** — bases de columna en fundación → apoyos empotrados (6 GDL) en los nodos
   correctos; resto de nodos sin apoyo.
5. **`ExportadorModeloMotorTests`** — export golden de un pórtico mínimo → `ModeloMotorDto` esperado;
   integridad referencial OK; `cargas` vacío; nombres JSON exactos del contrato.
6. **Integración (guardada)** — si el motor está disponible, correr `--analyze`/`visor` real sobre el
   JSON exportado y verificar que **no es singular** y que `escena` reporta el nº de barras/nodos
   esperado. Se omite si no hay motor. (Espejo del test de integración guardado de #5a.)

---

## 9. Archivos previstos

**Nuevos (`Core`):**
- `Core/Services/SintetizadorFrame.cs` (+ `NodoFrame`, `ElementoFrame` internos).
- `Core/Services/ExportadorModeloMotor.cs` (+ helpers de sección/material).
- `Core/Models/ModeloMotorDto.cs` (DTOs serializables del contrato del motor).

**Modificados:**
- `MemoriaPlus/ViewModels/MainViewModel.cs`: comando `ExportarModeloMotorAsync` + estado.
- Vista de MemoriaPlus: botón "Exportar modelo para visor (FEA)".

**Reutilizados sin cambio:** convenciones de `EscenaEdificio`, modelo de dominio (`Edificio`, `Nivel`,
`Columna`, `Viga`, `Sistema`), infra de tests .NET.

**Tests:** `tests/LosasPlus.Tests/` — los cinco grupos de §8 + integración guardada.

---

## 10. Limitaciones heredadas / gaps conocidos (Etapa 1a)

- **Sin cargas** (`cargas: []`): deformada plana en el visor. Cargas reales (losa por área tributaria,
  peso propio) = Etapa 2.
- **Losas no visibles** como superficie (el visor no las dibuja): Etapa 1b (toca el repo del motor).
- **Muros** no exportados.
- **Vigas sin sección** usan default `0.30×0.50 m`.
- **Material único por f'c**; E por fórmula ACI (la app no guarda E).
- **Apoyos** solo empotramiento en fundación; sin apoyos parciales/resortes ni continuidad real de
  fundación.
- **`vector_referencia`** elegido por orientación (`[1,0,0]` columnas verticales, `[0,0,1]` resto);
  la orientación fina de ejes locales se valida en el test de integración.
- **Entrega manual** (archivo → subida al visor); el lazo en vivo (POST + recálculo) = Etapa 2.

---

## 11. Próximo paso

Invocar `superpowers:writing-plans` para escribir el plan de implementación detallado (TDD) a partir
de esta spec.
