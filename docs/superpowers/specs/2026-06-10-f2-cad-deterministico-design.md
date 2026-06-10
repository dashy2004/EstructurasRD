# F2 — Pipeline CAD/DXF determinístico · Design Spec

**Fecha:** 2026-06-10 · **Fase:** F2 (roadmap `roadmap-fases-F0-F9.md`) · **Tamaño:** L
**Redactado por el loop autónomo (iteración 4), grounding verificado contra el código en la rama `engine/f2-cad-deterministico`.**

## 1. Problema (evidencia archivo:línea)

Una losa importada por el **pipeline batch** (`MainViewModel.GenerarDesdeDxfAsync`, `src/ViewModels/MainViewModel.cs:1572`) NO queda en la posición/orientación del plano:

1. **Y espejada:** el batch asigna `CoordenadaY = l.YMetros` (Y cruda del DXF, ascendente) — `MainViewModel.cs:~1610`. El lienzo de planta usa **Y descendente** (`PlantaCanvas`), y el path interactivo SÍ invierte: `posY = (Plano?.MaxY ?? rect.MaxY) - rect.MaxY` (`src/ViewModels/CadEditorViewModel.cs:866`).
2. **Sin anclaje:** el batch no asigna `PosX`/`PosY` (quedan `null` ⇒ losa "flotante": `LayoutSolver` la **reubica** por adyacencias — semántica documentada en `src.Core/Models/Sistema.MemoriaPlus.cs:563-585`). El interactivo ancla: `PosX = posX, PosY = posY` (`CadEditorViewModel.cs:723-724` y `:873-875`).
3. **Pérdida silenciosa:** un rectángulo en capa Viga/Eje se descarta sin viga, sin aviso, sin contador (`src.Core/Services/DxfEstructuraMapper.cs:78`, comentario explícito).
4. **Ambientes en L descartados enteros:** `PoligonoLosaMapper.TryMapearRectangulo` exige 4 vértices ortogonales; cualquier contorno cerrado no rectangular solo incrementa `contornosNoRect` (`DxfEstructuraMapper.cs:89-91`) y se pierde toda la geometría.
5. **Bounding box de arcos inflado:** usa el círculo circunscrito completo (`src.Core/Services/DxfImportService.cs:268-272`, comentario `:269`) — un arco de 10° infla el encuadre del plano.
6. **Círculo en capa ambigua ⇒ 0 columnas** y el path de visión (`QwenAnalizador.cs:94-120`) devuelve `Array.Empty` de columnas.

## 2. Decisiones de diseño

### 2.1 Paridad batch↔interactivo (núcleo)
Nuevo helper **puro y testeable** en `src.Core`:
`DxfEstructuraMapper.CrearLosaBatch(LosaPropuesta l, double maxYPlano, int id)` →

```
PosX        = l.XMetros
PosY        = maxYPlano − (l.YMetros + l.LyM)     // top-left en Y-descendente; idéntico a CadEditorViewModel.cs:866 (rect.MaxY = MinY+Alto)
CoordenadaX = l.XMetros
CoordenadaY = PosY                                 // misma convención Y-down de PlantaCanvas
Lx/Ly       = l.LxM/l.LyM (fallback 4.0 como hoy) · Espesor 0.12 · Carga 2.0 · Tipo l.Tipo
```

`GenerarDesdeDxfAsync` pasa `plano.MaxY` y usa el helper (deja de construir la `Losa` inline). El path interactivo NO se toca.

### 2.2 Rectángulo en capa Viga: contar + avisar (no heurística)
Un rectángulo en capa Viga podría ser una viga dibujada con ancho O un anillo de 4 vigas — interpretar en silencio sería otra mentira. Decisión: contador `rectVigaDescartados` + texto en `PropuestaElementos.Advertencias`. La heurística geométrica queda para F2b con fixtures reales.

### 2.3 Ambientes en L: descomposición rectilínea
Nuevo `PoligonoLosaMapper.TryDescomponerRectilineo(PolilineaCad, out List<RectanguloCad>, tol)`:
- Acepta polígonos cerrados **rectilíneos** (todos los lados paralelos a ejes, ±tol).
- Algoritmo celda-barrido: Xs/Ys distintos ordenados → rejilla de celdas → celda dentro del polígono (test punto-en-polígono del centro) → fusión greedy de celdas contiguas por filas (rectángulos maximales por banda).
- El mapper, cuando `TryMapearRectangulo` falla y la capa es Losa/Otro, intenta descomponer: una `LosaPropuesta` por rectángulo + advertencia informativa («ambiente en L subdividido en N paños»). Si tampoco es rectilíneo → `contornosNoRect` como hoy.
- Un rectángulo simple sigue yendo por `TryMapearRectangulo` (sin cambio de comportamiento).

### 2.4 BBox real de arcos parciales
En `DxfImportService`: para `ArcoCad` no-círculo-completo, acumular inicio, fin y los puntos cardinales (0°/90°/180°/270°) **contenidos en el sweep**. Círculo completo: igual que hoy.

### 2.5 DIFERIDO (F2 queda "parcial" hasta la próxima iteración)
- Heurística forma→columna en capa ambigua (`DxfEstructuraMapper.cs:65`).
- Columnas en el path de visión (`QwenAnalizador.cs:94-120`).

## 3. Testing (TDD estricto)
- `DxfBatchParityTests` (nuevo): `CrearLosaBatch` reproduce la fórmula interactiva (misma `PosY` que `maxY − rect.MaxY`); `PosX/PosY` no nulos (anclada, `TienePosicionExplicita`); `CoordenadaY` invertida.
- `DxfEstructuraMapperTests` (ampliar): rectángulo en capa Viga → `Advertencias` lo menciona y no produce losa/columna; L de 6 vértices en capa Losa → ≥2 `LosaPropuesta` cuya área suma la del L.
- `PoligonoLosaMapperTests` (ampliar): L → 2 rects (área exacta); rectángulo simple → 1; polígono no rectilíneo (triángulo) → false.
- `DxfImportServiceTests` (ampliar/crear): arco de 90° (0°→90°) → bbox = (cx, cy)–(cx+r, cy+r), no el círculo completo.

## 4. Criterios de aceptación (roadmap)
1. Batch y `MapearPoligono` interactivo dan la **misma Y** y los **mismos `PosX/PosY`** para el mismo rectángulo DXF (test de paridad).
2. Un contorno en L produce **≥2 losas** (test del mapper).
3. Nada del catálogo se pierde en silencio: todo descarte aparece en `Advertencias`.
4. Suites completas verdes (≥1171 .NET / 208 Py), `estado-real.sh --check` exit 0.

## 5. Restricciones
- NO tocar `Losas.exe` ni su import; NO tocar el path interactivo (`CadEditorViewModel`); NO tocar `QwenAnalizador` (2.5 diferido); los records `*Propuesta` solo ganan datos vía `Advertencias` (sin romper firmas).
