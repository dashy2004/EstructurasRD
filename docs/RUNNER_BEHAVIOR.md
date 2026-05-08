# RUNNER_BEHAVIOR — Cómo invocar `Losas.exe` desde LosasPlus

**Estado:** Tarea A (validación del runner) — **completada**. Hallazgos definitivos. Pendiente: decisión del usuario sobre la estrategia a implementar antes de tocar `LosasRunner.cs`.

---

## TL;DR

`Losas.exe` (Visual Smalltalk Enterprise 3.1) es **completamente impermeable** a las técnicas estándar de automatización:

| Técnica                                     | ¿Funciona? | Por qué                                                    |
|---------------------------------------------|------------|-------------------------------------------------------------|
| Argumento CLI (`Losas.exe foo.DL`)          | ❌         | El binario lo ignora silenciosamente.                       |
| Pre-cargar `Losas.INI` con ruta del .DL     | ❌         | El INI sólo guarda el último directorio.                    |
| UIA `InvokePattern` / `ValuePattern`        | ❌         | Los controles exponen 0 patterns (todos son "Pane" custom). |
| Atajo `Ctrl+O` para abrir archivo           | ❌         | No registrado.                                              |
| Atajo `Alt+F` para abrir menú File          | ❌         | El menú es custom-drawn por Smalltalk, no estándar Win32.   |
| Drag-drop sobre la ventana                  | (no probado) | Costo alto, valor estimado bajo.                          |

Lo único que **sí funciona** es:
- Conocer el `BoundingRectangle` de cada control (UIA lo expone).
- Mover el mouse a esas coordenadas y simular clic con `mouse_event` / `SendInput` (Win32).
- Escribir texto con `System.Windows.Forms.SendKeys.SendWait`.

Esto fuerza una decisión arquitectónica.

---

## Experimentos realizados

### Experimento 1 — `Losas.exe <ruta.DL>` como argumento

Setup: copié `SISTEMA DEMO 27 LOSAS.DL` al directorio de Losas.exe como `test_runner.DL`, lancé `Losas.exe "test_runner.DL"` desde esa carpeta, esperé 8 s.

Resultado:
- Proceso vivo, sin salir, GUI abierta titulada `"Losas"`.
- **Ningún `.TXT`, `.OUT` ni `.RES` generado** en la carpeta.
- Stdout/stderr vacíos.
- Comportamiento idéntico al lanzamiento sin args (ver Experimento 2).

### Experimento 2 — `Losas.exe` sin argumentos (control)

Resultado: ventana `"Losas"` con apariencia idéntica al Experimento 1, sin estado precargado. **Confirma que el argumento del Experimento 1 fue silenciosamente descartado.**

### Experimento 3 — Enumeración del árbol UIAutomation

Lanzamos `Losas.exe` sin args, esperamos 3 s, recorrimos el árbol UIA con `[System.Windows.Automation.AutomationElement]::FromHandle($hwnd)`.

Resultado: árbol completamente accesible. **38 controles hijos** de la ventana raíz, con `AutomationId` numérico estable. Mapa relevante:

| AutomationId | Class    | Función                                               |
|--------------|----------|-------------------------------------------------------|
| `106`        | Edit     | Nombre del sistema                                    |
| `109`        | Edit     | f'c (kg/cm², default 210)                             |
| `112`        | Edit     | fy (kg/cm², default 4200)                             |
| `114`        | Button   | Camellado: Sí                                         |
| `115`        | Button   | Camellado: No                                         |
| `117`        | Button   | (sin name — probable selector de tipo de losa)        |
| `118`        | Button   | **INICIO**                                            |
| `120`–`132`  | Edit     | Por losa: No, Tipo, Lx, Ly, H, Rec, Wu                |
| `133`        | Button   | Añadir                                                |
| `134`        | Button   | Quitar                                                |
| `135`        | Button   | Modificar                                             |
| `136`        | Button   | **FIN**                                               |
| `137`        | ListBox  | Lista de losas agregadas                              |

Class de la ventana raíz: `BasicApplicationWindow` (clase custom de Visual Smalltalk).

### Experimento 4 — ¿Y los menús File / Help?

Visibles en captura de pantalla del programa, pero **no aparecen en el árbol UIA**. Smalltalk Enterprise dibuja sus menús con `vgui31w.sll` / `voflr31w.sll` en lugar del sistema de menús Win32, por lo que UIA no los ve.

### Experimento 5 — Patrones soportados por los controles

Probamos `GetSupportedPatterns()` en cada control y la ventana raíz:

```
Root (BasicApplicationWindow):
  - WindowPatternIdentifiers.Pattern
  - TransformPatternIdentifiers.Pattern

[106 Edit]    rect=938,651,150,25  patterns=[]
[109 Edit]    rect=1038,716,50,25  patterns=[]
[112 Edit]    rect=1038,746,50,25  patterns=[]
[118 Button]  rect=938,851,60,25   patterns=[]
[120 Edit]    rect=948,921,50,25   patterns=[]
[122 Edit]    rect=1018,921,50,25  patterns=[]
[133 Button]  rect=1548,921,70,25  patterns=[]
[136 Button]  rect=1548,1031,70,25 patterns=[]
[137 ListBox] rect=948,956,550,100 patterns=[]
```

**Hallazgo crítico:** todos los controles tienen `patterns=[]` (vacío). No implementan ni `InvokePattern` (clic programático) ni `ValuePattern` (set de texto). Sí exponen `BoundingRectangle` con coordenadas de pantalla precisas.

Intentar `el.GetCurrentPattern(InvokePattern.Pattern)` lanza `Unsupported Pattern`. Verificado contra Button 118 (INICIO) y Button 133 (Añadir).

### Experimento 6 — Form-driving via UIA programática

Intentamos llenar Edit 106/109/112 con `ValuePattern.SetValue(...)` y disparar Button 118 con `InvokePattern.Invoke()`. **Falló**: ningún Set ni Click se ejecutó (todos lanzaron `Unsupported Pattern`).

### Experimento 7 — Atajos de teclado (`Ctrl+O`, `Alt+F`)

Tras dar foco al window con `SetForegroundWindow`, enviamos:
- `SendKeys.SendWait("^o")` → ningún diálogo nuevo.
- `SendKeys.SendWait("%f")` → ningún diálogo nuevo, ningún menú abierto.

La cuenta de top-level windows no cambia. Concluye que **no hay atajos de teclado para File→Open** en la GUI de Smalltalk.

---

## Estrategias viables (ordenadas por costo creciente)

### Opción A — Drop the runner (LosasPlus = editor + exporter)

Eliminar `LosasRunner` y reposicionar LosasPlus como **editor + exportador `.DL`** sin invocar el motor. El usuario:
1. Edita el sistema en LosasPlus.
2. Hace clic en "Guardar .DL".
3. Abre `Losas.exe` manualmente, carga el .DL desde el menú File.
4. Ejecuta el cálculo manualmente.
5. Vuelve a LosasPlus a "Importar .TXT" para visualizar.

**Pros:** cero riesgo, cero código de automatización frágil, mantiene el wrapper simple.
**Contras:** rompe la promesa de "wrapper completo" del README. El usuario tiene 3 pasos manuales.
**Costo:** 0–4 h (mover botones, actualizar README, eliminar runner, ajustar VM).

### Opción B — Semi-manual con `FileSystemWatcher`

LosasPlus **abre Losas.exe** y muestra una guía paso-a-paso al usuario ("Ahora cargá `output.DL` en File→Abrir, y ejecutá"). En paralelo, monta un `FileSystemWatcher` sobre el directorio esperando el `.TXT`. Cuando aparece, lo parsea y lo muestra automáticamente.

**Pros:** tan automático como permite Smalltalk; un solo paso manual del usuario.
**Contras:** UX confusa la primera vez. El watcher puede dispararse con archivos viejos.
**Costo:** ~1 día (Watcher + UX + tests).

### Opción C — Coordinate clicks + SendKeys (form-driving)

LosasPlus **drivea la GUI con mouse y teclado**:
1. Lanza Losas.exe.
2. Por cada Edit, mueve el mouse al centro del `BoundingRectangle`, hace clic, `Ctrl+A`, `Delete`, escribe el valor con SendKeys.
3. Por cada Button, mueve el mouse y hace clic.
4. Espera que aparezca el dialog de "Save As" tras FIN, lo maneja con SendKeys (path + Enter).
5. Lee el `.TXT` resultante.

**Pros:** automatización end-to-end. Cero pasos manuales del usuario.
**Contras:**
- **Frágil con DPI scaling** (coordenadas absolutas).
- **Roba el cursor del usuario** durante la ejecución (no se puede usar la PC mientras corre).
- **Sensible a focus**: si el usuario hace clic en otra app durante el run, los SendKeys se desvían.
- **Dependencia oculta de la versión de Losas.exe**: si cambian los AutomationIds o coordenadas, todo se rompe sin error claro.
- Aún hay **dos preguntas abiertas** que requieren validación adicional:
  1. ¿Cómo se ingresan las **adyacencias I-J** en X e Y? El árbol UIA no las muestra como controles. ¿Aparecen tras INICIO? ¿Tras Añadir? ¿En un sub-formulario?
  2. ¿Qué dialog aparece al clic en **FIN**? ¿Estándar Windows "Save As" (manejable) o custom Smalltalk (también requiere coordenadas)?

**Costo:** 3–5 días sólo para el runner + tests + manejo de errores.

### Opción D — Híbrido: editor → .DL + visor del .TXT, con auto-launch

Compromiso entre A y B:
1. LosasPlus genera el .DL.
2. Botón "Ejecutar" abre Losas.exe automáticamente.
3. El usuario manualmente carga el .DL y ejecuta.
4. Al terminar, presiona "Importar resultado" en LosasPlus que abre un FileDialog en el último directorio, el usuario selecciona el `.TXT` y se parsea.

**Pros:** previsible, sin magia, sin riesgo de cursor secuestrado.
**Contras:** dos clics manuales del usuario (cargar .DL en Losas, importar .TXT).
**Costo:** ~0.5 día.

---

## Recomendación

**Opción D (híbrido)** o **Opción B (semi-manual con Watcher)**.

Razones:
1. Las opciones C (coordenadas) requieren resolver dos preguntas abiertas que sólo se pueden contestar con una sesión interactiva con el usuario observando la pantalla. Pre-implementar sería caro y frágil.
2. Opción A subutiliza el motor — el usuario quiere ver los resultados parseados en LosasPlus, eso pierde valor si nunca se ejecuta el motor.
3. La diferencia entre B y D es UX, no técnica. Si el `FileSystemWatcher` se prueba estable, B gana. Si no, D es lo más previsible.

---

## Decisión pendiente

Antes de tocar `LosasRunner.cs`, necesito tu input:

**1. ¿Qué opción elegimos? A / B / C / D.**

**2. Si C (form-driving), abrir `Losas.exe` y reportar:**
   a. Qué pasa al hacer clic en INICIO (¿se habilitan otros campos? ¿aparece otra ventana?).
   b. Cómo se ingresan las adyacencias I-J en X e Y.
   c. Qué dialog aparece al clic en FIN — `Save As` estándar o algo custom de Smalltalk.
   d. Dónde se guarda el `.TXT` y con qué nombre por defecto.

**3. Si B/D, confirmar UX:**
   - ¿El usuario tolera 1-2 clics manuales por cada cálculo, a cambio de previsibilidad y poder seguir trabajando en otras cosas mientras Losas.exe corre?

Mi sugerencia: **Opción D**. Es el sweet-spot. Implementación clara, baja superficie de error, UX honesta ("LosasPlus es un editor + visor, el motor sigue siendo el original de F. Perdomo").

---

## Apéndice — Estado actual del código

`LosasRunner.cs` actual:

```csharp
var psi = new ProcessStartInfo {
    FileName = ExePath,
    Arguments = $"\"{dlPath}\"",     // ← ignorado por Losas.exe
    ...
};
await p.WaitForExitAsync(...);       // ← nunca exit
```

Está roto en su premisa. Cualquiera de las opciones A/B/C/D requiere reescribirlo. No tiene sentido modificarlo sin haber elegido la opción.

Las llamadas actuales a `LosasRunner.RunAsync` desde `MainViewModel.cs` también tendrán que ajustarse a la nueva firma (probablemente `RunAsync(Sistema)` o `OpenLosasAndWatchAsync(string dlPath, string expectedTxtPath)`).
