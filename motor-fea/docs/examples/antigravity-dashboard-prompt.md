# Prompt para Antigravity — Dashboard EstructurasRD

Pega el bloque de abajo directamente en Antigravity para generar la interfaz.
Antes de generar, asegúrate de que el servidor esté corriendo:

```bash
motor-fea --serve --host 0.0.0.0 --port 8000
```

---

## PROMPT

Crea una web app de una sola página (SPA) en español llamada
"EstructurasRD – Panel de Análisis FEA".

STACK: React + Tailwind CSS. La app consume una REST API local
cuya URL base el usuario configura en un campo de texto
(por defecto: http://localhost:8000).

---

### SECCIÓN 1 – CABECERA

- Logo texto "EstructurasRD" a la izquierda (color #1e293b sobre fondo oscuro).
- Campo de texto "URL del servidor" con valor por defecto `http://localhost:8000`
  y botón "Conectar" que hace `GET /escena` y muestra un indicador verde ✓ / rojo ✗.

---

### SECCIÓN 2 – CARGA DE EDIFICIO

- Zona de drag-and-drop para subir un archivo `.json` (edificio autorado).
- Al soltar o seleccionar el archivo, hace `POST /visor-edificio` con el
  contenido JSON en el body y muestra un spinner de carga.
- Si no hay archivo cargado, usa `GET /escena` (modelo de ejemplo del servidor).

---

### SECCIÓN 3 – VISOR 3D

- `<iframe>` que apunta a `{URL_BASE}/` (el visor Three.js ya existe en el servidor).
- Ocupa el 60 % del alto visible de la pantalla.
- Tres botones sobre el iframe: **Sin deformar** | **Deformada** | **Modo 1**
  que envían mensajes `postMessage` al iframe para cambiar el estado de visualización.

---

### SECCIÓN 4 – RESULTADOS DE DISEÑO

Llama `GET /diseno?fc=21&fy=420` al conectar o al subir un edificio nuevo.

Muestra:
- Dos campos editables: **f'c (MPa)** y **fy (MPa)** con botón "Recalcular"
  que repite el GET con los valores nuevos.
- Tabla con columnas: **Elem · Tipo · As (cm²) · ρ (%) · Estado**
  donde Estado es ✓ OK si ρ está entre 1 % y 4 %, o ⚠ Revisar si está fuera.

---

### SECCIÓN 5 – TABLA DE ELEMENTOS

Llama `GET /escena` al conectar.

- Tabla con columnas: **ID · Tipo · Nodo i · Nodo j · L (m)**
- Filtro por tipo (columna / viga / todos) con botones tipo chip.
- Al hacer clic en una fila, resalta ese elemento enviando al iframe:
  `postMessage({ tipo: "seleccionar", id: <elem_id> }, "*")`.

---

### SECCIÓN 6 – DIAGRAMA DE ESFUERZOS

- Dropdown con los IDs de elementos obtenidos de `GET /escena`.
- Al seleccionar un elemento, llama `GET /esfuerzos?n=11` y muestra
  con Recharts o Chart.js:
  - Diagrama de cortante **V** (barras horizontales, color azul).
  - Diagrama de momento **M** (barras horizontales, color naranja).

---

### DISEÑO VISUAL

| Propiedad | Valor |
|---|---|
| Fondo de página | `#0f172a` |
| Fondo de cards | `#1e293b` |
| Borde sutil | `#334155` |
| Texto principal | `#f8fafc` |
| Acento | `#3b82f6` |
| Alerta | `#f59e0b` |

Layout desktop (≥ 1024 px): dos columnas.
- Columna izquierda (40 %): secciones 2, 4, 5 y 6.
- Columna derecha (60 %): sección 3 (iframe del visor).

Layout móvil: stack vertical en el orden 1 → 3 → 2 → 4 → 5 → 6.

Sin dependencias externas fuera de: React, Tailwind CSS y Recharts.

---

## ENDPOINTS DE REFERENCIA

| Método | Ruta | Uso en el dashboard |
|---|---|---|
| GET | `/escena` | Geometría inicial y tabla de elementos |
| GET | `/resultados` | Deformada y modos (los consume el iframe) |
| GET | `/diseno?fc=&fy=` | Tabla de diseño ACI 318 |
| GET | `/esfuerzos?n=11` | Diagramas V y M |
| POST | `/visor-edificio` | Carga edificio JSON autorado |

---

## NOTA CORS

Si Antigravity sirve el dashboard desde un origen distinto al servidor
(puerto diferente o dominio diferente), el navegador bloqueará las llamadas
a la API por política CORS.

Solución: pídele al asistente de Claude Code que lo habilite con:

> "agrega CORS al servidor FastAPI del motor FEA"

Eso agrega una línea al `servidor.py` que permite llamadas desde cualquier origen.
