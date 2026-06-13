# PLAN PENDIENTE — EstructurasRD · 2026-06-10

> Tablero vivo de fases pendientes. Conteos/estado real: ver `STATE.md` (fuente única de verdad).
> Diagnóstico detallado con evidencia `archivo:línea`: `docs/superpowers/AUDITORIA_UI_2026-06-10.md`.

---

## ▶️ Cómo abrir la aplicación SIEMPRE con el build nuevo

```bash
cd /home/gdc/Downloads/EstructurasRD-engine
dotnet build LosasPlus.Linux.sln --no-incremental -c Release && dotnet run --project src -c Release --no-build
```

- `--no-incremental` es **obligatorio**: el build incremental oculta errores AVLN2000 del compilador XAML de Avalonia y puede dejarte corriendo binarios viejos.
- `--no-build` en el `run` garantiza que se ejecuta EXACTAMENTE lo que se acaba de compilar.
- Si `dotnet` no está en el PATH (shell nueva): `export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"`.
- MemoriaPlus (generador de memorias): `dotnet run --project src.Memoria -c Release --no-build`.
- Suite completa antes/después de tocar código: `dotnet test LosasPlus.Linux.sln` y `( cd motor-fea && .venv/bin/python -m pytest -q )`.

---

## FASES PENDIENTES (orden por apalancamiento)

### UI1 — Un solo lienzo (eliminar Plano CAD) · L
> Causa raíz de la desincronización: dualidad `PosX/PosY` (CAD) vs `CoordenadaX/Y` (planta) — dos verdades para la misma losa.

- [x] **UI1.1** Unificar el sistema de coordenadas de Losa (una sola fuente; adaptador para proyectos guardados) y auditar el caché de `LayoutSolver`. ✅ 2026-06-10 — `CoordenadaX/Y + Anclada` (PosX/PosY retirados), migración v4→v5 encadenada desde v1, caché CAD por `TopologiaPlanta.Hash`; spec: `docs/superpowers/specs/2026-06-10-ui1.1-coordenadas-unificadas-design.md`.
- [x] **UI1.2** Calibración interactiva de PDF → Planta 2D (`CadEditorViewModel.cs:585-664`). ✅ 2026-06-10 — gesto de 2 puntos en `PlantaCanvas` + panel flotante; homotecia extraída a `CalibradorPdf` (src.Core, testeada) y aplicada vía el comando compartido del `CadEditorViewModel` (misma `PdfReferencia` ⇒ ambos lienzos calibrados).
- [x] **UI1.3** Click sobre polilínea del DXF → losa (MapearPoligono, `CadEditorViewModel.cs:847-887`) en PlantaCanvas. ✅ 2026-06-10 — herramienta «▱ Calcar losa» (hit-test con `PoligonoLosaMapper.ContienePunto` en coords mundo + comando compartido). Bonus: corregido el **espejo vertical** del underlay DXF en planta (dibujaba sin flip-Y; ahora `PlanoAPlanta` usa la convención `MaxY − y` del mapper/CAD).
- [x] **UI1.4** Leyenda "suma de colores" de muros como overlay (`CadEditorViewModel.cs:529`). ✅ 2026-06-10 — leyenda flotante en Planta 2D (mismo markup, bindea `CadEditor.ResumenMuros`) + muros de PlantaCanvas coloreados por espesor con `PaletaMuros` + `RefrescarResumenMuros()` para mutaciones desde planta.
- [x] **UI1.5** Resize de LOSAS con handles en PlantaCanvas (reciclar el patrón de `CadCanvasHost.cs:956-970`). ✅ 2026-06-10 — 8 asas (`AsaEnPunto` + `GeometriaEdicion.Redimensionar`), ancla la losa y snapshotea Undo por gesto (`GestoEdicionIniciado` → `PushUndoSnapshot`; el drag también — avance del hotspot #8).
- [x] **UI1.6** Retirar `CadView`/`CadCanvasHost` del shell (modo PlanoCad). ✅ 2026-06-11 — pase visual humano ✓; modo PlanoCad + vista CAD borrados (~2900 líneas); `CadEditorViewModel` queda como sub-VM de servicios (podado de los miembros host-only); portados a Planta 2D: panel PLANO/PDF (ajuste espacial, opacidad, modo oscuro, muros), defaults de muros nuevos y captura del lienzo para el export .xlsx; `PaletaMuros` → `Views/`; suite 1216 → 1207 (−CadTransformTests −InlineData PlanoCad). Spec: `docs/superpowers/specs/2026-06-11-ui1.6-retirar-cadview-design.md`.
- [x] **UI1.7** (alcance decidido 2026-06-12: bugs+limpieza; el render de bordes se va a UI1.8) (1) Clamp `Escala > 0` en los proxies `EscalaPlano/EscalaPdf` del `CadEditorViewModel` — el TextBox puede commitear un 0 transitorio y `PlantaAPlano` divide por `Escala`. (2) Encuadre del `CaptureCanvasPng` de planta para el export Excel (hoy captura el viewport actual; edificios > ~27 m salen recortados con el zoom default — el CAD encuadraba antes de capturar). (3) Encuadre automático al importar PDF en Planta (se perdió con el host: `SolicitudEncuadrePdf`); reutiliza la tubería de encuadre de (2). (4) `SnappingEngine` (src.Core) quedó sin consumidor de producción — Planta usa `PlantaSnapEngine`; borrar o converger (decidido: borrar). (5) Prioridad de muros en el hit-test de `PlantaCanvas` — hoy Columnas→Vigas→Losas→Muros (`:696-748`) y una losa captura el click/gesto encima de un muro (el render ya dibuja muros sobre losas). Ampliado 2026-06-12: + botón «⛶ Encuadrar» y auto-encuadre también al importar DXF. Spec: `docs/superpowers/specs/2026-06-12-ui1.7-encuadre-clamp-poda-design.md`. ✅ 2026-06-12.
- [x] **UI1.8** Render de bordes de continuidad + edición interactiva en `PlantaCanvas`. ✅ 2026-06-13 — servicio puro `BordesPlantaService` (EjeInferido/SegmentoCompartido/HachuraAristas/HitTestBorde, 14 tests) + render de hachura por arista (`BorderKind`, solo lectura) y conectores (sólido=Balanceo S / discontinuo=N); crear por selección libre de 2 losas (herramienta 🔗 Conectar bordes); menú contextual (click derecho) Balanceo S↔N · Eliminar, coexiste con pan; eje X/Y inferido por geometría (sustituye el hack always-BordesX). Suite **1223 .NET + 208 Py** verde; build `--no-incremental` 0/0; pase visual humano ✓. Alcance decidido en brainstorming: chips «+» → selección libre (no auto-colocados); grid del Editor se mantiene; hachura solo lectura. Spec `docs/superpowers/specs/2026-06-13-ui1.8-bordes-continuidad-design.md`, plan `docs/superpowers/plans/2026-06-13-ui1.8-bordes-continuidad.md`.
- [ ] **UI1.9** Etiquetas cortadas en Planta 2D en resoluciones bajas — abreviaturas o scroll horizontal (triage 2026-06-12; decisión de diseño pendiente).
- [ ] **UI1.10** Redimensionar MUROS por sus extremos (longitud) en `PlantaCanvas` — hermano de UI1.5 (resize de losas con asas); hoy los muros se seleccionan/mueven pero no tienen asas de longitud. Detectado en el gate visual de UI1.7 (2026-06-13): el usuario confirmó el hit-test muro-sobre-losa (punto 5) y pidió que el muro sea modificable en longitud. Reciclar el patrón de asas de UI1.5 (`AsaEnPunto` + snapshot Undo por gesto), adaptado a los 2 extremos de un segmento de muro.

### UI2 — Modelo unificado Nivel⊕Sistema · L
> "Niveles separados del sistema" — en la práctica siempre se usa `Sistemas[0]` (hardcodeado en 6 sitios de `src/`).

- [ ] **UI2.1** Fachada `Nivel.Losas`/`Nivel.Bordes` que delega a `Sistemas[0]` (creándolo si falta).
- [ ] **UI2.2** Migrar los 6 usos de `Sistemas[0]` a la fachada; serialización JSON intacta (compatibilidad con proyectos guardados).
- [ ] **UI2.3** Semántica columna↔cota en el 3D: definir qué pasa con una columna de 6 m en un nivel de 3 m (¿atraviesa al nivel 2?) y corregir `EscenaEdificio*` ("las columnas elevan los niveles/losas").
- [ ] **UI2.4** Carga viva/muerta POR LOSA: overrides anulables `CargaMuerta?`/`CargaViva?` sobre el global de `CargasGlobales` + campo en la UI de propiedades + lectura en `CalculoEngine` (combinaciones 1.2D+1.6L).
- [ ] **UI2.5** Tipo de uso por losa (triage 2026-06-12, extiende UI2.4): selector de reglamento → tipo de ocupación → carga viva automática; carga muerta automática de muros apoyados sobre losas; «reset de normas».

### UI3 — Diagramas vivos (export + hover + escala) · M
- [ ] **UI3.1** Botón "Exportar a Excel" en el editor de VIGAS: hojas Esfuerzos V-M, Deflexión δ, Reacciones (datos ya existen: `PuntoDiagrama{X,Cortante,Momento,Deflexion}`, `EnvolventeViga`; reciclar `AcerosLosaExporter.cs:140-210`).
- [ ] **UI3.2** Botón "Exportar a Excel" bajo el P-M de COLUMNAS: hojas Diagrama P-M (c, Pn, Mn, φ, φPn, φMn) + Resumen de diseño (`DisenoColumna.Diagrama`, ya hay `ToCsv`).
- [ ] **UI3.3** Verificación runtime de cortante/deflexión "en blanco": correr la app con el build nuevo (comando de arriba); si persiste → instrumentar `Converters.cs:54-62` (`BytesToBitmap`).
- [ ] **UI3.4** Sección transversal a escala: reducir los márgenes de ejes (`VigaEditorViewModel.cs:796-797`, −40%…+140%) MOVIENDO también las cotas/resumen (TextAnnotations) para que no se recorten.
- [ ] **UI3.5** Hover con valores: overlay sobre el `<Image>` que interpola `ResultadoViga` por X (tooltip V/M/δ bajo el cursor). Después del export.

### UI4 — Identidad, versión y configuración · M
- [ ] **UI4.1** UNA sola fuente de versión: hoy la UI muestra `v0.5.0` hardcodeado (`MainViewModel.cs:191`), el csproj dice `0.1.0` y los releases reales van por v1.4+. Leer del assembly y estampar el csproj.
- [ ] **UI4.2** Branding: retirar "motor: F. Perdomo" de la barra de estado (`MainWindow.axaml:176`) y tooltip (`:271`); la atribución completa queda en "Acerca de" (`:992-1034`). Centralizar en `Branding.Producto`.
- [ ] **UI4.3** Página de Configuración real: tema, unidades, rutas, snap, factores de combinación visibles, parámetros IA.
- [ ] **UI4.4** Cargar `qwen.config.json` en runtime (hoy defaults hardcodeados en `MainViewModel`) — absorbe la vieja F6.

### UI5 — Shell y navegación global · XL (triage 2026-06-12 — brainstorming propio)
> Reorganización propuesta por el usuario: navegación superior global (Logo/Home · Proyecto · **Calcular** persistente · Configuración · Plugins); dentro de proyecto: Explorador (árbol del modelo), Búsqueda, editores por elemento (Losas, Vigas, Columnas, Muros — estos dos últimos no existen como editores hoy) y Validación; visualización flotante o panel lateral (Planta 2D, Visor 3D, **Visor PDF consolidado** — hoy hay vistas PDF duplicadas). Pestaña Calcular: motor FEA por elemento (losas/vigas/columnas), generación automática (geometría desde columnas; sistema desde foto IA/QIF con **panel de razonamiento**; sistema estructural XYZ), cargas y combinaciones, y exportación (DL, TXT, Aceros, PDF-memoria). Nota: `Explorador`, `Busqueda`, `Acerca` y `Plugins` ya existen como modos del sidebar — esto re-agrupa, no crea de cero.

### UI6 — Design system · L (deuda «Antigravity/UI», triage 2026-06-12)
- [ ] Redesign del kit de componentes; brushes obsoletos de Main; tipografía y versiones desactualizadas (solapa con UI4.1); página Acerca: verificar contenido y licencias (solapa con UI4.2).

### F2b — CAD restante · M
- [ ] **F2b.1** Heurística forma→columna en capa ambigua (`DxfEstructuraMapper.cs:65`; hoy círculo en capa ambigua ⇒ 0 columnas).
- [ ] **F2b.2** Columnas en el path de visión (`QwenAnalizador.cs:94-120` devuelve `Array.Empty`). Requiere Ollama + fixtures.
- [ ] **F2b.3** Heurística rectángulo-en-capa-Viga → viga con ancho (hoy solo se avisa; con fixtures reales).

### F4 — Correctitud del motor · XL (varias iteraciones)
- [ ] **F4.1** Cargas distribuidas + peso propio en el solver de motor-fea (hoy solo cargas nodales; viga a gravedad da momento ~0).
- [ ] **F4.2** Reparto viga→columna por REACCIONES reales (hoy 50/50; `RepartoGeometrico.cs:176`).
- [ ] **F4.3** Validar el descenso completo losa→viga→columna→zapata contra un caso de referencia.
- [ ] **F4.4** Profiling del motor FEA con columnas (ineficiencia reportada 2026-06-12) — medir antes de rediseñar el algoritmo.

### F5 — Servicio · M  (tras F4)
- [ ] Deflexión, deriva y torsión como chequeos de servicio en el flujo principal.

### F7 — IA / Memoria · M  (tras F5)
- [ ] Contrato IA estable + memoria con diagramas embebidos.

### F8 restante — Distribución
- [ ] Instalador + release Linux/Windows. ⏸️ Firma de código: requiere certificado (decisión/compra humana).

### F9 — Interoperabilidad · XL · fecha dura 2027-04-10
- [ ] IFC 4.3, elemento MITC4, CDCRD.

### ⏸️ PENDIENTES HUMANOS (no automatizables)
- [ ] **Pase visual de F1**: correr la app (comando de arriba) y confirmar los 6 diagramas + underlay DXF/PDF.
- [x] **Pase visual de UI1 (desbloquea UI1.6)**: en Planta 2D — (1) importar un PDF y calibrarlo con «🎯 Calibrar PDF» (2 clicks + distancia real); (2) importar un DXF, verificar que el plano NO se ve espejado y calcar una losa con «▱ Calcar losa»; (3) dibujar muros y ver la leyenda «Suma de Colores» con colores por espesor; (4) seleccionar una losa, redimensionarla por las 8 asas y deshacer con Ctrl+Z; (5) mover una losa en planta, cambiar a Plano CAD y confirmar que aparece donde se dejó (fin de la desincronización). ✅ 2026-06-11.
- [x] **Pase visual corto post-UI1.6**: en Planta 2D, abrir el Expander «📐 PLANO / PDF» — editar Escala/Offset del DXF y del PDF (el lienzo debe refrescar), mover el slider de opacidad, 🗑 quitar y re-importar; crear un muro con espesor custom desde el panel; exportar a Excel y verificar que el .xlsx trae la imagen de la planta — **también exportando desde otro modo** (p. ej. Editor) después de haber visitado Planta 2D (cubre el re-anclaje de DataContext del review A). ✅ 2026-06-12 (7/7).
- [ ] **Pase visual corto post-UI1.7**: en Planta 2D — (1) con un edificio > 27 m, exportar a Excel y verificar la planta COMPLETA y centrada en el .xlsx (también exportando desde otro modo); (2) botón «⛶ Encuadrar» tras perderse con pan/zoom; (3) importar un PDF y un DXF y ver el encuadre automático; (4) teclear 0 en Escala del DXF/PDF (no debe romper el lienzo); (5) click/drag sobre un muro que pisa una losa opera el muro.
- [ ] **Fixtures vs `Losas.exe` (Windows)**: validar los 12 códigos x3/x4 del mapeo Pieper-Martens (corrección = 1 línea).
- [ ] **Destino de las ramas** `engine/f1…/f3…/f2…` (encadenadas, sin push): ¿PR a `origin/main` o merge a `avalonia-linux`?

---

## 🔎 LUGARES PARA ANALIZAR MÁS BUGS (hotspots, en orden de sospecha)

| # | Lugar | Por qué sospechar |
|---|---|---|
| 1 | `src/Views/PlantaCanvas.cs` (gestos OnPointer*) | El drag del ORIGEN de viga (`:381-389`) recalcula longitud como no-op (usa `ExtremoX` ya movido); hit-test sin handles; cada gesto muta el modelo sin pasar por comandos → difícil de testear. |
| 2 | `src/ViewModels/MainViewModel.cs` (~1700+ líneas) | God-class: paths de import foto/DXF duplicados (`:1517` vs `:1606`), defaults IA hardcodeados, mucha lógica no testeada. Candidata a partir en servicios. |
| 3 | `LayoutSolver` + `Losa.PosX/PosY` vs `CoordenadaX/Y` | La dualidad de coordenadas (causa de la desincronización CAD↔planta). Cualquier feature nueva puede escribir en la "verdad" equivocada. |
| 4 | `src.Core/.../EscenaEdificio*` (3D/WebXR) | Z de columnas/losas cuando `Columna.Altura` ≠ separación de cotas ("columnas elevan niveles"). |
| 5 | `src/Converters.cs:54-62` (`BytesToBitmap`) | Único eslabón runtime del pipeline PNG sin test (los tests de píxeles cubren hasta el byte[]). |
| 6 | `VigaEditorViewModel` recálculo async (`:540-555`) | Cancelación de recálculo: si se aborta, ¿quedan diagramas stale/null? Posible race al cambiar de viga rápido. |
| 7 | Serialización de proyecto (guardar/abrir) | Compatibilidad de proyectos viejos con campos nuevos (`PosX/PosY`, overrides CV/CM futuros); probar abrir un proyecto guardado con versión anterior. |
| 8 | Undo/Redo (`PushUndoSnapshot`) | Consistencia entre editores: ¿cada gesto de PlantaCanvas snapshotea? ¿el resize nuevo lo hará? |
| 9 | Importadores `.DL`/`.TXT`/XLSX (`src.Core/Services`) | Formatos legacy con casos borde (codificación, decimales con coma, archivos truncados). |
| 10 | `DxfImportService` (INSERT anidados, unidades) | Transformación afín de bloques anidados y autodetección de unidades — probar DXF reales de obra. |
| 11 | `QwenAnalizador`/`ClasificadorCapasIA` | Timeouts/errores de Ollama sin red; respuestas malformadas del modelo (parser JSON). |
| 12 | `ValidationEngine` | Cobertura: ¿hay reglas para apoyos fuera de la viga, losas solapadas, columnas duplicadas en la misma coordenada? |

**Método sugerido por hotspot:** test de caracterización primero (fijar el comportamiento actual), luego el fix con TDD — igual que F1–F3.
