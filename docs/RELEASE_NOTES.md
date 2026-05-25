# Notas de Lanzamiento — Suite EstructurasRD

## Releases publicados

| Versión | Marca | Documento detallado | Resumen |
|---|---|---|---|
| **v0.8.0** | EstructurasRD | [`RELEASE_NOTES_v0.8.0.md`](RELEASE_NOTES_v0.8.0.md) | Liga B (UI Optimization) + Liga C (Planta Estructural editora) + Fase 3D-II MVP. 853 tests verdes. Módulos columnas/vigas/losas/zapatas **en prueba**. Adopción futura del nuevo Código de Construcción dominicano planificada. |
| v1.3.0 | LosasPlus legacy | (este documento — sección histórica) | Epics v1.2 & v1.3: ergonomía interactiva, PDF Underlay y automatización geométrica de losas. 584 tests verdes. |

> Para el **release activo** consulte
> [`RELEASE_NOTES_v0.8.0.md`](RELEASE_NOTES_v0.8.0.md). El contenido a
> continuación documenta el último release histórico bajo la marca
> *LosasPlus* (pre-rebrand).

---

# (Histórico) Notas de Lanzamiento — LosasPlus v1.3.0

### Epics v1.2 & v1.3 · Ergonomía Interactiva, PDF Underlay y Automatización Geométrica

Esta versión consolida dos epics completos de trabajo sobre el lienzo CAD
de LosasPlus: la **representación visual e interacción** del entorno
gráfico (Epic v1.2) y la **automatización geométrica** del diseño de losas
(Epic v1.3). El editor deja de ser una grilla de rectángulos vacíos para
convertirse en un lienzo CAD interactivo con underlay de planos PDF/DXF,
calibración por referencia y un motor que alinea y conecta losas
automáticamente.

> Todas las funcionalidades descritas en este documento están
> **implementadas y commiteadas** en `main`. La suite de **584 pruebas
> unitarias** está en verde y el build compila con **0 warnings**.

---

## 1. Representación visual e interacción del lienzo CAD

- **Representación visual de losas.** Cada losa se dibuja con un patrón
  interior según su condición estructural — franjas horizontales (1D-H),
  verticales (1D-V) o cuadrícula reticular (2D) — más un rótulo interno de
  tres líneas (Id, Lx × Ly, Tipo). Las adyacencias declaradas se marcan
  con un ícono de acero adicional.
- **Edición in-canvas.** El doble clic sobre una losa abre un editor
  flotante superpuesto (Lx, Ly y Tipo); al confirmar se persiste el cambio
  con un único snapshot de Undo.
- **Toolbar flotante de herramientas.** Herramienta **Mano** (paneo
  dedicado del lienzo, sin tocar losas), toggle **Snap** (imán de
  alineación a la grilla y a bordes vecinos) y toggle **Mover Conectadas**
  (al arrastrar una losa, un recorrido BFS sobre las adyacencias arrastra
  toda la componente conexa con el mismo vector).

## 2. PDF Underlay y calibración

- **Importación de planos PDF.** Un plano arquitectónico en PDF puede
  cargarse como capa de fondo del lienzo. La primera página se rasteriza
  de forma **asíncrona** (librería Docnet.Core) para no congelar la
  interfaz, con un panel lateral de ajuste espacial (escala, offset X/Y,
  Encuadrar).
- **Calibración interactiva por dos puntos.** El usuario marca dos puntos
  de referencia sobre el PDF e indica la distancia real entre ellos; el
  sistema aplica una **homotecia afín** que recalcula la escala del plano
  conservando el primer punto fijo. Resuelve los PDFs que llegan a escala
  arbitraria.

## 3. Robustez del importador de planos

- **PDF — defensa en profundidad.** Fallback de dimensiones ANSI D para
  PDFs sin marca `/MediaBox`; corrección del crash de Docnet en planos
  *landscape*; renderizado por **factor de escala** que resuelve los
  buffers de render vacíos en PDFs con transformaciones de impresión
  complejas. Ningún PDF problemático colapsa la aplicación.
- **Control de opacidad y Modo Oscuro CAD.** Slider de opacidad del
  underlay y un toggle de inversión cromática que transforma los planos de
  fondo blanco en fondo negro con líneas blancas, para reducir la fatiga
  visual en sesiones largas.
- **DXF — soporte de bloques `INSERT`.** El importador DXF ahora expande
  recursivamente las referencias de bloque (`INSERT`). Los planos de
  AutoCAD / Revit que envuelven toda su geometría en bloques anidados
  ahora se renderizan completos en lugar de aparecer en blanco.

## 4. Automatización geométrica e inteligencia CAD

- **Rasterización dinámica del PDF según el zoom.** El underlay se
  re-rasteriza en alta resolución cuando el usuario hace zoom-in
  significativo, de modo que los textos del plano no se pixelan al
  acercarse. El zoom-out no dispara reprocesamiento.
- **Eliminación de planos de referencia.** Botones 🗑 para quitar el DXF o
  el PDF del lienzo sin afectar el modelo de losas.
- **Renombrado de sistemas en caliente.** Un campo editable en la barra
  superior permite renombrar el sistema activo y el cambio se refleja
  instantáneamente en el selector.
- **Motor de Auto-Alineación y Auto-Conexión.** El botón **🤖
  Auto-Conectar** ejecuta un motor de geometría analítica que (1) cierra
  las holguras pequeñas entre losas vecinas para que sus aristas calcen
  exacto y (2) genera automáticamente los bordes de continuidad de acero
  (`BordesX` / `BordesY`) entre las losas que quedan en contacto físico —
  todo bajo un único snapshot de Undo.

## 5. MemoriaPlus

- **Eliminación de niveles.** La pestaña Niveles incorpora un botón 🗑 para
  eliminar el nivel (sistema) seleccionado del proyecto, con una
  confirmación que advierte la pérdida de las losas y los datos importados
  de ese nivel.

---

## Resumen de cambios

| Componente / Módulo | Tipo de cambio | Impacto en el flujo de trabajo |
|---|---|---|
| Lienzo CAD — representación e interacción | Nueva feature | Edición visual directa de losas con patrones, rótulos y toolbar |
| PDF Underlay + calibración | Nueva feature | Plano arquitectónico de referencia, escalable por dos puntos |
| Importador PDF / DXF | Estabilización / robustez | Soporta PDFs complejos y DXFs con bloques sin colapsar |
| Motor Auto-Alineación / Auto-Conexión | Nueva feature | Alinea y conecta losas vecinas en un solo gesto |
| Nitidez dinámica del PDF | Optimización gráfica | El underlay no se pixela al hacer zoom-in extremo |
| MemoriaPlus — eliminar niveles | Bug fix / feature | Gestión completa de niveles del proyecto |

## Calidad y arquitectura

- **584 pruebas unitarias** en verde.
- Build con **0 warnings**.
- El núcleo de dominio `src.Core` se mantiene **agnóstico de WPF**: la
  lógica geométrica y de cálculo es pura y testeable; lo que produce
  `BitmapSource` u otros tipos gráficos vive en la capa de presentación.

---

## Roadmap — próximas versiones

El ecosistema continúa su evolución. Las siguientes funcionalidades están
**en planificación** (todavía no implementadas):

- Salida de la memoria de cálculo en formato **`.docx`**.
- Importación de resultados de **`Losas.exe`** con colocación automática
  de las etiquetas analíticas sobre cada elemento del lienzo.
- **Refuerzos adicionales** (bastones y barras de acero) leídos e
  incrustados directamente desde tablas de Excel.
- **Exportación vectorial** de los diagramas analíticos (momentos,
  deformaciones, despieces).
- **Módulo Muros v0.9.0** — elementos verticales paramétricos (espesor,
  altura, longitud), con dibujo y clasificación en el esquema CAD y
  codificación cromática por espesor.
- Correcciones de ergonomía: reinicio de la vista de planta y refresh del
  editor.
- **MemoriaPlus** — folder de fotos unificado para que el generador de
  reportes inyecte automáticamente los esquemas en las secciones del
  informe técnico.

---

*Ecosistema LosasPlus — capa moderna de UI, edición y exportación sobre el
motor de cálculo Losas de Francisco Eludino Perdomo.*
