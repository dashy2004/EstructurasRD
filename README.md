<h1 align="center">EstructurasRD · Motor FEA</h1>

<p align="center">
  <b>Motor de análisis y diseño estructural (FEA) en Python para ingeniería civil dominicana,
  con visor web/WebXR y módulo de incidencias en realidad virtual.</b><br/>
  Núcleo de cálculo puro (marcos 3D, modal, losas FEM) · normativa R-001 / ACI 318 ·
  API FastAPI · visor three.js con soporte VR.
</p>

<p align="center">
  <img alt="python" src="https://img.shields.io/badge/python-3.11+-3776AB?logo=python&logoColor=white">
  <img alt="license MIT" src="https://img.shields.io/badge/license-MIT-blue">
  <img alt="status" src="https://img.shields.io/badge/estado-en%20desarrollo%20activo-orange">
</p>

> **Autor:** Emil Guillén De la Cruz · GitHub [@dashy2004](https://github.com/dashy2004)

---

## Qué es

EstructurasRD es un **motor de elementos finitos (FEA)** para el diseño estructural
bajo normativa dominicana (R-001) y ACI 318. Resuelve marcos 3D por rigidez directa,
análisis modal, diafragmas rígidos y losas por FEM, aplica las combinaciones y reglas
normativas, y **expone todo como datos neutrales** que un visor web (y VR) consume
directamente.

El código del motor vive en [`motor-fea/`](motor-fea/).

El sistema tiene **dos formas de uso** que se complementan — el **visor web/VR** (que
sirve el propio motor) y la **app de escritorio** de autoría (LosasPlus / MemoriaPlus).
Ver **[Cómo acceder](#cómo-acceder)** para correr cada una.

> **Suite de escritorio .NET/Avalonia** (LosasPlus / MemoriaPlus): app de autoría de
> modelos + generación de memoria de cálculo `.docx`. Vive en la rama
> [`archive/dotnet-suite`](../../tree/archive/dotnet-suite) (y los tags `archive/dotnet/*`).
> Construye el edificio (niveles, losas, columnas, cargas) y lo **exporta al contrato JSON
> del motor** para verlo en el visor; su dirección de roadmap es consolidarse como
> **cliente de memoria** de este motor (#5).

## Cómo acceder

Hay dos piezas que puedes correr; lo normal es usar las dos juntas (autoría en la app →
visualización en el visor).

### 1) El visor web / VR (motor)

Sirve el modelo 3D en el navegador (y en VR con WebXR). Es lo que muestra geometría,
deformada, modos, mapas de calor de losa y armado. Requiere **Python 3.11+**.

```bash
git clone https://github.com/dashy2004/EstructurasRD.git
cd EstructurasRD/motor-fea
python -m venv .venv && . .venv/bin/activate
pip install -e '.[api]'

motor-fea --serve                 # abre en http://127.0.0.1:8000
motor-fea --serve modelo.json     # carga tu propio modelo (contrato JSON del motor)
```

Luego abre **http://127.0.0.1:8000** en el navegador. Sin `modelo.json` sirve un pórtico
de ejemplo. Para verlo desde el **celular o un visor Quest** en la misma red Wi-Fi, usa
`motor-fea --serve --host 0.0.0.0` y entra desde el dispositivo a `http://<IP-de-tu-PC>:8000`
(el botón **VR** aparece en visores con WebXR). Detalle de endpoints y modos en
[Visor web + VR](#visor-web--vr).

### 2) La aplicación de escritorio (LosasPlus / MemoriaPlus)

App de autoría .NET 8 / Avalonia (Linux · Windows · macOS): construyes el edificio
(niveles, losas, columnas, cargas), calculas, generas la **memoria `.docx`**, y exportas
el modelo al **contrato JSON del motor** para abrirlo en el visor de arriba. Vive en la
rama **[`archive/dotnet-suite`](../../tree/archive/dotnet-suite)**. Requiere el **SDK de .NET 8**.

```bash
git clone https://github.com/dashy2004/EstructurasRD.git
cd EstructurasRD
git checkout archive/dotnet-suite

dotnet build LosasPlus.Linux.sln -c Debug          # compila la suite
dotnet run --project src         -c Debug          # LosasPlus  (autoría de losas)
dotnet run --project src.Memoria -c Debug          # MemoriaPlus (memoria .docx)
```

En Windows puedes generar ejecutables `.exe` self-contained (no requieren instalar .NET);
los pasos de build/publish multiplataforma están en
[`BUILD-Linux.md`](../../blob/archive/dotnet-suite/BUILD-Linux.md) de esa rama.

> El flujo app → visor (exportar el modelo de la app al JSON que consume el motor) está en
> **desarrollo activo** en la línea del editor de planta; el motor ya acepta ese contrato
> (`motor-fea --serve modelo.json`).

## Arquitectura

```
motor-fea/src/motor_fea/
  core/       FEA puro: modelo, solver (rigidez 12 GDL), modal, diafragma, losa_fem, placa
  normativa/  R-001, ACI 318, combinaciones de carga
  viz/        DTOs JSON neutrales (escena, resultados, diseño, armado, georref) + incidencias
  api/        FastAPI (frontera HTTP) + CLI (contrato JSON)
  viz/static/ visor three.js / WebXR (geometría, deformada, modos, heatmaps, VRButton)
```

La separación **`core` (cálculo) → `viz` (datos) → `static` (render)** es la clave de
escalabilidad: features visuales nuevas (diagramas, vista en secciones) se agregan en
`viz/` + un endpoint + el visor, **sin tocar el solver**.

## Capacidades

- **Marcos 3D** por rigidez directa (6 GDL/nodo) con esfuerzos internos por elemento
  evaluables en cualquier estación `t ∈ [0,1]`.
- **Análisis modal** (formas y períodos) y **diafragma rígido**.
- **Losas por FEM** (placa, malla rectangular) con deflexión y momentos nodales.
- **Normativa**: R-001, ACI 318, combinaciones de carga.
- **Diseño/armado** de refuerzo y visualización del armado en 3D.
- **IA local** opcional (Qwen vía Ollama / Anthropic) — solo lectura: analiza
  PDF/DXF/imágenes y propone elementos; nunca modifica código. Ver `docs/qwen-setup.md`.

## Visor web + VR

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

## CLI (frontera de integración)

```bash
cd motor-fea
motor-fea --version
motor-fea --analyze modelo.json        # resultados JSON por stdout
cat modelo.json | motor-fea --analyze - # '-' = leer de stdin
motor-fea --disenar-losa params.json   # diseño de losa por FEM → JSON
```

El esquema JSON de entrada/salida está documentado en `motor_fea.api.contrato`.

## Desarrollo

```bash
cd motor-fea
python -m venv .venv && . .venv/bin/activate
pip install -e '.[api,ia,dev]'
pytest
```

## Roadmap

- **#0** Reconciliación del repo: `main` = motor engine-only. *(hecho)*
- **#1** API de escritura (POST-modelo) + esfuerzos por elemento.
- **#2** Shell de interfaz nueva (web/WebXR): entrada/edición de modelo, navegación.
- **#3** Diagramas de esfuerzos (M / V / δ) en el visor.
- **#4** Vista en secciones (corte por plano del modelo + campos en el corte).
- **#5** Reposicionar la suite .NET (`archive/dotnet-suite`) como cliente de memoria.

## Licencia

[MIT](LICENSE). La suite .NET archivada interopera opcionalmente con `Losas.exe`
(Ing. Francisco E. Perdomo, método Pieper-Martens), **no cubierto por esta licencia**
y que el usuario debe obtener directamente del autor.
