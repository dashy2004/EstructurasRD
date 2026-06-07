# Prompt para Antigravity — Interfaz de resultados de losas y edición de acero

> Pegá todo lo de abajo (desde "## Tarea") en Antigravity, abierto sobre el repo
> `EstructurasRD-engine`. Está anclado a tipos reales del código: **no inventes
> nombres**, bindeá a lo que se lista.

---

## Tarea

Rediseñá la **pestaña "Aceros"** y el panel de resultados de la app de diseño de
losas **EstructurasRD / LosasPlus** para que muestre mejor los datos y permita
**editar el acero** inline. Es una app de escritorio **.NET 8 + Avalonia 11 (MVVM)**.

- UI en `src/` (assembly `LosasPlus`); dominio/cálculo en `src.Core/` (`LosasPlus.Core`).
- **No modifiques la lógica de cálculo** en `src.Core/Calculo/**` ni los modelos en
  `src.Core/Models/**` — están testeados. Construí UI que **bindee** a los ViewModels
  existentes; si necesitás estado nuevo, agregalo como observables delgados.
- No rompas `tests/LosasPlus.Tests`. Si agregás lógica de VM, agregá tests xUnit.

## A qué te bindeás (tipos reales)

### `LosasPlus.ViewModels.AcerosViewModel` — `src/ViewModels/AcerosViewModel.cs`
- `ObservableCollection<DisenoAceroFila> Filas` — 4 filas por losa: `X centro`,
  `Y centro`, `X apoyo`, `Y apoyo`.
- `double RecubrimientoCm` y `int BarraSupuesta` (editables; al cambiar, recalculan).
- `int TotalFranjas`, `int FranjasNoCumplen`, `bool HayMomentos`, `string MensajeVacio`.
- `void Recargar()` — recalcula todo desde el modelo (descarta overrides).

### `LosasPlus.ViewModels.DisenoAceroFila` — implementa `INotifyPropertyChanged`
- Identidad: `int LosaId`, `int Tipo`, `string Franja`.
- Demanda: `double MuTonM`, `double DCm`, `double AsRequeridoCm2M`,
  `double AsMinimoCm2M`, `double AsDisenoCm2M`.
- Dispuesto (**editable**): `int NumeroBarra`, `double EspaciamientoCm`,
  `double AsProvistaCm2M`, `string Disponer`.
- Estado: `bool Cumple`, `string Estado` (`"OK"` / `"REVISAR"` / `"SECCIÓN INSUF."`),
  `bool SeccionInsuficiente`, `bool GobiernaMinimo`, `bool EsManual`.
- **Edición**: `void AplicarOverride(int numeroBarra, double espaciamientoCm)` —
  recalcula `AsProvistaCm2M`/`Cumple`/`Disponer`, marca `EsManual = true` y notifica
  vía `PropertyChanged`. Barras válidas **#3..#8**; espaciamiento mínimo 7.5 cm.

### `LosasPlus.Models.SalidaPerdomo` — `src.Core/Models/SalidaPerdomo.cs`
- `ObservableCollection<MomentoLosa> Momentos` — `(LosaId, Tipo, Carga, H, Lx, Ly,
  Mfx, Mfy, NMSx, NMSy)` (ton·m/m).
- `ArmadurasXCentro` / `ArmadurasYCentro : ObservableCollection<ArmaduraLosa>` —
  `(LosaId, D, Mu, As, Disponer, AsReal)`.
- `ArmadurasXApoyos` / `ArmadurasYApoyos : ObservableCollection<ArmaduraApoyo>` —
  `(BordeI, BordeJ, MuI, MuJ, MuIJ, D, As, AsI, AsJ, DAs, Disponer)`.

### Comando de cálculo (ya existe)
- `MainViewModel.CalcularNativo()` — calcula con el motor nativo Pieper-Martens y
  llena `Sistema.SalidaPerdomo` + los momentos de cada losa + el diseño de acero.
- Iconos de tipo de losa: `src/Resources/icons/tipo_<tipo>.svg` (renderizables con
  `Avalonia.Svg.Skia`, ya referenciado). Logo de marca:
  `avares://LosasPlus/Resources/branding/EstructurasRD.svg`.

## Requisitos de UX

1. **Agrupar por losa**: una tarjeta por losa con su número, el **ícono SVG del tipo**
   (`tipo_<Tipo>.svg`) y dimensiones (Lx×Ly, h). Dentro, las 4 franjas.
2. **Grilla de franjas** compacta: Franja · Mu · d · As req · As dispuesto · Disponer ·
   Estado. Números monoespaciados, alineados a la derecha, unidades en los headers
   (ton·m/m, cm, cm²/m).
3. **Editar el acero inline**:
   - `NumeroBarra` → ComboBox `#3..#8`; `EspaciamientoCm` → NumericUpDown (paso 0.5,
     mín 7.5).
   - Al cambiar cualquiera, invocá `fila.AplicarOverride(barra, esp)` y refrescá en vivo.
   - Resaltá las filas con `EsManual == true` (chip "Manual" o fondo distinto) y ofrecé
     "volver a automático" (que llama `Recargar()` con confirmación, pierde overrides).
4. **Demanda vs capacidad**: barra/gauge `AsProvistaCm2M / AsRequeridoCm2M`; color por
   `Estado` → verde `OK`, ámbar `REVISAR`, rojo `SECCIÓN INSUF.`.
5. **Encabezado-resumen**: `TotalFranjas`, `FranjasNoCumplen` (destacar si > 0), botones
   "Calcular nativo" (`CalcularNativo`) y "Exportar a Excel".
6. **Panel de resultados** (opcional, lectura): tablas de `SalidaPerdomo.Momentos` y de
   apoyos (`ArmadurasX/YApoyos`) con `MuI/MuJ/MuI-J`, bien tipografiadas.
7. Estética moderna, alto contraste, coherente con los temas existentes en
   `src/Resources/Theme*.axaml`.

## Restricciones técnicas
- Avalonia 11 `.axaml` + MVVM; bindeo a los VMs listados. Converters/estilos para
  color por `Estado` y resaltado `EsManual`.
- Formato numérico en **cultura invariante** (como el resto del código).
- El export a Excel ya existe (`AcerosLosaExporter` / `OnExportAcerosXlsxClick`) —
  cableá un botón visible que lo dispare.

## Entregables
- Vista(s) `.axaml` para la pestaña Aceros (grilla editable agrupada por losa) y,
  opcional, panel de resultados leyendo `SalidaPerdomo`.
- Converters/estilos (estado→color, EsManual→resalte).
- Botón de export cableado.
- Tests xUnit para cualquier lógica de VM nueva.

## Nota de integración (gap conocido)
Hoy `AplicarOverride` actualiza la **fila de la pestaña Aceros** (y su export a Excel).
Para que los overrides manuales también aparezcan en la **Memoria de cálculo** habría
que escribirlos de vuelta en `SalidaPerdomo.ArmadurasX/YCentro`. Si lo abordás, hacelo
en una capa de VM/servicio (no en `src.Core/Calculo`), con test.
