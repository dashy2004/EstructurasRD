# Diseño — #0 Reconciliación GitHub + README (engine-first)

**Fecha:** 2026-06-13
**Sub-proyecto:** #0 del programa "EstructurasRD bajo dirección A (motor = núcleo)"
**Estado:** Diseño aprobado (pendiente revisión del spec por el usuario)

---

## 1. Contexto y motivación

El repo público `dashy2004/EstructurasRD` (default `main`) está en un estado
incoherente respecto a la dirección elegida:

- **`origin/main` está obsoleto:** del 2026-05-22, solo contiene `feat/fase1-3`
  mergeadas. No refleja ni siquiera el producto .NET vivo.
- **La suite .NET viva (`avalonia-linux`, 2026-06-07) NO está en `main`.**
- **El motor Python (la línea elegida como futuro) no está publicado en
  ningún lado** — vive en `master` local, **sin ancestro común** con
  `origin/main` (verificado: `git merge-base master origin/main` → vacío).
- **12 ramas**, solo 3 limpiamente mergeadas.

**Dirección adoptada (decisión previa "A"):** el motor Python es el núcleo de
verdad; el visor web/WebXR es la interfaz nueva; la suite .NET se conserva
(no se jubila) para la generación de memoria `.docx`, reposicionada como
cliente del motor en un sub-proyecto futuro (#5).

**Objetivo de #0:** dejar el GitHub coherente con esa dirección —
`main` = motor, suite .NET preservada sin pérdida, ramas podadas, README
reescrito — de forma **reversible y con gates de confirmación** para
operaciones destructivas sobre un repo público.

## 2. Decisiones de diseño (cerradas)

| # | Decisión | Valor elegido |
|---|----------|---------------|
| D1 | Modelo de publicación | Motor = nuevo `main`; .NET archivado en rama/tag (no monorepo aún; no repos separados) |
| D2 | Fuente del nuevo `main` | `master` **limpio** (la línea de integración del motor) — **sin** incluir aún `engine/incidencias-vr-mvp` |
| D3 | Destino del trabajo de incidencias VR | Se preserva como rama remota `engine/incidencias-vr-mvp`; se mergea a `main` más tarde, tras su gate Quest + adaptador de interop |
| D4 | Preservación .NET | Tag inmutable de cada tip remoto **antes** de borrar nada + rama viva `archive/dotnet-suite` ← `avalonia-linux` |
| D5 | Fusión física monorepo | Diferida (YAGNI) hasta #5, cuando Avalonia se vuelva cliente del motor |

## 3. Hechos de ramas verificados (base de la política de poda)

```
Mergeadas a origin/main (seguras):   feat/fase1, feat/fase2, feat/fase3
NO mergeadas (trabajo único):        avalonia-linux, feat/fase4, feat/fase5,
                                     feat/fase6, feat/fase7,
                                     feat/rebrand-estructurasrd,
                                     feat/saf-export-interop1, ui/editor-planta
Contención en avalonia-linux:        ui/editor-planta -> CONTENIDA (borrable)
                                     feat/fase4-7, rebrand, saf -> NO contenidas
Línea motor:                         master detrás de engine/incidencias-vr-mvp
engine/f0..f3 (local):               ya contenidas en master (borrables localmente)
```

**Implicación clave:** como `feat/fase4-7`/`rebrand`/`saf` tienen commits
únicos no integrados, **se archivan TODAS por tag** antes de cualquier borrado
(no basta con preservar `avalonia-linux`).

## 4. Estrategia de preservación (.NET) — "no perder nada"

Un tag es un puntero permanente a un commit: **borrar la rama no borra los
commits si un tag los referencia**. Esta es la red de seguridad que hace
reversible toda la operación, incluso en un repo público.

**Tags de archivo a crear y pushear ANTES de tocar nada** (prefijo `archive/dotnet/`):

- `archive/dotnet/main-v0.7` ← `origin/main` (el main viejo WPF)
- `archive/dotnet/avalonia-linux` ← `origin/avalonia-linux`
- `archive/dotnet/fase4-diseno-rc` ← `origin/feat/fase4-diseno-rc`
- `archive/dotnet/fase5-columnas` ← `origin/feat/fase5-columnas`
- `archive/dotnet/fase6-zapatas` ← `origin/feat/fase6-zapatas`
- `archive/dotnet/fase7-orquestacion` ← `origin/feat/fase7-orquestacion`
- `archive/dotnet/rebrand-estructurasrd` ← `origin/feat/rebrand-estructurasrd`
- `archive/dotnet/saf-export-interop1` ← `origin/feat/saf-export-interop1`
- (opcional, barato) `archive/dotnet/fase1..3` ← `origin/feat/fase1..3`

**Rama viva preservada:** `archive/dotnet-suite` ← `avalonia-linux` (la línea
.NET integrada más nueva; será la base de #5, Avalonia como cliente de memoria).

## 5. Publicación del motor como `main`

- **Fuente:** `master` local (incluye `engine/f0..f3`; **no** incluye el visor
  VR de incidencias por D2).
- **Mecánica:** historia no relacionada ⇒ **reemplazo por force-push**, no merge.
  El main viejo ya quedó archivado por tag (D4), así que el force-push es seguro
  y reversible.
  - `git push --force origin master:main`
- **Preservar incidencias VR (D3):** `git push origin engine/incidencias-vr-mvp`
  (queda como rama remota para mergear luego de su gate).
- **Default branch:** sigue `main` (ya lo es; no cambia).

## 6. Política de poda

**Borrar en remoto** (solo tras verificar que los tags de archivo existen en `origin`):

- `feat/fase1`, `feat/fase2`, `feat/fase3` (mergeadas a main viejo, ya archivado)
- `feat/fase4-diseno-rc`, `feat/fase5-columnas`, `feat/fase6-zapatas`,
  `feat/fase7-orquestacion` (archivadas por tag)
- `feat/rebrand-estructurasrd`, `feat/saf-export-interop1` (archivadas por tag)
- `ui/editor-planta` (contenida en avalonia-linux)
- `avalonia-linux` → su contenido pasa a vivir como `archive/dotnet-suite`;
  la rama `avalonia-linux` se borra del remoto tras crear `archive/dotnet-suite`.

**Limpiar en local:**

- `engine/f0-verdad-de-estado`, `engine/f1-verdad-visual`,
  `engine/f2-cad-deterministico`, `engine/f3-pieper-martens-21` (ya en master)
- `ui/verificacion-visual-compat` (revisar antes; merge a avalonia-linux probable)
- copias locales de `avalonia-linux`
- **Caveat worktree:** `ui/editor-planta` está checked-out en el worktree
  `/home/gdc/Downloads/EstructurasRD-main`; no se puede borrar mientras esté
  activa. El plan debe manejar/posponer ese worktree.

**Estado final del remoto (de 12 ramas → 3 + tags):**

```
main                        (motor, = ex-master limpio)
engine/incidencias-vr-mvp   (preservada, para merge futuro tras gate)
archive/dotnet-suite        (línea .NET viva = ex avalonia-linux)
+ tags archive/dotnet/*     (red de seguridad de todo lo .NET)
```

## 7. README nuevo (engine-first)

Reescribir `README.md` del motor para reflejar la realidad honesta de la
dirección A. Estructura propuesta:

1. **Encabezado / identidad:** EstructurasRD = motor FEA en Python + visor
   web/WebXR + Incidencias VR. Suite .NET (memoria `.docx`) como cliente.
2. **Qué es** — el problema que resuelve y para quién (ingeniería civil RD,
   R-001 / ACI 318).
3. **Arquitectura** — `core/` (FEA puro) → `viz/` (DTOs JSON neutrales) →
   `static/` (visor three.js / WebXR). API FastAPI como frontera.
4. **Capacidades del motor** — marcos 3D, modal, diafragma, losa FEM,
   normativa (r001/aci318/combinaciones), diseño/armado, IA local.
5. **Visor web + VR** — geometría, deformada, modos, heatmaps, VRButton;
   roadmap de secciones/diagramas.
6. **Incidencias VR** — el visor de incidencias (rama preservada).
7. **Estado de la suite .NET** — preservada en `archive/dotnet-suite`;
   rol futuro como cliente de memoria (#5).
8. **Roadmap** — #1 API de escritura + esfuerzos · #2 shell de interfaz ·
   #3 diagramas · #4 vista en secciones · #5 Avalonia cliente.
9. **Correr local** — `pip install -e .[ia]`, levantar FastAPI, abrir visor.
10. **Licencia** — mantener atribución a `Losas.exe` (Ing. F. Perdomo).

El README viejo (suite .NET) se preserva en `archive/dotnet-suite`.

## 8. Seguridad y gates

- **Toda operación destructiva** (force-push a `main`, borrado de ramas
  remotas) se ejecuta **por lote, con confirmación explícita del usuario**, y
  **solo después** de crear y verificar los tags de archivo en `origin`.
- **Orden invariante:** (1) crear tags de archivo → (2) push de tags →
  (3) verificar tags en remoto → (4) crear `archive/dotnet-suite` y pushear →
  (5) force-push motor a `main` → (6) push `engine/incidencias-vr-mvp` →
  (7) borrar ramas remotas → (8) limpieza local.
- **Rollback:** cualquier paso es reversible reconstruyendo ramas desde los
  tags `archive/dotnet/*`.
- **No tocar `main` a ciegas:** el reemplazo de `main` es deliberado y
  documentado aquí; no se hace ningún otro push a `main` fuera de este flujo.

## 9. Fuera de alcance (de #0)

- Cambios en el código del motor o del visor (eso es #1–#4).
- Fusión física monorepo del .NET (eso es #5).
- Merge del visor VR de incidencias a `main` (D3: diferido a su gate).
- CI/CD nuevo del motor (se puede tratar como sub-tarea aparte si el usuario
  lo pide).

## 10. Criterios de éxito

1. `origin` queda con `main` (motor) + `engine/incidencias-vr-mvp` +
   `archive/dotnet-suite` + tags `archive/dotnet/*`; sin ramas huérfanas.
2. Todo el trabajo .NET (incluyendo commits únicos de `fase4-7`/`rebrand`/`saf`)
   es recuperable por tag — verificable con `git rev-list` desde cada tag.
3. `README.md` describe el motor y la dirección A; el README .NET vive en
   `archive/dotnet-suite`.
4. Cero pérdida de historia; cada acción destructiva fue confirmada y es
   reversible.
