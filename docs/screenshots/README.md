# Guía de Capturas de Pantalla — LosasPlus

Esta carpeta documenta visualmente **toda la aplicación** LosasPlus. Cada
subcarpeta corresponde a un área de la app; dentro van las capturas con un
nombre de archivo fijo definido en las tablas de abajo.

> **Importante:** las capturas las toma una persona en una máquina que esté
> corriendo `LosasPlus.exe`. Esta guía dice **qué** capturar, **cómo
> llegar** a cada pantalla y **con qué nombre** guardar cada archivo.

## Cómo capturar

1. Compilá y abrí la app: `dotnet run --project src` (o `LosasPlus.exe`).
2. Maximizá la ventana para que las capturas sean consistentes.
3. Usá **Win + Shift + S** (Recorte de Windows) o la **Herramienta de
   Recortes**. Para ventana completa: `Alt + Impr Pant`.
4. Guardá cada imagen como **PNG** en la subcarpeta indicada, con el
   **nombre exacto** de la columna «Archivo».
5. Usá el tema **Precision** (el de fábrica) en todas las capturas, salvo
   las de la sección `00-shell-temas` que piden Light y Dark.
6. Para datos realistas: tené un proyecto abierto con varios sistemas y
   losas cargadas antes de capturar.

## Convención de nombres

```
NN-descripcion-en-kebab.png
```

- `NN` — secuencia de dos dígitos dentro de la carpeta (`01`, `02`, …).
- `descripcion-en-kebab` — minúsculas, sin acentos ni espacios, palabras
  unidas por guiones.
- Formato **PNG** siempre.
- Ejemplo: `docs/screenshots/03-lienzo-cad/11-panel-pdf.png`.

---

## 00 · Shell y temas — `00-shell-temas/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-shell-tema-precision.png` | Ventana completa, modo Explorador, tema Precision | Abrir la app (tema de fábrica) |
| `02-shell-tema-light.png` | La misma vista con el tema claro | Botón «Tema» → ciclar a Light |
| `03-shell-tema-dark.png` | La misma vista con el tema oscuro | Botón «Tema» → ciclar a Dark |
| `04-barra-superior.png` | Close-up de la barra superior: selector SISTEMA, rename inline, +/−, botones Tema y Apariencia | Recortar solo la franja superior |
| `05-selector-modos.png` | Close-up del selector de los 12 modos de navegación | Recortar la fila de botones de modo |

## 01 · Explorador (Hub de proyectos) — `01-explorador/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-explorador-vacio.png` | Hub sin proyectos recientes | Modo «Explorador», sin recientes |
| `02-explorador-con-recientes.png` | Hub con la tabla de proyectos recientes poblada | Modo «Explorador» tras abrir/crear proyectos |
| `03-card-proyecto-activo.png` | La tarjeta «Proyecto activo» (nombre, autor, código de obra, Guardar / Guardar como) | Recortar la card del proyecto activo |

## 02 · Editor de losas — `02-editor-losas/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-editor-vacio.png` | Editor con un sistema sin losas | Modo «Editor», sistema vacío |
| `02-editor-con-losas.png` | El DataGrid de losas poblado | Modo «Editor» con losas cargadas |
| `03-toolbar-editor.png` | Toolbar superior: filtro de tipo, +Losa/−Losa, Pegar Excel, Undo/Redo, Atajos | Recortar la toolbar del Editor |
| `04-bulk-apply-panel.png` | El panel de aplicación masiva | Seleccionar 2+ losas en el grid |
| `05-panel-lateral-sistema.png` | Panel lateral derecho: sistema activo, lista de sistemas, FC / FY / Adicionales, nombre del sistema | Recortar el panel lateral |
| `06-columna-tipo-selector.png` | La columna TIPO del grid con su selector | Recortar la columna TIPO |

## 03 · Lienzo CAD — `03-lienzo-cad/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-lienzo-vacio.png` | Lienzo CAD vacío con la grilla métrica | Modo «Lienzo CAD», sin losas ni plano |
| `02-toolbar-flotante.png` | Close-up de la toolbar flotante (Puntero / Dibujar Losa / Mano / Auto-Conectar / Snap / Conectadas) | Recortar la toolbar superior-centro |
| `03-losas-dibujadas.png` | Varias losas dibujadas con patrón interior (1D-H / 1D-V / 2D) y rótulo | Dibujar losas con la herramienta «Dibujar Losa» |
| `04-losa-seleccionada.png` | Una losa seleccionada con sus tiradores de redimensión | Click en una losa con la herramienta Puntero |
| `05-editor-flotante-losa.png` | El editor flotante in-canvas (Lx / Ly / Tipo) | Doble clic sobre una losa |
| `06-chips-adyacencia.png` | Los chips «+» de adyacencia entre losas vecinas | Dibujar dos losas adyacentes |
| `07-marcas-acero.png` | Losas conectadas mostrando las marcas de acero adicional | Conectar dos losas (chip + o Auto-Conectar) |
| `08-dxf-importado.png` | Un plano DXF importado como fondo | Botón «Importar DXF…» → elegir un .dxf |
| `09-panel-dxf.png` | Panel lateral: «PLANO IMPORTADO» + «AJUSTE ESPACIAL» (escala, offset, Encuadrar, 🗑) | Recortar el panel lateral con un DXF cargado |
| `10-pdf-importado.png` | Un PDF importado como underlay | Botón «Importar PDF…» → elegir un .pdf |
| `11-panel-pdf.png` | Panel lateral: «PDF IMPORTADO» + «AJUSTE ESPACIAL PDF» (escala, offset, slider de opacidad, checkbox Modo Oscuro, Encuadrar, Calibrar, 🗑) | Recortar el panel lateral con un PDF cargado |
| `12-pdf-modo-oscuro.png` | El PDF con «Modo Oscuro CAD» activado (fondo negro, líneas blancas) | Checkbox «🌙 Modo Oscuro» del panel PDF |
| `13-pdf-opacidad-reducida.png` | El PDF atenuado con el slider de opacidad bajo | Bajar el slider «Opacidad» del panel PDF |
| `14-calibracion-pdf.png` | El editor flotante de calibración (línea entre dos puntos + distancia real) | Botón «📐 Calibrar PDF» → marcar dos puntos |
| `15-herramienta-mano.png` | El lienzo con la herramienta «Mano» activa (cursor de mano) | Seleccionar el modo «✋ Mano» en la toolbar |
| `16-auto-conectar-resultado.png` | El sistema tras «Auto-Conectar»: losas alineadas y conexiones generadas | Botón «🤖 Auto-Conectar» con losas vecinas dibujadas |
| `17-pie-ayuda-interaccion.png` | El pie con la ayuda de interacción (zoom, pan, crear/mover losas) | Recortar la franja inferior del lienzo |

## 04 · Flujo .DL / .TXT — `04-flujo-dl-txt/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-editor-dl.png` | El editor del archivo .DL generado + botón «Aplicar al modelo» | Modo «Editor .DL» |
| `02-salida-txt-texto.png` | La salida .TXT, pestaña «Texto» (resaltado sintáctico) | Modo «Salida .TXT» → pestaña Texto |
| `03-salida-txt-tabla.png` | La salida .TXT, pestaña «Tabla (editable)» | Modo «Salida .TXT» → pestaña Tabla |
| `04-aceros-placeholder.png` | La pantalla «Próximamente» del modo Aceros *(opcional)* | Modo «Aceros» |

## 05 · Validación normativa — `05-validacion/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-validacion-lista.png` | La lista de reglas / validaciones normativas | Modo «Validación» |
| `02-validacion-detalle.png` | El detalle de una violación o advertencia | Seleccionar una entrada de la lista |
| `03-validacion-indicadores.png` | Los indicadores de severidad (verde / naranja / rojo) | Recortar la zona de indicadores |

## 06 · Búsqueda — `06-busqueda/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-busqueda-vacia.png` | La vista de búsqueda sin consulta | Modo «Búsqueda» (o Ctrl+E) |
| `02-busqueda-resultados.png` | La búsqueda con resultados (proyectos / sistemas / losas) | Escribir una consulta con resultados |

## 07 · Reglamento — `07-reglamento/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-reglamento-lista.png` | El listado de normas disponibles | Modo «Reglamento» |
| `02-reglamento-detalle.png` | El detalle de una norma seleccionada | Seleccionar una norma del listado |

## 08 · Plugins — `08-plugins/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-plugins-lista.png` | Las tarjetas de plugins cargados (badge Confiable / No confiable, Trust / Revoke) | Modo «Plugins» con plugins cargados |
| `02-plugins-vacio.png` | El modo Plugins sin plugins *(opcional)* | Modo «Plugins» sin plugins |

## 09 · Configuración — `09-configuracion/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-config-datos-ingeniero.png` | Pestaña «Datos del Ingeniero» (nombre, CODIA, especialidad, contacto, firma) | Modo «Configuración» → pestaña 👤 |
| `02-config-apariencia.png` | Pestaña «Apariencia» (tema, tipografía, color de acento, densidad) | Modo «Configuración» → pestaña 🎨 |
| `03-config-atajos.png` | Pestaña «Atajos de teclado» (editor de atajos) | Modo «Configuración» → pestaña ⌨ |

## 10 · Diálogos y ventanas — `10-dialogos/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-dialogo-atajos.png` | Ventana de atajos de teclado | `Ctrl + /` o el botón «Atajos» |
| `02-dialogo-personalizar-colores.png` | Diálogo «Personalizar colores» (tokens HEX + sliders RGB) | Botón «Apariencia» 🎨 de la barra superior |
| `03-dialogo-pegar-excel.png` | Diálogo «Pegar de Excel» (preview de la tabla parseada) | Modo Editor → botón «Pegar de Excel» |
| `04-dialogo-selector-tipo-losa.png` | Selector visual de tipos de losa (filtros + grid de iconos) | Click en la columna TIPO de una losa |
| `05-dialogo-doctor-dl.png` | Ventana «Doctor .DL» (diagnóstico de archivos .DL) | Botón «Diagnosticar» al abrir un .DL |
| `06-dialogo-captura-tecla.png` | Ventana de captura de combinación de teclas | Configuración → Atajos → editar un atajo |

## 11 · Acerca de — `11-acerca/`

| Archivo | Qué capturar | Cómo llegar |
|---|---|---|
| `01-acerca-de.png` | La pantalla «Acerca de» (créditos, alcance, copyright) | Modo «Acerca de» |

---

## Checklist de progreso

### 00 · Shell y temas
- [ ] `01-shell-tema-precision.png`
- [ ] `02-shell-tema-light.png`
- [ ] `03-shell-tema-dark.png`
- [ ] `04-barra-superior.png`
- [ ] `05-selector-modos.png`

### 01 · Explorador
- [ ] `01-explorador-vacio.png`
- [ ] `02-explorador-con-recientes.png`
- [ ] `03-card-proyecto-activo.png`

### 02 · Editor de losas
- [ ] `01-editor-vacio.png`
- [ ] `02-editor-con-losas.png`
- [ ] `03-toolbar-editor.png`
- [ ] `04-bulk-apply-panel.png`
- [ ] `05-panel-lateral-sistema.png`
- [ ] `06-columna-tipo-selector.png`

### 03 · Lienzo CAD
- [ ] `01-lienzo-vacio.png`
- [ ] `02-toolbar-flotante.png`
- [ ] `03-losas-dibujadas.png`
- [ ] `04-losa-seleccionada.png`
- [ ] `05-editor-flotante-losa.png`
- [ ] `06-chips-adyacencia.png`
- [ ] `07-marcas-acero.png`
- [ ] `08-dxf-importado.png`
- [ ] `09-panel-dxf.png`
- [ ] `10-pdf-importado.png`
- [ ] `11-panel-pdf.png`
- [ ] `12-pdf-modo-oscuro.png`
- [ ] `13-pdf-opacidad-reducida.png`
- [ ] `14-calibracion-pdf.png`
- [ ] `15-herramienta-mano.png`
- [ ] `16-auto-conectar-resultado.png`
- [ ] `17-pie-ayuda-interaccion.png`

### 04 · Flujo .DL / .TXT
- [ ] `01-editor-dl.png`
- [ ] `02-salida-txt-texto.png`
- [ ] `03-salida-txt-tabla.png`
- [ ] `04-aceros-placeholder.png` *(opcional)*

### 05 · Validación normativa
- [ ] `01-validacion-lista.png`
- [ ] `02-validacion-detalle.png`
- [ ] `03-validacion-indicadores.png`

### 06 · Búsqueda
- [ ] `01-busqueda-vacia.png`
- [ ] `02-busqueda-resultados.png`

### 07 · Reglamento
- [ ] `01-reglamento-lista.png`
- [ ] `02-reglamento-detalle.png`

### 08 · Plugins
- [ ] `01-plugins-lista.png`
- [ ] `02-plugins-vacio.png` *(opcional)*

### 09 · Configuración
- [ ] `01-config-datos-ingeniero.png`
- [ ] `02-config-apariencia.png`
- [ ] `03-config-atajos.png`

### 10 · Diálogos y ventanas
- [ ] `01-dialogo-atajos.png`
- [ ] `02-dialogo-personalizar-colores.png`
- [ ] `03-dialogo-pegar-excel.png`
- [ ] `04-dialogo-selector-tipo-losa.png`
- [ ] `05-dialogo-doctor-dl.png`
- [ ] `06-dialogo-captura-tecla.png`

### 11 · Acerca de
- [ ] `01-acerca-de.png`

---

Cuando tengas las capturas en sus carpetas, avisá para verificar los
nombres y hacer el commit + push de las imágenes a GitHub.
