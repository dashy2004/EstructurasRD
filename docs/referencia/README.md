# Referencias

Material fuente del módulo Memoria Plus (generador de memorias de cálculo estructural).

## Archivos

| Archivo | Origen | Uso |
|---|---|---|
| `Memoria_Losas_PLANTILLA.docx` | Plantilla del ingeniero | Base sobre la que `MemoriaPlus.App` reemplaza placeholders y rellena tablas. **No modificar** — copia de trabajo se vive en `MemoriaPlus.App/Resources/templates/`. |
| `Memoria_Referencia_LuisSamboy.docx` | Memoria real ejecutada (proyecto Luis Samboy) | Golden de referencia para diff visual y validación de plurinivel. Convertido desde `.doc` legacy. |
| `ARCHIVO_ESTRUCTURAL_2025.xlsx` | Libro Excel de cálculo del ingeniero | Fuente de las fórmulas (espesor, espesor equivalente, cargas) y tablas semilla para `Cargas globales`. Convertido desde `.xls` binario. |
| `ui-design/` | Stitch — Google Labs | Wireframes de las 7 pantallas de `MemoriaPlus.App` + tokens de diseño (`DESIGN.md`). |

## Hojas relevantes del .xlsx

| Hoja | Uso |
|---|---|
| `Cargas` | Tabla de carga muerta por espesor (h=0.06..0.20), pesos propios entrepiso/techo, cargas vivas, factores de combinación ACI 318-05. |
| `Espesor Earlette` (y E2..E5, Techo) | Cálculo de h por losa: detección 1D/2D, h_calc, h_usar, espesor equivalente para vigueta + bloque. |
| `Espesor Equivalente` | Tabla de lookup de espesores equivalentes pre-tabulados para combinaciones estándar de vigueta/bloque. |
| `Carga EARLLETTE` (y E2..E5, Techo) | Cargas por losa: Q_mamp, Qmap, qd, ql, qu. |

## Fórmulas verificadas (decisiones de implementación)

| Magnitud | Fórmula | Notas |
|---|---|---|
| Cond | `IF(MAX(Lx,Ly)/MIN(Lx,Ly) > 2, "1D", "2D")` | |
| Ln | `IF(1D, MIN(Lx,Ly), MAX(Lx,Ly))` | |
| h_calc 2D | `Ln*(0.8 + Fy/14000) / (36 + 9*ratio)` | ACI 9.5.3 |
| h_calc 1D | `Ln/K`, K ∈ {20, 24, 28, 10} | xls hardcodea K=10. **MemoriaPlus expone K configurable** (ACI 9.5.2.1). |
| h_usar | `MAX(0.12, ROUND(h_calc, 2))` | |
| Q_mamp | `1.8 * (h_piso − h_losa) * (0.2·N + 0.15·O + 0.1·P)` | N, O, P = m lineales por espesor de bloque. ρ_bloque = 1.8 ton/m³. |
| Qmap | `IF(Q_mamp = 0, 0, MAX(0.10, Q_mamp/Area))` | |
| qd | `lookup_qd(h_eq) + Qmap` | |
| qu | `1.2·qd + 1.6·ql` | ACI 318-05. Factores configurables. |

## Discrepancias entre xls y MemoriaPlus

1. **K (factor 1D)**: xls = 10 fijo; MemoriaPlus = configurable {20, 24, 28, 10} por losa.
2. **Espesor equivalente**: xls usa VLOOKUP en hoja pre-tabulada como fuente principal con cálculo paramétrico en paralelo. MemoriaPlus computa paramétricamente por defecto (con bw, H, Is, αfm) y permite VLOOKUP opcional sobre la tabla pre-tabulada importada del xls.
3. **Cantidad de niveles**: xls limitado a 6 (Earlette/E1, E2, E3, E4, E5, Techo). MemoriaPlus no tiene límite (1..N).
4. **Pesos propios — UI vs xls**: la pantalla de Stitch alucinó "Hormigón Armado / Acero / Hormigón Ligero / Cubierta Metálica". La semántica real es:
   - Entrepiso: Mosaicos (0.069), Mortero (0.072), Pañete (0.040)
   - Techo: por inspeccionar en el .xlsx (filas 65-68 de hoja `Cargas`).

## Aviso

Estos archivos contienen información de proyectos reales (Neapolis IV, ingeniero Oliver Guillén Rosa CODIA 18139; proyecto Luis Samboy). Se mantienen en este repo solo para servir de golden de tests y referencia de plantilla. **No redistribuir** sin consentimiento del autor.
