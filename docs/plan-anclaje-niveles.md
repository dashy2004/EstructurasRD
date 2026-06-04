# Plan — Anclaje a niveles (editor nivel-aware)

**Prioridad del usuario:** es la base; sin esto no se pueden generar/editar varios
niveles ni, después, la generación con IA local (Qwen).

## Diagnóstico (estado actual del código)

- `Proyecto.Sistemas` es una **fachada de compatibilidad** que devuelve
  `Edificios[0].Niveles[0].Sistemas` — está cableada **al primer nivel del primer
  edificio** (`src.Core/Models/Sistema.cs:98`).
- `MainViewModel.Sistema` (setter, `src/ViewModels/MainViewModel.cs:598`) hace
  `_proyecto.Sistemas.Clear(); _proyecto.Sistemas.Add(value)` → **colapsa todo a
  `Niveles[0]` con un solo sistema**.
- **No existe `NivelActivo`.** Solo `EdificioActivo => Edificios.FirstOrDefault()`.
- El árbol real `Edificio → Nivel → {Sistemas, Vigas, Columnas}` existe en el
  modelo y se **serializa** (`Edificio.cs`), pero el editor solo ve el nivel 0.
- Los **ejes** (`Edificio.Ejes : EjeEstructural`) están en el modelo
  (compartidos por todas las plantas) pero **no se dibujan** en CAD ni Planta 2D.

**Consecuencia:** la app es de facto mono-nivel; los elementos "no se anclan a
niveles" porque el editor no tiene noción de nivel activo.

## Objetivo

Editor **nivel-aware**: edificio activo + **nivel activo**; el canvas y los
editores muestran/editan los elementos del nivel activo; los elementos nuevos se
**anclan al nivel activo**; se pueden crear/cambiar niveles; serialización y undo
respetan el árbol completo.

## Pasos (TDD estricto, incremental — cada paso build+test verde)

1. **Navegación activa (modelo/VM, aditivo y testeable)**
   - VM: `NivelActivo` (ref al `Nivel` activo del `EdificioActivo`),
     `NivelesDelEdificio`, `IndiceNivelActivo`.
   - Métodos: `AgregarNivel(nombre, cota)`, `SeleccionarNivel(n)`, `EliminarNivel(n)`.
   - Tests: seleccionar/agregar nivel cambia el conjunto de Sistemas/Columnas/Vigas
     expuesto; los demás niveles se preservan.

2. **Sistema activo derivado del nivel activo**
   - `SistemaActivo` pasa a ser un sistema **del `NivelActivo`** (no de `Niveles[0]`).
   - Reemplazar el setter "clobber" por uno que opere **dentro del nivel activo**
     sin borrar otros niveles.
   - Tests de regresión: cambiar de nivel preserva los sistemas de los demás;
     agregar una losa cae en el nivel activo; serialización round-trip con ≥2 niveles;
     undo/redo no aplana el árbol.

3. **UI: selector de nivel (Avalonia — no verificable headless, mínimo)**
   - ComboBox de niveles + botón "Agregar nivel" (patrón de `SistemasList`).
   - Rebind del canvas/editores al nivel activo.

4. **Ejes en el editor (depende de base Planta 2D)**
   - Render de `Edificio.Ejes` en Planta 2D; etiquetas y snap a ejes.

5. **Unificar CAD + Planta 2D (base = Planta 2D)** — fase aparte, ya decidida.

## Riesgos / por qué TDD e incremental

- El setter "clobber" lo usan `AbrirProyecto`/load, undo (`ProyectoSerializer`) y
  los bindings XAML (`Proyecto.Sistemas`). Cambiarlo exige **tests de regresión de
  serialización y undo** antes de tocarlo.
- Pasos 3–5 son UI Avalonia: **no verificables en sesión headless** → prueba manual.

## Después (visión)

Con 1–4 sólidos, la **IA local (Qwen)** puede: leer PDF/DXF/imágenes → generar
elementos **anclados al nivel correspondiente** y por eje. No empezar hasta que el
modelo por niveles esté bien programado (pedido explícito del usuario).
