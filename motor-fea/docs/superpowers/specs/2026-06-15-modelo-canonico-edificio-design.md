# Rebanada A · Modelo canónico del edificio — Diseño

**Fecha:** 2026-06-15
**Estado:** Diseño aprobado (brainstorm). Pendiente: plan de implementación.
**Rama destino:** `engine/shell-web-webxr` (repo motor `motor-fea`).

## Contexto

Adaptación de la suite .NET (LosasPlus/MemoriaPlus) a la visión actual (motor Python = cálculo,
web/VR = visualización). El alcance total se descompuso en sub-proyectos (A modelo canónico ·
B bajada de cargas · C entrada CAD web · D autoría web · E memoria `.docx` · F IA local). **Todo
cuelga de A.** Esta spec cubre **solo A**.

### Por qué A primero (el bug que resuelve)

El modelo .NET nació centrado en **`Sistema`** (cada sistema = un `.DL` byte-compatible con
`Losas.exe`) y **`Nivel` se le agregó encima** con una "fachada de compatibilidad" (documentada
en `Proyecto`/`Sistema.cs`). Consecuencias reales reportadas por el usuario:

1. **Nivel y Sistema enredados:** el nombre del nivel termina siendo el del sistema; una elevación
   asignada al nivel **no baja** al sistema (el sistema no está realmente "dentro de" un nivel).
2. **Verticales no conectan niveles:** una columna/muro que pasa de una elevación a la del nivel
   superior **no se refleja arriba**; no existe el concepto "esta columna va del nivel N al N+1",
   por lo que la **transmisión de cargas entre niveles queda en duda**.

Como la era `Losas.exe` se está jubilando (el motor Python la reemplaza), se define el modelo
**limpio desde cero**, sin esa deuda.

## Decisiones (del brainstorm)

- **D1 — Nivel = Sistema (unificado).** Un solo concepto: planta con su cota **y** sus losas. Se
  elimina la dualidad de botones. (Se acepta no soportar varias zonas de losa distintas en una
  misma planta.)
- **D2 — Verticales continuas.** Columna/muro se define **una vez** como elemento vertical con un
  rango de cota (`cota_base → cota_tope`); atraviesa los niveles que le corresponden. La conexión y
  la base para la bajada de cargas quedan correctas por construcción.
- **D3 — Enfoque 1: modelo nuevo, dueño el motor.** Contrato JSON versionado cuya fuente de verdad
  vive en el motor Python (de ahí lo consumen FEA, visor y memoria). La web y el exportador .NET
  hablarán este mismo contrato.
- **D4 — Sin importador.** Se empieza de cero; no se migran proyectos `.lpx.json` viejos.
- **D5 — Rebrand EstructurasRD.** El modelo nuevo usa la convención de nombres EstructurasRD; cero
  identificadores `losasplus` en el código nuevo.

## Alcance

**Incluye:**
- El **modelo canónico** del edificio como **contrato JSON versionado** (`version` en la raíz).
- Implementación de referencia en el **motor Python** (dataclasses + parseo/serialización).
- **Validación** del modelo con reportes claros.
- Pruebas (TDD), incluidos los escenarios del bug como tests de no-regresión.

**No incluye (rebanadas siguientes):**
- Interfaz web (D).
- Síntesis a malla FEA: modelo de autoría → nodos/barras con nodos compartidos (rebanada que
  consume A; relacionada con B y con el exportador actual).
- Bajada de cargas (B), entrada CAD (C), memoria `.docx` (E), IA local (F).
- Importador de proyectos viejos (descartado, D4).

## El modelo

Jerarquía:

```
Proyecto
 ├─ metadata (nombre, autor, codigo_obra, ubicacion, fecha)
 ├─ cargas_globales + combinaciones
 └─ edificios[]
     ├─ niveles[]              (ordenados por cota, crecientes)
     └─ elementos_verticales[] (columnas/muros CONTINUOS; viven en el edificio porque atraviesan niveles)
```

### Entidades

- **Proyecto** — raíz. Metadata + cargas globales + combinaciones + uno o más edificios.
- **Edificio** — `id`, `nombre`, `niveles[]`, `elementos_verticales[]`.
- **Nivel** (= planta = sistema unificado) — `id`, `nombre` (independiente), **`cota`** (elevación
  en m, SI, Z arriba), y **`losas[]`** (el "sistema" de losas de la planta). La cota la manda el
  nivel; las losas la heredan. *(Resuelve la mitad #1 del bug.)*
- **Losa** — `id`, `tipo` (catálogo de tipos de losa), `espesor` (m), `puntos` (contorno en planta
  `[[x,y], …]`, a la cota del nivel), `cargas` (muerta/viva). *Nota de consistencia con 1b: en el
  contrato del visor/escena la losa lleva `puntos:[[x,y,z]×4]`; aquí el contorno es en planta y la
  cota la aporta el nivel — la síntesis (rebanada siguiente) combina ambos.*
- **ElementoVertical** (columna o muro), **continuo** — `id`, `tipo` (`columna`|`muro`); para
  columna `posicion:[x,y]`, para muro `linea:[[x1,y1],[x2,y2]]`; `seccion` (columna: `base`,
  `peralte`; muro: `espesor`); **`cota_base`** y **`cota_tope`** (rango vertical que atraviesa);
  `material`; `zapata` opcional (en la base, típicamente columnas). *(Resuelve la mitad #2 del
  bug.)*

### Contrato JSON (ejemplo, `version: 1`)

```json
{
  "version": 1,
  "proyecto": {
    "nombre": "Edificio demo",
    "autor": "…",
    "codigo_obra": "…",
    "ubicacion": "…",
    "fecha": "2026-06-15"
  },
  "cargas_globales": { "muerta_adicional": 1.5, "viva": 2.0 },
  "combinaciones": ["1.2D+1.6L"],
  "edificios": [
    {
      "id": 1,
      "nombre": "Bloque A",
      "niveles": [
        { "id": 1, "nombre": "Primer nivel", "cota": 0.0,
          "losas": [ { "id": 1, "tipo": "maciza", "espesor": 0.20,
                       "puntos": [[0,0],[5,0],[5,5],[0,5]],
                       "cargas": { "muerta": 1.5, "viva": 2.0 } } ] },
        { "id": 2, "nombre": "Segundo nivel", "cota": 3.0,
          "losas": [ { "id": 2, "tipo": "maciza", "espesor": 0.20,
                       "puntos": [[0,0],[5,0],[5,5],[0,5]],
                       "cargas": { "muerta": 1.5, "viva": 2.0 } } ] }
      ],
      "elementos_verticales": [
        { "id": 1, "tipo": "columna", "posicion": [0,0],
          "seccion": { "base": 0.30, "peralte": 0.30 },
          "cota_base": 0.0, "cota_tope": 6.0, "material": "H210",
          "zapata": { "ancho": 1.2, "largo": 1.2, "peralte": 0.4 } },
        { "id": 2, "tipo": "muro", "linea": [[0,0],[0,5]],
          "seccion": { "espesor": 0.20 },
          "cota_base": 0.0, "cota_tope": 6.0, "material": "H210" }
      ]
    }
  ]
}
```

(La columna `id:1` con `cota_base:0 → cota_tope:6` **atraviesa** los niveles a cota 0, 3 y 6 →
queda conectada a los tres.)

## Validación

El modelo verifica y reporta (lista de errores legibles, estilo `validar()` del motor):

- **Niveles:** cotas **estrictamente crecientes y únicas** por edificio; `nombre` libre (no derivado
  de la losa/sistema); al menos un nivel por edificio.
- **Verticales:** `cota_base < cota_tope`; ambas **alineadas con cotas de niveles existentes** (la
  base puede ser fundación = cota mínima del edificio); sección y geometría positivas.
- **Passing-through:** se computa **qué niveles atraviesa** cada vertical (los niveles cuya cota
  cae en `[cota_base, cota_tope]`) → base explícita para la futura transmisión de cargas.
- **Losas:** contorno con ≥3 puntos; espesor > 0; `tipo` dentro del catálogo conocido.
- **IDs** únicos dentro de su colección.

## Dónde vive / naming

- Módulo nuevo en el **motor Python** (`motor-fea`), fuente de verdad del contrato de autoría
  (distinto del `ModeloEstructural` FEA de bajo nivel, que sigue siendo nodos/barras/losas).
- Convención de nombres **EstructurasRD** (sin `losasplus`).
- Contrato **versionado** (`version` en la raíz) para evolucionar sin romper consumidores
  (web, exportador .NET, síntesis FEA).

## Pruebas (TDD)

- Round-trip **parse → serialize → parse** del contrato JSON (estabilidad del contrato).
- Casos de validación: cotas desordenadas/duplicadas, vertical con `cota_base ≥ cota_tope`, vertical
  con cota fuera de los niveles, losa con <3 puntos / espesor ≤ 0, IDs duplicados.
- **Escenarios del bug como tests de no-regresión:**
  - "la cota del nivel se propaga a sus losas" (no se puede asignar cota al nivel sin que las losas
    queden a esa elevación).
  - "columna continua `0→6` queda conectada a los 3 niveles (0, 3, 6)" (passing-through correcto).
  - "el nombre del nivel es independiente del de la losa".

## Follow-ups (fuera de A, registrados)

- **Síntesis FEA** (modelo de autoría → nodos/barras con **nodos compartidos** entre niveles para
  que la carga baje) — siguiente rebanada; realiza lo que A deja preparado.
- **Bajada de cargas** (B) consumirá el passing-through.
- **Rebrand** completo de los identificadores `losasplus` en el resto del código (D5 aquí solo
  aplica al modelo nuevo).

## Self-review (cobertura)

- Bug mitad #1 (nivel↔sistema, cota) → D1 + Nivel.cota + test de propagación.
- Bug mitad #2 (verticales/transmisión) → D2 + ElementoVertical continuo + passing-through + test.
- Rebrand → D5 + naming EstructurasRD.
- "Empezar de cero" → D4 (sin importador).
- Dueño = motor → D3 + sección "Dónde vive".
