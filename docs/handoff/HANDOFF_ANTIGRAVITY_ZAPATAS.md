# Hand-off → Antigravity — UI de diseño de zapatas

> El motor (Claude) dejó **headless y verde** (929/929) el diseño completo de zapatas
> aisladas (ACI 318-19). Falta surfacearlo en la UI. Rama `engine/columnas-diseno`.
> Todo lo de abajo es binding/render; la lógica ya existe y está testeada en `src.Core`.

## Motor disponible (puro, testeado)

`src.Core/Calculo/ZapataDisenador.cs` (SI: N, mm, MPa):
- `DisenarZapata(puN, bMm, lMm, c1Mm, c2Mm, dMm, hMm, fcMPa, fyMPa)` → `DisenoZapata`:
  `{ QuMPa, Punzonamiento{VuN,PhiVcN,Ratio,Cumple}, Cortante{...}, MuNmm, Acero{AsReqMm2,AsMinMm2,AsMm2,SeccionInsuficiente}, Cumple }`.
- Piezas sueltas si las necesitás: `PresionContactoUltima`, `ChequeoPunzonamiento`,
  `ChequeoCortanteUnidireccional`, `MomentoFlexionZapata`, `AceroFlexionZapata`.
- Exporter: `src.Core/Services/ZapataDisenoExporter.ToCsv/ExportCsv`.

## UI a construir

En la pestaña de **Bajada de cargas / Zapatas** (donde ya está `DescensoColumnas` y el
predimensionado), por cada columna/zapata:
1. **Alimentar el diseño:** `Pu` viene del descenso (axial de la columna, ton → N: ×9806.65),
   la geometría B×L del predimensionado (`PredimZapata`/`Zapata.Ancho/Largo`, m → mm), c1×c2 de
   la columna (m → mm), `d ≈ h − recubrimiento` (h de `Zapata.Peralte`). Llamar `DisenarZapata(...)`.
2. **Mostrar el resultado:** presión q_u, ratios de **punzonamiento** y **cortante**
   (verde/rojo según `.Cumple`), **Mu**, **As** requerido vs mínimo, y un sello global
   **CUMPLE / NO CUMPLE** (`DisenoZapata.Cumple`).
3. **Botón ⬇ CSV** que llame `ZapataDisenoExporter.ExportCsv(diseno, path)`.

*(Si preferís que el wiring lo exponga un comando/propiedad en el VM de bajada, pedímelo —
es lane de motor; yo lo agrego en `src.Core`/VM y vos hacés los pixeles.)*

## Notas
- Unidades del motor: N, mm, MPa. Convertí en la frontera (ton↔N ×9806.65; m↔mm ×1000).
- Supuestos del diseño: columna interior, zapata cuadrada concéntrica sin momento, λ=1.
- Mantené verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (929/929 hoy).
- ⛔ No eliminar `Losas.exe`.
