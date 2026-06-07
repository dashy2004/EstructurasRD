# Fase B — Coherencia Nivel/Sistema (cerrar la pérdida de datos de raíz) — Plan

> **Ejecución:** subagent-driven SECUENCIAL. Compuerta tras CADA tarea: `dotnet test` `Failed: 0`, total ≥ baseline. Base = rama Fase A (incluye A1/IsDirty).

**Spec fuente:** `docs/ANALISIS_UI_v2.md` §3.3, §3.4, §5.3, §6 (Fase B). **Depende de:** A1 (IsDirty — ya en esta rama).

**Goal:** Erradicar la fachada `Proyecto.Sistemas = Niveles[0]` como destino de mutaciones/guardado; que un proyecto multinivel mute, exporte y guarde **todos** los niveles. Cierra los críticos de pérdida de datos *de raíz* (junto con A = hito de seguridad).

**Toolchain:** `export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"`; `cd /home/gdc/Downloads/EstructurasRD-engine`; build `dotnet build LosasPlus.Linux.sln --no-incremental`; test `dotnet test tests/LosasPlus.Tests/LosasPlus.Tests.csproj -v minimal --nologo` (baseline 1044, flaky preexistente `PlantillaRegistryTests` puede parpadear — re-correr).

**Archivos núcleo:** `src.Core/Models/Edificio.cs`, `Sistema.cs` (fachada ~L98-106), `Sistema.MemoriaPlus.cs` (Uso/Cota), `src/ViewModels/MainViewModel.cs` (AgregarSistema/EliminarSistema ~L996-1027, setter alias `Sistema` ~L705-719, export/validación/búsqueda ~L872/918/1037/1714/1752), `src.Core/Services/ProyectoService.cs` (guarda solo `p.Sistemas`), el serializer (`ProyectoSerializer*` — buscalo).

---

## Task B1 — Mutaciones al nivel activo + matar el alias destructivo (caracterización primero)

**Test primero (debe FALLAR en el código actual):** en `tests/LosasPlus.Tests/`, crear un proyecto con **2 niveles**; fijar `NivelActivo` al **segundo**; `AgregarSistema` → assertar que el sistema nuevo está en `Niveles[1].Sistemas` y NO en `Niveles[0]`. Igual para `EliminarSistema` (borra del nivel activo). Hoy fallan (van a la fachada `Niveles[0]`).

**Fix:**
1. `AgregarSistema`/`EliminarSistema` (MainViewModel) operan sobre `NivelActivo.Sistemas` (no `_proyecto.Sistemas`).
2. Eliminar/neutralizar el setter alias `Sistema` que hace `Clear()+Add()` (colapsa la colección del nivel). Si algo lo usa, redirigir a `SistemaActivo`.
3. Confirmar que la UI (que lee `NivelActivo.Sistemas`) sigue coherente.

**Gate:** tests nuevos verdes + 1044 sin regresión. **Commit:** `fix(dominio): mutaciones de sistema al nivel activo + eliminar alias destructivo (B1)`.

---

## Task B2 — Export/guardado del árbol completo (EnumerarSistemas)

**Test primero (falla):** proyecto 2 niveles con sistemas en ambos; `Guardar` + recargar → asertar que **ambos** niveles y sus sistemas persisten (hoy se pierde todo salvo `Niveles[0]`). Idem export `.DL`/validación/búsqueda recorren todos.

**Fix:**
1. Helper `EnumerarSistemas(edificio) = edificio.Niveles.SelectMany(n => n.Sistemas)` (o por proyecto).
2. `ProyectoService.GuardarProyecto`/serializer: serializar el árbol completo `Edificios→Niveles→Sistemas` (+ vigas/columnas/muros/ejes/cargas/combinaciones/metadata), no `p.Sistemas`.
3. Export `.DL`/validación/búsqueda (MainViewModel ~L872/918/1037/1714/1752) usan `EnumerarSistemas`. Decisión: `.DL` por-edificio (todos los niveles).
4. Resolver los **dos formatos homónimos** `proyecto.lpx.json`: un solo serializer del árbol con magic-header que distinga envelope de manifest (o unificar a uno).

**Gate + Commit:** `fix(persistencia): guardar/exportar el arbol completo multinivel (B2)`.

---

## Task B3 — Uso/Cota a Nivel + migración + CantidadNiveles

**Fix:** mover `Uso`/`CotaMetros`/`Elevacion` de `Sistema` (Sistema.MemoriaPlus.cs) a `Nivel`; `Proyecto.Sistemas` → `[Obsolete]` solo-lectura legacy; migración de versión de esquema (v3→v4) al cargar; `CantidadNiveles` (serializer metadata) = nº de **niveles** (no de sistemas). Tests de migración + conteo. **Commit:** `refactor(dominio): Uso/Cota en Nivel + migracion v3->v4 + CantidadNiveles (B3)`.

---

## Task B4 — Carga completa + undo total

**Fix:** abrir `.lpx.json` copia `Cargas`/`Combinaciones`/metadata MemoriaPlus al proyecto vivo; `RestoreSnapshot` restaura Cargas + placeholders. Tests round-trip. **Commit:** `fix(persistencia): carga completa (cargas/combinaciones/metadata) + undo total (B4)`.

---

## Cierre Fase B
- `dotnet test` `Failed: 0` (≥ 1044 + nuevos); build `--no-incremental` 0/0.
- Revisión adversarial: un proyecto multinivel muta/exporta/guarda/recarga sin perder niveles. Hito de seguridad (A+B) cumplido: los 4 críticos cerrados.
- PR contra la rama Fase A (stack) o `avalonia-linux`.

## Aceptación
1. Mutaciones de sistema caen en el nivel activo (no `Niveles[0]`).
2. Guardar/exportar/recargar preserva **todos** los niveles y su contenido.
3. Sin regresión (1044+ verde).
