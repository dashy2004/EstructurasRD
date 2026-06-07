# Hand-off → Antigravity — Quitar barra Losas.exe + UX de Niveles/Elevación

> Reportado por el usuario. Todo UI (tu lane). El motor (Claude) ya dejó la elevación
> reflejada en el 3D (`EscenaEdificio`, commit `0b7838e`). Rama `engine/columnas-diseno`.

## Tarea 1 — Quitar la barra de Losas.exe (decisión del usuario: «quitar la barra, conservar import»)

En `src/MainWindow.axaml` (región "TOP ACTION BAR", ~línea 211) **remover**:
- El display de path **`Losas.exe:`** (~268) y «Examinar Losas.exe» (~229) / «Lanzar Losas.exe» (~230).
- El botón **«▶ Lanzar motor»** (~251) — su tooltip dice "Abre Losas.exe".

**CONSERVAR** (no tocar): el flujo de **importar .DL/.TXT** (respaldo silencioso) y mis botones del
motor nativo: «Calcular carga última (Wu) desde geometría» (~233) y «Calcular losas con el motor
(FEM, sin Losas.exe)» (~236, `CalcularConMotorCommand`). Esos quedan como camino principal.
⛔ No borrar el binario ni el import — sólo los controles de *lanzar/examinar* Losas.exe de la barra.

## Tarea 2 — Recuadro de elevación al lado del sistema

Hoy el `TextBox` de `Sistema.Elevacion` está en `CadView.axaml:300` (panel «Elemento Activo»).
El usuario lo quiere **junto al sistema** (en el selector/lista de sistemas, no en el panel CAD).
Mover/duplicar ese `TextBox Text="{Binding Sistema.Elevacion}"` allí donde se elige el sistema.

## Tarea 3 — Poder generar varios NIVELES (no sistemas)

**Causa raíz:** no hay un botón **«+ Agregar nivel»**. El modelo soporta múltiples `Edificio.Niveles`
(cada `Nivel` con su `Cota`), pero la UI sólo los crea por replicación (`MainWindow.axaml.cs:570`).
- Agregar un botón **«+ Nivel»** que haga `EdificioActivo.Niveles.Add(new Nivel { Cota = ... })`.
- *(Si querés un comando en el VM para esto —`AgregarNivelCommand`— lo expongo yo en `src.Core`/VM;
  pedímelo y vos hacés el botón.)*

## Aclaración importante (no es bug): elevación en la planta

En la **vista en planta (2D top-down)** la elevación NO separa las huellas (Z está fuera del plano):
dos sistemas a distinta cota se ven solapados igual. Eso es geometría, no un fallo. La separación por
cota se ve en **3D** (ya funciona) o en la **vista de sección**. Para trabajar "por piso", lo correcto
es que la planta tenga un **selector de Nivel** (ver un nivel a la vez), no separar por elevación.
Sugerencia: agregar un ComboBox de Nivel en `Planta2DEditorView` que filtre `EditorCanvas.Nivel`.

## Notas
- Mantené verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (929/929 hoy).
- `src.Core` es lane de Claude — si necesitás un comando/propiedad de motor, pedilo.
- ⛔ `Losas.exe`: sólo se quita de la barra; el import .DL/.TXT se conserva.
