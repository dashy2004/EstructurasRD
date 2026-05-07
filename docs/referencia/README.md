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

## Convención de placeholders en la plantilla

`MemoriaGenerator` (en `LosasPlus.Core/Generation/`) sustituye placeholders `{{KEY}}` por valores del proyecto. Hay dos grupos:

### 1. Placeholders de portada (17, sustitución uno-a-uno)

Donde aparezcan en la plantilla — body, headers o footers — se reemplazan por el valor correspondiente del `Proyecto` activo. Lista completa en `PlaceholderConstants.Todos`:

```
{{NOMBRE_PROYECTO}}        {{TEL_FIJO}}             {{TIPO_FUNDACIONES}}
{{CIUDAD_UBICACION}}       {{TEL_CELULAR}}          {{ESFUERZO_ADMISIBLE}}
{{MES_AÑO}}                {{NOMBRE_DISEÑADOR_ARQ}} {{PROFUNDIDAD_DESPLANTE}}
{{UBICACION_COMPLETA}}     {{NOMBRE_INGENIERO}}     {{OTROS_PARAMETROS}}
{{USO}}                    {{CODIA}}                {{DD/MM/AAAA}}
{{CANTIDAD_NIVELES}}       {{SISTEMA_ESTRUCTURAL}}
```

`{{DD/MM/AAAA}}` es la fecha de generación (timestamp del momento de correr el botón Generar), no un campo del proyecto.

### 2. Bloque plurinivel (clonado por `Sistema`)

Si la plantilla tiene **dos markers de bloque**, todo lo que esté entre ellos se clona una vez por cada `Sistema` del proyecto, sustituyendo placeholders del nivel en cada clon:

```
... contenido de portada ...

{{NIVEL_BLOQUE_INICIO}}        ← marker (en su propio párrafo)

  ## Diseño de Losas - Nivel {{NIVEL_NOMBRE}}        ← se clona N veces
  Uso: {{NIVEL_USO}}, cota +{{NIVEL_COTA}}
  Cantidad de losas: {{NIVEL_NUMERO_LOSAS}}
  ... más contenido por nivel (tablas, descripciones, etc.) ...

{{NIVEL_BLOQUE_FIN}}           ← marker (en su propio párrafo)

... contenido de cierre / firma ...
```

Placeholders disponibles dentro del bloque (`PlaceholderConstants.TodosNivel`):

| Placeholder | Valor |
|---|---|
| `{{NIVEL_NOMBRE}}` | Nombre del sistema (`E1`, `E2`, `Techo`, ...) |
| `{{NIVEL_NUMERO}}` | Índice 1-based (1, 2, 3, ...) |
| `{{NIVEL_USO}}` | `Entrepiso`, `Techo`, `Balcon`, `Otro` |
| `{{NIVEL_COTA}}` | Cota desde el +0.00 formateada (`+2.80 m`) |
| `{{NIVEL_NUMERO_LOSAS}}` | Cantidad de losas en el sistema |

Reglas:

- Cada marker debe vivir en **su propio párrafo** (texto plano del marker, sin más contenido en la línea). El generador remueve el párrafo completo del marker después de renderizar.
- Si la plantilla **no** tiene los markers, el generador procede solo con la sustitución de portada (compat con plantillas simples).
- Si hay 0 sistemas en el proyecto, el bloque NIVEL desaparece del output completo (no quedan markers ni contenido del bloque). El proyecto puede generarse aunque no haya niveles capturados.
- Los placeholders de portada también se sustituyen dentro de los clones (orden: clonado plurinivel → reemplazo de portada). Útil si querés repetir `{{NOMBRE_PROYECTO}}` en cada nivel, por ejemplo en el header de cada sección.

### ¿Cómo agrego los markers a `Memoria_Losas_PLANTILLA.docx`?

La plantilla actual del ingeniero **no** tiene markers — fue diseñada para un solo nivel hardcodeado. Para activar plurinivel:

1. Abrir `Memoria_Losas_PLANTILLA.docx` en Microsoft Word.
2. Identificar la sección que el ingeniero hoy duplica manualmente por cada nivel (típicamente el bloque "DISEÑO DE LOSAS - Nivel X" con sus tablas).
3. Insertar `{{NIVEL_BLOQUE_INICIO}}` en un párrafo nuevo justo arriba del bloque.
4. Reemplazar los nombres hardcodeados (`E1`, etc.) por los placeholders correspondientes (`{{NIVEL_NOMBRE}}`, etc.).
5. Insertar `{{NIVEL_BLOQUE_FIN}}` en un párrafo nuevo justo debajo del bloque.
6. Guardar.

Para validar sin tocar la plantilla original, los tests usan plantillas programáticas creadas con OpenXml — ver `tests/LosasPlus.Tests/MemoriaGeneratorPluriNivelTests.cs::ConstruirPlantillaConMarkers`.

## Aviso

Estos archivos contienen información de proyectos reales (Neapolis IV, ingeniero Oliver Guillén Rosa CODIA 18139; proyecto Luis Samboy). Se mantienen en este repo solo para servir de golden de tests y referencia de plantilla. **No redistribuir** sin consentimiento del autor.
