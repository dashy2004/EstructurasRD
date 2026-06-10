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
- [ ] **UI1.3** Click sobre polilínea del DXF → losa (MapearPoligono, `CadEditorViewModel.cs:847-887`) en PlantaCanvas.
- [ ] **UI1.4** Leyenda "suma de colores" de muros como overlay (`CadEditorViewModel.cs:529`).
- [ ] **UI1.5** Resize de LOSAS con handles en PlantaCanvas (reciclar el patrón de `CadCanvasHost.cs:956-970`).
- [ ] **UI1.6** Retirar `CadView`/`CadCanvasHost`/`CadEditorViewModel` del shell (modo PlanoCad) — solo cuando UI1.1–1.5 estén verdes.

### UI2 — Modelo unificado Nivel⊕Sistema · L
> "Niveles separados del sistema" — en la práctica siempre se usa `Sistemas[0]` (hardcodeado en 6 sitios de `src/`).

- [ ] **UI2.1** Fachada `Nivel.Losas`/`Nivel.Bordes` que delega a `Sistemas[0]` (creándolo si falta).
- [ ] **UI2.2** Migrar los 6 usos de `Sistemas[0]` a la fachada; serialización JSON intacta (compatibilidad con proyectos guardados).
- [ ] **UI2.3** Semántica columna↔cota en el 3D: definir qué pasa con una columna de 6 m en un nivel de 3 m (¿atraviesa al nivel 2?) y corregir `EscenaEdificio*` ("las columnas elevan los niveles/losas").
- [ ] **UI2.4** Carga viva/muerta POR LOSA: overrides anulables `CargaMuerta?`/`CargaViva?` sobre el global de `CargasGlobales` + campo en la UI de propiedades + lectura en `CalculoEngine` (combinaciones 1.2D+1.6L).

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

### F2b — CAD restante · M
- [ ] **F2b.1** Heurística forma→columna en capa ambigua (`DxfEstructuraMapper.cs:65`; hoy círculo en capa ambigua ⇒ 0 columnas).
- [ ] **F2b.2** Columnas en el path de visión (`QwenAnalizador.cs:94-120` devuelve `Array.Empty`). Requiere Ollama + fixtures.
- [ ] **F2b.3** Heurística rectángulo-en-capa-Viga → viga con ancho (hoy solo se avisa; con fixtures reales).

### F4 — Correctitud del motor · XL (varias iteraciones)
- [ ] **F4.1** Cargas distribuidas + peso propio en el solver de motor-fea (hoy solo cargas nodales; viga a gravedad da momento ~0).
- [ ] **F4.2** Reparto viga→columna por REACCIONES reales (hoy 50/50; `RepartoGeometrico.cs:176`).
- [ ] **F4.3** Validar el descenso completo losa→viga→columna→zapata contra un caso de referencia.

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
