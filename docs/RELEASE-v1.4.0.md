> ⚠️ Estado real autogenerado → ver [/STATE.md](STATE.md) (este documento puede estar desactualizado).

# Release v1.4.0 — Motor nativo Pieper-Martens + niveles + ejes

Branch: `engine/columnas-diseno` · 957 tests verde.

## Motor de losas Pieper-Martens (nativo, sin Losas.exe)
- **Tablas de Perdomo** embebidas (`TablasPerdomo.json`) + interpolación lineal
  en ε=Ly/Lx (`TablaPieperMartens`).
- **Momentos** por losa (`MomentosCalculator`), incl. voladizo one-way (`q·L²/2`).
- **Balanceo** de momentos en apoyos compartidos (`BalanceoMomentos`): promedio
  con piso 0.75·max, o "vuelo gobierna".
- **Orquestador** `SistemaPieperMartensCalculator` → `SalidaPerdomo` (momentos +
  balanceo + armaduras), reusando `AcerosLosaDesigner`.
- **Comando UI** "Calcular nativo" (menú Engine) → resultado en pestaña Aceros y
  listo para la Memoria.
- Validado número-a-número contra el fixture **RESTAURANTE 2** (`Losas.exe`).

## Estructura por niveles (anclaje)
- `NivelActivo` + selector de nivel (ComboBox + agregar/eliminar) en el shell.
- El sistema activo deriva del **nivel activo** (ya no colapsa a `Niveles[0]`).

## Ejes / rejilla
- `GeneradorEjes.DesdeColumnas` → rejilla **A,B,C / 1,2,3** desde las columnas.
- Comando "Generar ejes del nivel"; `PlantaCanvas` los dibuja en Planta 2D.

## Editar acero
- `AcerosLosaDesigner.AplicarOverride` + `DisenoAceroFila` editable (barra +
  espaciamiento, recálculo en vivo, marca manual) + sync a `SalidaPerdomo`.
- Nueva vista `AcerosView` (grilla por losa).

## Marca / logo
- Logo **EstructurasRD** en: encabezado de la Memoria `.docx`, esquina de la UI,
  e **ícono de ventana** al ejecutar.

## Docs
- `docs/superpowers/specs/2026-06-03-motor-pieper-martens-nativo-design.md`
- `src.Core/Calculo/PieperMartens/TABLAS-PERDOMO.md`
- `docs/plan-anclaje-niveles.md`, `docs/antigravity-prompt-aceros-ui.md`

## Pendiente (próximo)
- Unificar Lienzo CAD + Planta 2D (base Planta 2D).
- Verificación visual de la UI (no posible en sesión headless).
- IA local **Qwen** (read-only) para PDF/DXF/imagen → elementos (ver
  `docs/qwen-setup.md`).