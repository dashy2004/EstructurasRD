# Reconciliación GitHub + README (engine-first) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: este plan toca un **repo público**
> con operaciones **destructivas e irreversibles-en-caliente** (force-push a
> `main`, borrado de ramas remotas). Los pasos marcados **[GATE]** EXIGEN
> confirmación humana explícita antes de ejecutarse. **Se recomienda ejecución
> inline con checkpoints (no subagentes desatendidos).** Pasos con checkbox
> (`- [ ]`).

**Goal:** Dejar `dashy2004/EstructurasRD` coherente con la dirección A — `main`
= motor Python, suite .NET preservada sin pérdida, 12 ramas podadas a 3 + tags,
README reescrito engine-first y workflows .NET retirados — de forma reversible.

**Architecture:** Historia no relacionada entre motor y .NET ⇒ se publica el
motor como `main` por reemplazo (force-push), tras archivar todo lo .NET con
tags inmutables (`archive/dotnet/*`) y una rama viva (`archive/dotnet-suite`).
Cada acción destructiva es reversible reconstruyendo desde los tags.

**Tech Stack:** git, GitHub CLI (`gh`), Markdown.

**Spec:** `docs/superpowers/specs/2026-06-13-github-reconciliacion-readme-engine-first-design.md`

**Orden invariante (spec §9):** tags → push tags → verificar → `archive/dotnet-suite`
→ force-push `main` → push incidencias → borrar ramas → retirar workflows → limpieza local.

---

## ⚠️ ACTUALIZACIÓN (2026-06-13) — monorepo + estado de ejecución

**Corrección:** `master` es un **MONOREPO** (suite .NET en raíz + motor en
`motor-fea/`), no la línea del motor pura. Por eso:

- **Task 1** se ejecutó reescribiendo el **README de RAÍZ** en versión
  engine-first/monorepo. El bloque de README dentro de Task 1 (titulado
  "EstructurasRD · Motor FEA") era la versión *engine-only* previa al hallazgo y
  quedó **superado**. README real: ver `README.md` en `master` (`3828c13`).
- **Task 5** (force-push `master`→`main`) publica el **monorepo completo** + README nuevo.

**Estado de ejecución (opción "solo lo seguro"):**
- ✅ Task 0 (snapshot) · Task 1 (README raíz, commit `3828c13`, **no pusheado**) ·
  Task 2 (11 tags) · Task 3 (push tags) · Task 4 (`archive/dotnet-suite`) — **HECHAS**.
- ⏸️ Tasks 5–9 (force-push `main`, push incidencias, poda, workflows, limpieza local) —
  **PENDIENTES**, requieren gate humano. `origin/main` **intacto**.

---

### Task 0: Snapshot de seguridad (solo lectura)

**Files:** ninguno (operación git de lectura).

- [ ] **Step 1: Sincronizar refs remotas**

Run: `git fetch --all --prune --tags`
Expected: descarga refs de `origin`; sin errores.

- [ ] **Step 2: Capturar el estado actual a un archivo de respaldo fuera de git**

Run:
```bash
{ echo "# Snapshot pre-#0 $(date -u +%FT%TZ)";
  echo "## ramas remotas"; git for-each-ref --format='%(objectname) %(refname:short)' refs/remotes/origin;
  echo "## ramas locales"; git for-each-ref --format='%(objectname) %(refname:short)' refs/heads;
  echo "## tags"; git tag; } > /home/gdc/Downloads/estructurasrd-snapshot-pre0.txt
cat /home/gdc/Downloads/estructurasrd-snapshot-pre0.txt
```
Expected: archivo con los SHAs de cada rama remota/local y tags. **Esta es la
red de seguridad humana** además de los tags git.

- [ ] **Step 3: Confirmar invariantes del spec**

Run:
```bash
git merge-base master origin/main || echo "OK: sin ancestro común (esperado)"
git merge-base --is-ancestor engine/incidencias-vr-mvp master && echo "incid EN master" || echo "OK: master detrás de incidencias (esperado)"
```
Expected: "OK: sin ancestro común" y "OK: master detrás de incidencias".

---

### Task 1: README engine-first en `master`

**Files:**
- Modify: `README.md` (en la rama `master`)

- [ ] **Step 1: Cambiar a `master`**

Run: `git switch master`
Expected: "Switched to branch 'master'". (Los archivos del visor de incidencias
desaparecen del working tree — es correcto, viven en su rama.)

- [ ] **Step 2: Reescribir `README.md` con este contenido exacto**

````markdown
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

La suite de escritorio **.NET/Avalonia** original (LosasPlus / MemoriaPlus) se preserva
en la rama [`archive/dotnet-suite`](../../tree/archive/dotnet-suite); su rol futuro es
ser **cliente de memoria `.docx`** apoyado en este motor (ver Roadmap #5).

## Arquitectura

```
core/   →  FEA puro: modelo, solver (rigidez 12 GDL), modal, diafragma, losa_fem, placa
normativa/ → R-001, ACI 318, combinaciones de carga
viz/    →  DTOs JSON neutrales (escena, resultados, diseño, armado, georref) + incidencias
api/    →  FastAPI (frontera HTTP) + CLI (contrato JSON, paridad con el patrón Losas.exe)
viz/static/ → visor three.js / WebXR (geometría, deformada, modos, heatmaps, VRButton)
```

La separación **`core` (cálculo) → `viz` (datos) → `static` (render)** es la clave de
escalabilidad: features visuales nuevas (diagramas, vista en secciones) se agregan en
`viz/` + un endpoint + el visor, **sin tocar el solver**.

## Capacidades del motor

- **Marcos 3D** por rigidez directa (6 GDL/nodo) con esfuerzos internos por elemento
  evaluables en cualquier estación `t ∈ [0,1]`.
- **Análisis modal** (formas y períodos) y **diafragma rígido**.
- **Losas por FEM** (placa, malla rectangular) con deflexión y momentos nodales.
- **Normativa**: R-001, ACI 318, combinaciones de carga.
- **Diseño/armado** de refuerzo y visualización del armado en 3D.
- **IA local** opcional (clasificación/asistencia) vía el extra `ia`.

## Visor web + VR

Levanta el visor (sirve un pórtico de ejemplo si no pasas un modelo):

```bash
pip install -e '.[api]'
motor-fea --serve                 # http://127.0.0.1:8000
motor-fea --serve modelo.json     # sirve tu propio modelo
```

El visor (`viz/static/`) carga la geometría desde `/escena`, la deformada y los modos
desde `/resultados`, heatmaps de losa desde `/losa`, y soporta **WebXR** (botón VR).

**Módulo de Incidencias VR** (`viz/static/incidencias/`): visor de incidencias en obra
con carga glTF, marcadores georreferenciados, ficha, clasificación IA e import/export.

## Uso como CLI (frontera de integración)

```bash
motor-fea --version
motor-fea --analyze modelo.json        # resultados JSON por stdout
cat modelo.json | motor-fea --analyze - # '-' = leer de stdin
motor-fea --disenar-losa params.json   # diseño de losa por FEM → JSON
```

El esquema JSON de entrada/salida está documentado en `motor_fea.api.contrato`.

## Desarrollo

```bash
python -m venv .venv && . .venv/bin/activate
pip install -e '.[api,ia,dev]'
pytest                                  # suite del motor
```

## Roadmap

- **#1** API de escritura (POST-modelo) + esfuerzos por elemento.
- **#2** Shell de interfaz nueva (web/WebXR): entrada/edición de modelo, navegación.
- **#3** Diagramas de esfuerzos (M / V / δ) en el visor.
- **#4** Vista en secciones (corte por plano del modelo + campos en el corte).
- **#5** Reposicionar la suite .NET (`archive/dotnet-suite`) como cliente de memoria.

## Licencia

[MIT](LICENSE). La suite .NET archivada interopera opcionalmente con `Losas.exe`
(Ing. Francisco E. Perdomo, método Pieper-Martens), **no cubierto por esta licencia**
y que el usuario debe obtener directamente del autor.
````

- [ ] **Step 3: Commit del README en `master`**

Run:
```bash
git add README.md
git commit -m "docs: README engine-first (motor FEA + visor web/VR + incidencias)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
Expected: 1 file changed en `master`.

- [ ] **Step 4: Verificar**

Run: `git show --stat master:README.md | head -3 && git log -1 --oneline master`
Expected: el commit del README es el tip de `master`.

---

### Task 2: Tags de archivo .NET (local, no destructivo)

**Files:** ninguno (tags git).

- [ ] **Step 1: Crear un tag inmutable por cada tip .NET**

Run:
```bash
git tag archive/dotnet/main-v0.7            origin/main
git tag archive/dotnet/avalonia-linux       origin/avalonia-linux
git tag archive/dotnet/fase1-edificio-nivel origin/feat/fase1-edificio-nivel
git tag archive/dotnet/fase2-casos-combos   origin/feat/fase2-casos-combinaciones
git tag archive/dotnet/fase3-vigas-continuas origin/feat/fase3-vigas-continuas
git tag archive/dotnet/fase4-diseno-rc      origin/feat/fase4-diseno-rc
git tag archive/dotnet/fase5-columnas       origin/feat/fase5-columnas
git tag archive/dotnet/fase6-zapatas        origin/feat/fase6-zapatas
git tag archive/dotnet/fase7-orquestacion   origin/feat/fase7-orquestacion
git tag archive/dotnet/rebrand-estructurasrd origin/feat/rebrand-estructurasrd
git tag archive/dotnet/saf-export-interop1  origin/feat/saf-export-interop1
```
Expected: sin salida (éxito). Si un tag ya existe, falla — está bien, ya existe.

- [ ] **Step 2: Verificar que cada tag apunta al SHA correcto**

Run: `for t in $(git tag -l 'archive/dotnet/*'); do echo "$(git rev-parse "$t")  $t"; done`
Expected: 11 tags, cada uno con su SHA. Cotejar contra el snapshot de Task 0.

---

### Task 3: Push de tags + verificación en remoto (aditivo) **[GATE — primer push]**

**Files:** ninguno.

- [ ] **Step 1: STOP — confirmación del usuario**

> Este es el **primer push** a `origin`. Es **aditivo** (solo crea tags, no borra
> ni mueve nada), pero pedir OK explícito antes de tocar el remoto público.
> Confirmar antes de continuar.

- [ ] **Step 2: Pushear todos los tags de archivo**

Run: `git push origin 'refs/tags/archive/dotnet/*'`
Expected: `* [new tag] archive/dotnet/...` por cada uno (11).

- [ ] **Step 3: Verificar los tags en el remoto**

Run: `git ls-remote --tags origin 'archive/dotnet/*' | wc -l`
Expected: `11`.

---

### Task 4: Rama viva `archive/dotnet-suite` (aditivo)

**Files:** ninguno.

- [ ] **Step 1: Crear y pushear la rama desde el tip .NET más nuevo**

Run:
```bash
git branch archive/dotnet-suite origin/avalonia-linux
git push origin archive/dotnet-suite
```
Expected: `* [new branch] archive/dotnet-suite -> archive/dotnet-suite`.

- [ ] **Step 2: Verificar**

Run: `git ls-remote --heads origin archive/dotnet-suite`
Expected: una línea con el SHA == `origin/avalonia-linux`.

---

### Task 5: Publicar el motor como `main` **[GATE — DESTRUCTIVO]**

**Files:** ninguno.

- [ ] **Step 1: STOP — confirmación del usuario (force-push a `main`)**

> **DESTRUCTIVO:** reemplaza `origin/main` (WPF viejo) con la línea del motor
> (`master`, historia no relacionada). El main viejo YA está en
> `archive/dotnet/main-v0.7` (verificar Task 3 Step 3 pasó). Reversible vía ese
> tag. **Requiere OK explícito.**

- [ ] **Step 2: Verificar que el respaldo del main viejo existe en remoto**

Run: `git ls-remote --tags origin archive/dotnet/main-v0.7`
Expected: una línea (el tag está en `origin`). **Si está vacío, ABORTAR.**

- [ ] **Step 3: Force-push del motor a `main`**

Run: `git push --force-with-lease origin master:main`
Expected: `+ <old>...<new> master -> main (forced update)`.

- [ ] **Step 4: Verificar**

Run: `git ls-remote --heads origin main && git log -1 --oneline master`
Expected: `origin/main` apunta al tip de `master` (el commit del README de Task 1).

---

### Task 6: Preservar la rama de incidencias VR (aditivo)

**Files:** ninguno.

- [ ] **Step 1: Pushear `engine/incidencias-vr-mvp`**

Run: `git push origin engine/incidencias-vr-mvp`
Expected: `* [new branch] engine/incidencias-vr-mvp -> engine/incidencias-vr-mvp`.

- [ ] **Step 2: Verificar**

Run: `git ls-remote --heads origin engine/incidencias-vr-mvp`
Expected: una línea con el SHA `8cc1448` (o el tip actual de la rama).

---

### Task 7: Podar ramas remotas **[GATE — DESTRUCTIVO]**

**Files:** ninguno.

- [ ] **Step 1: STOP — confirmación del usuario (borrado de ramas remotas)**

> **DESTRUCTIVO:** borra 11 ramas remotas. Todas están preservadas por tag
> (`archive/dotnet/*`, verificado en Task 3) o contenidas en
> `archive/dotnet-suite`. Reversible. **Requiere OK explícito.**

- [ ] **Step 2: Pre-chequeo — confirmar que cada rama a borrar está respaldada**

Run:
```bash
for b in feat/fase1-edificio-nivel feat/fase2-casos-combinaciones feat/fase3-vigas-continuas \
         feat/fase4-diseno-rc feat/fase5-columnas feat/fase6-zapatas feat/fase7-orquestacion \
         feat/rebrand-estructurasrd feat/saf-export-interop1 avalonia-linux ui/editor-planta; do
  sha=$(git rev-parse "origin/$b" 2>/dev/null)
  if git tag --points-at "$sha" | grep -q 'archive/dotnet/' || git merge-base --is-ancestor "$sha" archive/dotnet-suite 2>/dev/null; then
    echo "OK respaldada -> $b"; else echo "!! SIN RESPALDO -> $b (NO BORRAR)"; fi
done
```
Expected: las 11 dicen "OK respaldada". **Si alguna dice "SIN RESPALDO", ABORTAR y archivarla primero.**

- [ ] **Step 3: Borrar las ramas remotas**

Run:
```bash
git push origin --delete \
  feat/fase1-edificio-nivel feat/fase2-casos-combinaciones feat/fase3-vigas-continuas \
  feat/fase4-diseno-rc feat/fase5-columnas feat/fase6-zapatas feat/fase7-orquestacion \
  feat/rebrand-estructurasrd feat/saf-export-interop1 avalonia-linux ui/editor-planta
```
Expected: `- [deleted]` por cada una (11).

- [ ] **Step 4: Verificar el estado final del remoto**

Run: `git ls-remote --heads origin | awk '{print $2}'`
Expected exactamente: `refs/heads/main`, `refs/heads/archive/dotnet-suite`,
`refs/heads/engine/incidencias-vr-mvp`.

---

### Task 8: Retirar workflows .NET (CI/CD) **[GATE]**

**Files:** ninguno (acciones sobre GitHub Actions).

- [ ] **Step 1: Clasificar los 4 workflows registrados**

Run: `gh workflow list --all`
Expected: `ci`, `release`, `ci-linux`, `ci-motor-fea`. Inspeccionar el contenido de
cada uno para clasificar .NET vs motor:
```bash
for wf in ci release ci-linux ci-motor-fea; do
  echo "===== $wf ====="; gh workflow view "$wf" 2>/dev/null | head -25; done
```
Expected: identificar cuáles son .NET (`dotnet build/test`, `win-x64`) y cuál es del
motor (`pytest`, `pip install`). `ci-motor-fea` = motor (CONSERVAR).

- [ ] **Step 2: STOP — confirmar la lista a deshabilitar**

> Confirmar con el usuario la clasificación: deshabilitar `ci`, `release` (y
> `ci-linux` **solo si** es build .NET). **Nunca** `ci-motor-fea`. Reversible con
> `gh workflow enable`.

- [ ] **Step 3: Deshabilitar los workflows .NET**

Run (ajustar la lista según Step 1/2):
```bash
gh workflow disable ci
gh workflow disable release
# gh workflow disable ci-linux   # SOLO si se clasificó como .NET
```
Expected: sin error por cada uno.

- [ ] **Step 4: Verificar**

Run: `gh workflow list --all`
Expected: `ci-motor-fea` = `active`; los .NET = `disabled_manually`.

---

### Task 9: Limpieza de ramas locales

**Files:** ninguno.

- [ ] **Step 1: Borrar ramas locales ya contenidas en `master`**

Run:
```bash
git branch -d engine/f0-verdad-de-estado engine/f1-verdad-visual \
              engine/f2-cad-deterministico engine/f3-pieper-martens-21
```
Expected: `Deleted branch ...` (las 4; `-d` solo borra si están mergeadas — seguro).

- [ ] **Step 2: Revisar `ui/verificacion-visual-compat` antes de borrar**

Run: `git merge-base --is-ancestor ui/verificacion-visual-compat archive/dotnet-suite && echo "contenida -> borrable" || echo "tiene trabajo único -> tag antes de borrar"`
Expected: si "borrable" → `git branch -D ui/verificacion-visual-compat`. Si "trabajo único"
→ `git tag archive/dotnet/verificacion-visual-compat ui/verificacion-visual-compat && git push origin refs/tags/archive/dotnet/verificacion-visual-compat` y luego borrar.

- [ ] **Step 3: Borrar la copia local de `avalonia-linux`** (preservada en remoto/tag)

Run: `git branch -D avalonia-linux`
Expected: `Deleted branch avalonia-linux`.

- [ ] **Step 4: Nota sobre el worktree .NET**

`ui/editor-planta` sigue checked-out en `/home/gdc/Downloads/EstructurasRD-main`.
NO borrarla mientras el worktree esté activo (contenida en `archive/dotnet-suite`,
ya preservada). Dejar el worktree como está o retirarlo con
`git worktree remove` solo si el usuario lo confirma.

---

### Task 10: Verificación final contra criterios de éxito

**Files:** ninguno.

- [ ] **Step 1: Estado del remoto = 3 ramas + tags**

Run: `git ls-remote --heads origin | awk '{print $2}' && echo "--- tags ---" && git ls-remote --tags origin 'archive/dotnet/*' | wc -l`
Expected: `main`, `archive/dotnet-suite`, `engine/incidencias-vr-mvp`; tags ≥ 11.

- [ ] **Step 2: Recuperabilidad de lo .NET**

Run: `git rev-list --count archive/dotnet/saf-export-interop1 && git show archive/dotnet/fase6-zapatas --stat | head -3`
Expected: cuentas/commits presentes — el trabajo único es recuperable por tag.

- [ ] **Step 3: README en `main`**

Run: `git show origin/main:README.md | head -5`
Expected: encabezado "EstructurasRD · Motor FEA".

- [ ] **Step 4: Actions limpio**

Run: `gh workflow list --all`
Expected: solo `ci-motor-fea` activo; .NET deshabilitados.

- [ ] **Step 5: Cerrar**

Actualizar el snapshot `/home/gdc/Downloads/estructurasrd-snapshot-pre0.txt` con un
"post-#0" o archivarlo. Reportar al usuario el estado final.
