> ✅ Pasos 4-5 ejecutados en F1 (2026-06-10) — ver `docs/superpowers/specs/2026-06-10-f1-verdad-visual-design.md` §2.4/§4.4. Este plan queda como referencia histórica.

# Plan — Unificar Lienzo CAD + Planta 2D (base Planta 2D)

## Estado actual (dos editores separados)

| | Planta 2D (`Planta2DEditorView`) | Plano CAD (`CadView`) |
|---|---|---|
| Canvas | `PlantaCanvas` (Nivel/Edificio) | `CadCanvasHost` (CadEditor + Sistema) |
| Herramientas | Puntero, +Losa, +Viga, +Columna, +Eje, snap | Puntero, Dibujar Losa, Dibujar Muro, Mano, Auto-Conectar, snap |
| Único de cada uno | Vigas, Columnas, **Ejes**, "Recalcular Descenso", panel propiedades | **Importar DXF/PDF** (underlay), calibrar PDF, ajuste espacial, **Muros** ("suma de colores") |
| ViewModel | `NivelActivo`/`EdificioActivo` | `CadEditor` (sub-VM) + `Sistema` |

Son **dos `Control` con render e interacción propios**. Fusionar el render en un
solo canvas es el grueso del trabajo (y no es verificable en sesión headless).

## Objetivo
Un **único editor** (base Planta 2D) donde el ingeniero hace todo: estructura
(losas/vigas/columnas/ejes) **e** import/CAD (DXF/PDF/muros), sin cambiar de "ventana".

## Estrategia incremental (cada paso con el usuario presente para validar UI)

1. **Unificar la superficie (HECHO — este commit).** `EditorUnificadoView`: un solo
   editor con sub-pestañas **Estructura** (default = `Planta2DEditorView`) y **CAD**
   (`CadView`). Una sola entrada de menú. Los dos canvas siguen internos, pero el
   usuario ve **un** editor. Reversible: las vistas viejas no se borran.
2. **Underlay compartido.** Llevar el render de **DXF/PDF** (capa de referencia)
   a `PlantaCanvas`, para trazar estructura sobre el plano importado sin cambiar de
   pestaña. (El comando de import ya existe en `CadEditor`.)
3. **Muros en Planta 2D.** Portar el dibujo/lista de muros a la superficie base.
4. **Auto-Conectar** disponible desde la base.
5. **Retirar** la pestaña CAD cuando 2–4 estén cubiertos en la base → un solo canvas.

## Riesgos
- UI Avalonia con dos canvas custom: **no verificable headless** → cada paso 2–5
  requiere prueba manual del usuario.
- Archivos compartidos con el otro agente (`MainWindow.axaml`, vistas) → editar con
  re-lectura y commits frecuentes.

## Nota
El cálculo (Pieper-Martens) y el modelo por niveles NO cambian: esto es solo la
capa de edición/visualización.
