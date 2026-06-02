# Visión y Roadmap — EstructurasRD

## Visión

**EstructurasRD** es una herramienta de **planificación y diseño estructural y
urbano**, pieza del **gemelo digital de la República Dominicana** (junto a
**IncidenciasRD**, que reporta incidencias de todo tipo). El recorrido del
producto:

> **edificaciones (ahora)** → **obras de arte** (estructuras civiles) → **mapa 3D
> urbano** (gemelo digital) → **integración con IncidenciasRD** para planificación
> y diseño urbano.

El foco actual es dejar las **edificaciones bien pulidas** antes de expandir.

## Estado actual (Hito 2 — junio 2026)

App Avalonia / .NET 8 multiplataforma (Linux primario). Edificaciones: losas
(cálculo en vivo), vigas continuas (rigidez directa + diagramas V/M/δ), columnas
y zapatas, **transmisión de cargas topológica** losa→viga→columna→zapata por
geometría en planta, vista 3D alámbrica sin SharpDX, memoria `.docx`, export
**SAF** y **XLSX**. (Ver `MEMORIA_CLAUDE.md`.)

## Estándares y modelos de referencia (investigados)

Para no reinventar y poder integrarse al ecosistema de gemelos digitales:

| Estándar / herramienta | Para qué | Estado |
|---|---|---|
| **SAF** (Structural Analysis Format, IDEA StatiCa) | Intercambio del modelo de análisis estructural | ✅ implementado |
| **IFC 4.3** (buildingSMART, ISO) — openBIM | Edificios **e infraestructura** (puentes, drenaje, geotecnia, geometría georreferenciada). La pieza clave para obras de arte + interoperabilidad BIM | 🔜 Fase K |
| **CityGML** (OGC) | Modelo de datos 3D de la ciudad (edificios, vías, puentes, vegetación) | 🔜 Fase M |
| **3D Tiles** (OGC) + **CesiumJS** | Streaming/render del mapa 3D urbano en navegador, a escala ciudad/país | 🔜 Fase M |
| **3DCityDB** | Base geoespacial open-source para CityGML / gemelos digitales | 🔜 Fase M |

## Fases futuras

### Fase K — Pulido + interoperabilidad de edificaciones
- **Pulir el motor:** casos límite, unidades coherentes (kN/t), combinaciones
  normativas DR (R-001, R-024) + ACI 318, validaciones más ricas, integrar el
  descenso geométrico (J.15–J.20) a la UI con un comando único.
- **IFC 4.3 export/import** (openBIM) además de SAF → el modelo dialoga con BIM/GIS.
- **Georreferenciación:** coordenadas geográficas + parcela por edificio
  (cimiento para el mapa 3D).

### Fase L — Obras de arte (estructuras civiles)
- Extender el dominio (`src.Core`): puentes, muros de contención, alcantarillas,
  tanques, geotecnia. Reusar el motor de rigidez directa.
- Modelarlas con **IFC 4.3 infra** (alignment, bridge structural, drainage…).

### Fase M — Mapa 3D / gemelo digital urbano
- Export del modelo georreferenciado a **CityGML / 3D Tiles**.
- Visor **CesiumJS** (web) del territorio con los edificios/obras 3D.
- Persistencia geoespacial con **3DCityDB**.

### Fase N — Integración IncidenciasRD
- Vincular **incidencias** (reportes) a estructuras georreferenciadas en el mapa.
- Capa de análisis urbano: riesgo, planificación, diseño sobre el gemelo digital.

## Arquitectura recomendada

- **`src.Core` puro y multiplataforma** sigue siendo el motor reusable (cálculo
  + transmisión de cargas). NO acoplar a UI.
- **Capa de interoperabilidad** `src.Core/Interop`: SAF (hecho) → IFC → CityGML.
  Cada exportador/importador puro y testeable headless (mismo patrón que SAF).
- El **mapa 3D urbano** (Fase M) probablemente sea **web (CesiumJS)** consumiendo
  los datos georreferenciados / 3D Tiles que produce el motor — separado de la
  app desktop Avalonia, pero compartiendo el modelo de dominio vía IFC/CityGML.
- **IncidenciasRD** se integra a nivel de datos georreferenciados (no de la app
  estructural directamente): ambos productos comparten el sustrato geoespacial
  del gemelo digital.

## División con Antigravity (resumen; detalle en `DIVISION_TRABAJO.md`)

- **Claude Code (motor/interop, headless):** pulido del motor, exportadores
  IFC/CityGML, georreferenciación, dominio de obras de arte. Todo `src.Core`
  puro + testeado.
- **Antigravity (UI/visual):** editor 2D de planta, refinamiento del visor 3D
  desktop, y —en Fase M— el frontend del **mapa 3D (CesiumJS)** y su integración
  visual con IncidenciasRD.

## Fuentes (investigación)

- buildingSMART — IFC 4.3 (openBIM edificios e infraestructura, estándar ISO).
- OGC — CityGML, 3D Tiles.
- Cesium — CesiumJS / Cesium ion para gemelos digitales urbanos.
- 3DCityDB — base geoespacial CityGML open-source.
