<h1 align="center">EstructurasRD</h1>

<p align="center">
  <b>Suite de análisis y diseño estructural para ingeniería civil dominicana (R-001 / ACI 318).</b><br/>
  Núcleo: un <b>motor FEA en Python</b> con visor web/WebXR y módulo de incidencias en realidad virtual.<br/>
  Incluye además la suite de escritorio .NET/Avalonia (LosasPlus / MemoriaPlus) como cliente de memoria <code>.docx</code>.
</p>

<p align="center">
  <img alt="python" src="https://img.shields.io/badge/motor-Python%203.11+-3776AB?logo=python&logoColor=white">
  <img alt="dotnet" src="https://img.shields.io/badge/escritorio-.NET%208%20%C2%B7%20Avalonia-512BD4?logo=dotnet&logoColor=white">
  <img alt="license MIT" src="https://img.shields.io/badge/license-MIT-blue">
  <img alt="status" src="https://img.shields.io/badge/estado-en%20desarrollo%20activo-orange">
</p>

> **Autor:** Emil Guillén De la Cruz · GitHub [@dashy2004](https://github.com/dashy2004)

---

## Qué es

EstructurasRD es un **monorepo** cuyo **núcleo de verdad es el motor FEA en Python**
([`motor-fea/`](motor-fea/)): resuelve marcos 3D por rigidez directa, análisis modal,
diafragmas rígidos y losas por FEM, aplica las combinaciones y reglas normativas
(R-001 / ACI 318), y **expone todo como datos neutrales** que un visor web (y VR)
consume directamente.

La **suite de escritorio .NET/Avalonia** (LosasPlus / MemoriaPlus, en la raíz del
repo) se conserva por su capacidad única: la **generación de memoria de cálculo
`.docx`**. Su dirección es reposicionarse como **cliente del motor** (ver Roadmap).

## Estructura del repo

```
motor-fea/            ← NÚCLEO: motor FEA en Python (la línea de desarrollo activa)
  src/motor_fea/
    core/             FEA puro: modelo, solver (rigidez 12 GDL), modal, diafragma, losa_fem, placa
    normativa/        R-001, ACI 318, combinaciones de carga
    viz/              DTOs JSON neutrales (escena, resultados, diseño, armado, georref) + incidencias
    api/              FastAPI (frontera HTTP) + CLI (contrato JSON)
    viz/static/       visor three.js / WebXR (geometría, deformada, modos, heatmaps, VRButton)

src.Core/  src.Memoria/  src.UI.Shared/  src.Linux/   ← suite de escritorio .NET/Avalonia
LosasPlus.sln  LosasPlus.Linux.sln                     (cliente de memoria .docx)
```

La separación del motor **`core` (cálculo) → `viz` (datos) → `static` (render)** es la
clave de escalabilidad: features visuales nuevas (diagramas, vista en secciones) se
agregan en `viz/` + un endpoint + el visor, **sin tocar el solver**.

## Motor FEA — capacidades

- **Marcos 3D** por rigidez directa (6 GDL/nodo) con esfuerzos internos por elemento
  evaluables en cualquier estación `t ∈ [0,1]`.
- **Análisis modal** (formas y períodos) y **diafragma rígido**.
- **Losas por FEM** (placa, malla rectangular) con deflexión y momentos nodales.
- **Normativa**: R-001, ACI 318, combinaciones de carga.
- **Diseño/armado** de refuerzo y visualización del armado en 3D.
- **IA local** opcional (clasificación/asistencia) vía el extra `ia`.

## Motor — visor web + VR

Levanta el visor (sirve un pórtico de ejemplo si no pasas un modelo):

```bash
cd motor-fea
pip install -e '.[api]'
motor-fea --serve                 # http://127.0.0.1:8000
motor-fea --serve modelo.json     # sirve tu propio modelo
```

El visor (`motor-fea/src/motor_fea/viz/static/`) carga la geometría desde `/escena`, la
deformada y los modos desde `/resultados`, heatmaps de losa desde `/losa`, y soporta
**WebXR** (botón VR).

**Módulo de Incidencias VR** (`viz/static/incidencias/`): visor de incidencias en obra
con carga glTF, marcadores georreferenciados, ficha, clasificación IA e import/export.

## Motor — CLI (frontera de integración)

```bash
cd motor-fea
motor-fea --version
motor-fea --analyze modelo.json        # resultados JSON por stdout
cat modelo.json | motor-fea --analyze - # '-' = leer de stdin
motor-fea --disenar-losa params.json   # diseño de losa por FEM → JSON
```

El esquema JSON de entrada/salida está documentado en `motor_fea.api.contrato`.

## Motor — desarrollo

```bash
cd motor-fea
python -m venv .venv && . .venv/bin/activate
pip install -e '.[api,ia,dev]'
pytest
```

## Suite de escritorio .NET (memoria `.docx`)

La suite Avalonia (multiplataforma Linux/Windows/macOS) vive en la raíz del repo.
Build y ejecución:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build LosasPlus.Linux.sln -c Debug
dotnet run --project src.Linux       -c Debug   # LosasPlus
dotnet run --project src.Memoria     -c Debug   # MemoriaPlus (generador de memoria .docx)
```

Detalles del port a Avalonia en [`BUILD-Linux.md`](BUILD-Linux.md). Su rol futuro es
delegar el cálculo/diagramas al motor en vez de duplicar lógica.

## Roadmap

- **#0** Reconciliación del repo (este README, `main` = monorepo engine-first). *(en curso)*
- **#1** API de escritura (POST-modelo) + esfuerzos por elemento.
- **#2** Shell de interfaz nueva (web/WebXR): entrada/edición de modelo, navegación.
- **#3** Diagramas de esfuerzos (M / V / δ) en el visor.
- **#4** Vista en secciones (corte por plano del modelo + campos en el corte).
- **#5** Reposicionar la suite .NET como cliente de memoria del motor.

## Licencia

[MIT](LICENSE). La suite .NET interopera opcionalmente con `Losas.exe`
(Ing. Francisco E. Perdomo, método Pieper-Martens), **no cubierto por esta licencia**
y que el usuario debe obtener directamente del autor.
