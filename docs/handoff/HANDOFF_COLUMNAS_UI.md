# Hand-off → Antigravity — UI de Columnas (diseño + diagrama de interacción P-M)

> **Lane.** Claude dejó el **motor de cálculo completo y testeado** (`src.Core`,
> headless); falta la **UI** (pestaña + plot del diagrama), que necesita ojos sobre
> pixeles. Rama: `engine/columnas-diseno` (5 commits, 825/825 verde, sin push).
> El **VM headless** (`ColumnasEditorViewModel`) lo está cableando Claude en paralelo
> con tests — vos te enganchás a sus propiedades para la View.

## Lo que ya existe (motor, no tocar — sólo consumir)

`src.Core/Calculo/ColumnaDisenador.cs` (ACI 318-19, SI: N, mm, MPa), cruza-validado
contra el motor Python `aci318.py`:

```csharp
// Sección rectangular + barras (posiciones respecto al centro, mm² c/u)
var barras  = ColumnaDisenador.LayoutPerimetral(b, h, recubrimiento, nx, ny, numeroBarra);
var seccion = new ColumnaSeccion(b, h, fcMPa, fyMPa, barras);

// Diseño completo de punta a punta:
ColumnaDisenador.DisenoColumna d = ColumnaDisenador.DisenarColumna(seccion, numeroBarraLong, puN, muNmm);
```

`DisenoColumna` expone:
- `RhoG` (cuantía), `CumpleCuantia` (1–8 %)
- `PoN` (squash, N), `PhiPnMaxN` (compresión máx de diseño, N)
- `Estribo { Numero, SeparacionMm }` (§25.7.2)
- `Chequeo { PhiPnMaxN, PhiMnCapacidadNmm, Ratio, Cumple }` (demanda Pu,Mu)
- `Diagrama` = `IReadOnlyList<PuntoPM>`, cada `PuntoPM { C, Pn, Mn, Et, Phi, PhiPn, PhiMn }`
  (nominales + φPn/φMn de diseño). **Unidades: N y N·mm.**

Export headless para inspección: `ColumnaDisenoExporter.ExportCsv(d, path)` (resumen + tabla del diagrama).

---

## Tarea 1 — Plot del diagrama de interacción P-M

**Pedido del usuario:** «diagrama de iteración [interacción], compresión máxima, diagramas».

Usá **OxyPlot** (ya está en el proyecto — lo usa el editor de Vigas con `PlotView` +
`ModeloEsfuerzos`). Mismo patrón.

**Ejes** (convención clásica de columnas):
- **X = φMn** (momento de diseño), **Y = φPn** (axial de diseño). Convertí a unidades
  amigables en el VM: kN·m = `N·mm / 1e6`, kN = `N / 1000`.
- Serie de línea con `d.Diagrama.Select(p => (p.PhiMn/1e6, p.PhiPn/1000))` → la curva.
- **Línea horizontal** del tope `φPn,max` = `d.PhiPnMaxN/1000` (recta punteada — la
  compresión no puede superarla).
- **Punto de demanda** (Mu, Pu): marcador. **Verde si `d.Chequeo.Cumple`, rojo si no.**
- Opcional: marcar el punto balanceado y el de flexión pura (se ven como el "codo" y
  el cruce con el eje X).

**Archivos:** un control/usercontrol de plot que bindea a una propiedad del VM (p. ej.
`PlotModel ModeloInteraccion`), dentro de la pestaña Columnas de `MainWindow.axaml`.

> **Contrato con el VM (Claude):** el VM va a exponer la lista de puntos y el resultado
> ya en unidades de diseño. Si preferís que exponga directamente un `PlotModel` de
> OxyPlot armado, **pedímelo** y lo agrego del lado del VM (es lógica, no pixeles).
> Decidí vos dónde querés la frontera.

## Tarea 2 — Pestaña/inputs de Columnas

**Inputs** (bindean a props del VM que Claude está agregando):
- Material: `FcMPa`, `FyMPa`.
- Armado: `RecubrimientoMm`, `NumeroBarra`, `BarrasX` (nx), `BarrasY` (ny).
- Demanda: `PuKN`, `MuKNm`.
- (La geometría b×h sale de la `Columna` seleccionada — `Base`/`Peralte`, en metros.)

**Panel de resultados** (read-only, bindea al `DisenoActual` del VM):
- ρg + chip OK/CHK (cuantía), Po, φPn,max, estribo `#n @ s mm`, **ratio de demanda** +
  chip CUMPLE/NO CUMPLE.

**Estado vacío:** si no hay columna seleccionada, mostrar guía «seleccioná una columna».

**Archivos:** región de la pestaña Columnas en `MainWindow.axaml` (hoy
`ColumnasEditorView` con el DataGrid de columnas) + el nuevo panel de diseño/plot.

---

## Notas de seguridad (Antigravity)

- `ControlTheme` de controles nativos: siempre `BasedOn="{StaticResource {x:Type <Control>}}"`.
- Mantené la suite verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests`.
- **Contrato-primero:** cualquier ajuste de firma del motor o del VM lo hace Claude
  (lane headless); vos cableás la View contra esas firmas.
- Unidades: el motor está en **N/mm**; convertí a **kN/kN·m** sólo en la capa de
  presentación.

## Archivos clave

- Motor: `src.Core/Calculo/ColumnaDisenador.cs` · `src.Core/Services/ColumnaDisenoExporter.cs`
- Tests (referencia de uso): `tests/LosasPlus.Tests/ColumnaDisenadorTests.cs`
- VM: `src/ViewModels/ColumnasEditorViewModel.cs` (Claude lo está extendiendo)
- View: `src/Views/ColumnasEditorView.*` + región Columnas en `src/MainWindow.axaml`
