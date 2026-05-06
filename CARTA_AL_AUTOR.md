# Carta al autor del motor original (F. Perdomo)

> Borrador del correo a enviar a `fa.perdomo@gmail.com` junto con el ZIP del proyecto.
> Está pensado para copiarse y pegarse directo en Gmail u otro cliente.
> Ajustá lo que quieras antes de mandar.

---

**Para:** fa.perdomo@gmail.com
**Asunto:** Sugerencia / aporte: capa moderna de UI sobre su programa Losas (Pieper-Martens)
**Adjunto:** `LosasPlus_v1.0_codigo_fuente.zip`

---

Estimado Ing. Francisco Perdomo:

Soy **Emil Guillen De la Cruz**, ingeniero civil de República Dominicana. Le escribo con
máxima consideración profesional para presentarle un trabajo que hice sobre su programa
**Losas** (v5.x, 2011/2013) y que me gustaría poner a su disposición.

## Contexto

Uso su programa de manera regular para el diseño de losas continuas en proyectos en
Santo Domingo. El método de Pieper-Martens que usted adaptó y la implementación
del programa son sólidos y resuelven con precisión casos que de otra manera requerirían
elementos finitos. Tengo claro que el motor de cálculo es obra suya y respeto íntegramente
su propiedad intelectual.

Como el programa fue construido en Visual Smalltalk Enterprise 3.1, su interfaz
gráfica responde a los paradigmas de los 90s. Eso me llevó a desarrollar una
**capa externa moderna** que envuelve su binario sin modificarlo, llamada
**LosasPlus**, escrita en C# .NET 8 + WPF. Es importante destacar que:

- **No modifica `Losas.exe`** ni redistribuye el binario.
- **Genera el archivo `.DL`** byte-compatible con el formato documentado en su
  `Losas.hlp` (verificado por roundtrip parser↔writer).
- **Lanza `Losas.exe`** como proceso externo, dejando que la GUI nativa
  ejecute el cálculo.
- **Importa el `.TXT` de salida** para visualizarlo y exportarlo enriquecido.
- Reconoce y atribuye su autoría en la pestaña **Acerca de** y en el README.

Le envío adjunto el código fuente completo, libre de cualquier reclamación
económica. Queda a su entera disposición para que decida qué hacer con él
(integrarlo, ignorarlo, basarse en él para una versión propia, etc.).

## Qué agrega LosasPlus al flujo

Esta es la lista de capas que añadí, todas en el lado del wrapper, sin tocar el motor:

1. **Editor estructurado**: grilla editable con validación in-place (Lx/Ly/Espesor/Carga >
   0; Rec < Espesor; warning ámbar si Ly/Lx queda fuera del rango Pieper-Martens [0.5, 2.0]).
2. **Catálogo visual de tipos**: 24 SVG embebidos para los códigos del catálogo
   Pieper-Martens (10/13/14/21-24/31-34/40-44/50-54/60/63-64/71-72), con tooltip de
   descripción. Click en un icono aplica el tipo a la fila seleccionada.
3. **Esquema 2D topológico**: layout BFS desde adyacencias I-J (X/Y), pan + zoom
   con rueda del mouse, etiquetas de momentos parseados, modo conexión por click
   (definir adyacencias gráficamente entre dos losas).
4. **Multi-sistema**: un proyecto puede contener múltiples sistemas (cada uno
   en su propio `.DL` nombrable), con manifest `proyecto.lpx.json` que guarda
   metadata (autor, código de obra, fecha, etc.).
5. **Pegar de Excel**: tabla Excel/Calc al portapapeles → losas creadas con
   detección automática de columnas (`Lx, Ly, Espesor, Carga, Tipo, Rec`),
   tolera coma decimal española y unidades en cm.
6. **Visor del `.TXT`** con resaltado por línea (títulos, separadores, headers,
   datos numéricos en colores diferenciados).
7. **Exportación a Excel** (.xlsx) con 6 hojas: Resumen / Losas / Apoyos / Espejo del
   `.TXT` (preserva el formato monoespaciado original) / Esquema (PNG embebido
   del Canvas) / Combinaciones (lectura de `Combinaciones.DZP/.CEZ`).
   También CSV separado por `;`.
8. **Exportación de `.DL`** byte-compatible (verificada por roundtrip de 27 losas +
   22 apoyos contra el sample real).
9. **Panel del Reglamento Dominicano**: R-001 (Sísmico), R-008 (Sanitario), R-033
   (Hormigón Armado, con su TÍTULO VI dedicado a losas) y ACI 318-19, con botón
   "Abrir PDF" para acceder al documento original.
10. **Sandbox de plugins** en C# Script (Roslyn) con sistema de confianza por
    SHA-256 — los usuarios pueden extender la app con scripts `.csx` sin recompilar.
11. **Themes** (Precision/Light/Dark) + panel de personalización de colores con
    sliders RGB+HEX persistentes.
12. **Suite de tests** con 126 tests xUnit cubriendo parser/writer del `.DL` y
    `.TXT`, layout solver, validaciones, catálogo, exportación, plugins.

## Lo que descubrí experimentalmente sobre `Losas.exe`

Documenté en `docs/RUNNER_BEHAVIOR.md` que:

- El binario ignora argumentos de línea de comandos.
- Sus controles no exponen patrones UI Automation programables, así que la
  automatización completa de la GUI (sin intervención del usuario) no es viable
  por API estándar.
- Por eso LosasPlus opta por un flujo **semi-manual honesto**: lanza el motor,
  el usuario carga manualmente el `.DL` desde el menú File del programa y ejecuta,
  y al cerrar el motor LosasPlus importa el `.TXT`.

## Sobre la propiedad intelectual

El motor `Losas.exe` y su algoritmo Pieper-Martens son íntegramente obra suya.
LosasPlus es solo una capa externa. El código fuente del wrapper es mío
(MIT-style si así lo prefiere) y se lo paso como aporte.

Si decide usar este código, integrarlo en una versión nueva de Losas, o
inspirarse en algo, me daría mucho gusto. Si prefiere desarrollar una versión
moderna por su cuenta, este envío le puede servir como referencia o discutirlo.

Si esta gestión le resulta inoportuna o no le interesa, le pido por favor que
ignore el correo y no se sienta obligado a responder. No habrá distribución
pública del wrapper sin su autorización explícita.

## Cómo abrir el código

El ZIP contiene el proyecto completo (`LosasPlus/`), el `README.md` con el
setup, y la lista de archivos relevantes. Requiere .NET 8 SDK para compilar.
Si no usa C# pero le interesa ver la lógica, los puntos clave son:

- `src/Services/DLFileService.cs` — parser/writer del `.DL` (byte-compatible).
- `src/Services/TxtParser.cs` — parser determinístico del `.TXT`.
- `src/Models/Sistema.cs` — modelo de losa, sistema, proyecto.
- `docs/RUNNER_BEHAVIOR.md` — el reporte de cómo intenté automatizar `Losas.exe`.

Estoy a su entera disposición para cualquier pregunta, cambio o colaboración.

Muy cordialmente,

**Emil Guillen De la Cruz**
Ingeniero Civil — Santo Domingo, República Dominicana
Email: emilgdc@gmail.com
GitHub: https://github.com/dashy2004
YouTube: https://www.youtube.com/@emilguillen
Instagram: https://www.instagram.com/emilguillendelacruz
