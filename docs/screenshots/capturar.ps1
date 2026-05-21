#Requires -Version 5
<#
================================================================================
  capturar.ps1  -  Asistente de capturas de pantalla de LosasPlus
================================================================================
  Recorre las 54 capturas de la guia (ver README.md de esta carpeta) en orden.
  Para cada una te dice que preparar; vos navegas LosasPlus a esa pantalla y
  presionas el hotkey de captura. El script fotografia la ventana de LosasPlus
  que este al frente y guarda el PNG con el nombre exacto en la subcarpeta
  correcta.

  USO
    1. Compila y abri LosasPlus.exe  (dotnet run --project src).
    2. Abri PowerShell en esta carpeta y ejecuta:   .\capturar.ps1
       (si Windows bloquea el script:  powershell -ExecutionPolicy Bypass -File .\capturar.ps1)
    3. El script imprime que pantalla preparar. Navegala en LosasPlus.
    4. Con LosasPlus (o su dialogo) al frente, presiona el hotkey de captura.
    5. Repeti hasta completar las 54.

  HOTKEYS  (globales: funcionan con LosasPlus al frente, sin tocar la consola)
    Ctrl + Shift + F12   ->  Capturar la pantalla actual
    Ctrl + Shift + F11   ->  Saltar esta captura (la haces despues)
    Ctrl + Shift + F10   ->  Terminar y mostrar el resumen

  NOTAS
    - Solo captura si la ventana al frente pertenece al proceso LosasPlus;
      si no, ignora la pulsacion (asi no fotografia la consola por error).
    - Si re-ejecutas el script, retoma desde la primera captura que falte.
    - Los PNG se guardan en docs/screenshots/<carpeta>/<archivo>.
================================================================================
#>

# --- Tipos Win32 (idempotente: no recompilar si ya existen) ---
if (-not ('CapWin' -as [type]))
{
    Add-Type @"
using System;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)]
public struct CapRect { public int Left; public int Top; public int Right; public int Bottom; }
public static class CapWin
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out CapRect r);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out CapRect r, int size);
}
"@
}

Add-Type -AssemblyName System.Drawing

[CapWin]::SetProcessDPIAware() | Out-Null

# --- Lista ordenada de las 54 capturas: carpeta, archivo, que preparar ---
$targets = @(
    @{ F = '00-shell-temas';  A = '01-shell-tema-precision.png';     D = 'Ventana completa - modo Explorador - tema Precision (de fabrica)' }
    @{ F = '00-shell-temas';  A = '02-shell-tema-light.png';         D = 'Misma vista - boton Tema -> ciclar a Light' }
    @{ F = '00-shell-temas';  A = '03-shell-tema-dark.png';          D = 'Misma vista - boton Tema -> ciclar a Dark' }
    @{ F = '00-shell-temas';  A = '04-barra-superior.png';           D = 'La barra superior completa (selector SISTEMA, rename, Tema, Apariencia)' }
    @{ F = '00-shell-temas';  A = '05-selector-modos.png';           D = 'El selector con los 12 botones de modo' }

    @{ F = '01-explorador';   A = '01-explorador-vacio.png';         D = 'Modo Explorador - sin proyectos recientes' }
    @{ F = '01-explorador';   A = '02-explorador-con-recientes.png'; D = 'Modo Explorador - con la tabla de recientes poblada' }
    @{ F = '01-explorador';   A = '03-card-proyecto-activo.png';     D = 'La tarjeta Proyecto activo (nombre, autor, codigo de obra)' }

    @{ F = '02-editor-losas'; A = '01-editor-vacio.png';             D = 'Modo Editor - sistema sin losas' }
    @{ F = '02-editor-losas'; A = '02-editor-con-losas.png';         D = 'Modo Editor - DataGrid de losas poblado' }
    @{ F = '02-editor-losas'; A = '03-toolbar-editor.png';           D = 'La toolbar superior del Editor (+Losa, Pegar Excel, Undo/Redo...)' }
    @{ F = '02-editor-losas'; A = '04-bulk-apply-panel.png';         D = 'El panel de aplicacion masiva (selecciona 2+ losas)' }
    @{ F = '02-editor-losas'; A = '05-panel-lateral-sistema.png';    D = 'El panel lateral derecho (sistema, FC/FY/Adicionales)' }
    @{ F = '02-editor-losas'; A = '06-columna-tipo-selector.png';    D = 'La columna TIPO del grid con su selector' }

    @{ F = '03-lienzo-cad';   A = '01-lienzo-vacio.png';             D = 'Modo Lienzo CAD - lienzo vacio con la grilla' }
    @{ F = '03-lienzo-cad';   A = '02-toolbar-flotante.png';         D = 'La toolbar flotante (Puntero/Dibujar/Mano/Auto-Conectar/Snap/Conectadas)' }
    @{ F = '03-lienzo-cad';   A = '03-losas-dibujadas.png';          D = 'Varias losas dibujadas con patron interior y rotulo' }
    @{ F = '03-lienzo-cad';   A = '04-losa-seleccionada.png';        D = 'Una losa seleccionada con sus tiradores' }
    @{ F = '03-lienzo-cad';   A = '05-editor-flotante-losa.png';     D = 'El editor flotante in-canvas (doble clic en una losa)' }
    @{ F = '03-lienzo-cad';   A = '06-chips-adyacencia.png';         D = 'Los chips + de adyacencia entre losas vecinas' }
    @{ F = '03-lienzo-cad';   A = '07-marcas-acero.png';             D = 'Losas conectadas mostrando las marcas de acero' }
    @{ F = '03-lienzo-cad';   A = '08-dxf-importado.png';            D = 'Un plano DXF importado de fondo' }
    @{ F = '03-lienzo-cad';   A = '09-panel-dxf.png';                D = 'El panel lateral PLANO IMPORTADO + AJUSTE ESPACIAL' }
    @{ F = '03-lienzo-cad';   A = '10-pdf-importado.png';            D = 'Un PDF importado como underlay' }
    @{ F = '03-lienzo-cad';   A = '11-panel-pdf.png';                D = 'El panel PDF IMPORTADO + AJUSTE ESPACIAL PDF' }
    @{ F = '03-lienzo-cad';   A = '12-pdf-modo-oscuro.png';          D = 'El PDF con Modo Oscuro CAD activado' }
    @{ F = '03-lienzo-cad';   A = '13-pdf-opacidad-reducida.png';    D = 'El PDF atenuado con el slider de opacidad bajo' }
    @{ F = '03-lienzo-cad';   A = '14-calibracion-pdf.png';          D = 'El editor flotante de calibracion del PDF' }
    @{ F = '03-lienzo-cad';   A = '15-herramienta-mano.png';         D = 'El lienzo con la herramienta Mano activa' }
    @{ F = '03-lienzo-cad';   A = '16-auto-conectar-resultado.png';  D = 'El sistema tras Auto-Conectar (alineado + conexiones)' }
    @{ F = '03-lienzo-cad';   A = '17-pie-ayuda-interaccion.png';    D = 'El pie con la ayuda de interaccion del lienzo' }

    @{ F = '04-flujo-dl-txt'; A = '01-editor-dl.png';                D = 'Modo Editor .DL' }
    @{ F = '04-flujo-dl-txt'; A = '02-salida-txt-texto.png';         D = 'Modo Salida .TXT - pestana Texto' }
    @{ F = '04-flujo-dl-txt'; A = '03-salida-txt-tabla.png';         D = 'Modo Salida .TXT - pestana Tabla (editable)' }
    @{ F = '04-flujo-dl-txt'; A = '04-aceros-placeholder.png';       D = 'Modo Aceros - pantalla Proximamente (OPCIONAL)' }

    @{ F = '05-validacion';   A = '01-validacion-lista.png';         D = 'Modo Validacion - la lista de validaciones' }
    @{ F = '05-validacion';   A = '02-validacion-detalle.png';       D = 'El detalle de una violacion o advertencia' }
    @{ F = '05-validacion';   A = '03-validacion-indicadores.png';   D = 'Los indicadores de severidad (verde/naranja/rojo)' }

    @{ F = '06-busqueda';     A = '01-busqueda-vacia.png';           D = 'Modo Busqueda - sin consulta' }
    @{ F = '06-busqueda';     A = '02-busqueda-resultados.png';      D = 'Modo Busqueda - con resultados' }

    @{ F = '07-reglamento';   A = '01-reglamento-lista.png';         D = 'Modo Reglamento - el listado de normas' }
    @{ F = '07-reglamento';   A = '02-reglamento-detalle.png';       D = 'El detalle de una norma seleccionada' }

    @{ F = '08-plugins';      A = '01-plugins-lista.png';            D = 'Modo Plugins - tarjetas de plugins cargados' }
    @{ F = '08-plugins';      A = '02-plugins-vacio.png';            D = 'Modo Plugins - sin plugins (OPCIONAL)' }

    @{ F = '09-configuracion';A = '01-config-datos-ingeniero.png';   D = 'Configuracion - pestana Datos del Ingeniero' }
    @{ F = '09-configuracion';A = '02-config-apariencia.png';        D = 'Configuracion - pestana Apariencia' }
    @{ F = '09-configuracion';A = '03-config-atajos.png';            D = 'Configuracion - pestana Atajos de teclado' }

    @{ F = '10-dialogos';     A = '01-dialogo-atajos.png';           D = 'Ventana de atajos de teclado (Ctrl+/)' }
    @{ F = '10-dialogos';     A = '02-dialogo-personalizar-colores.png'; D = 'Dialogo Personalizar colores (boton Apariencia)' }
    @{ F = '10-dialogos';     A = '03-dialogo-pegar-excel.png';      D = 'Dialogo Pegar de Excel (Editor -> Pegar de Excel)' }
    @{ F = '10-dialogos';     A = '04-dialogo-selector-tipo-losa.png';   D = 'Selector visual de tipos de losa (clic en columna TIPO)' }
    @{ F = '10-dialogos';     A = '05-dialogo-doctor-dl.png';        D = 'Ventana Doctor .DL (Diagnosticar al abrir un .DL)' }
    @{ F = '10-dialogos';     A = '06-dialogo-captura-tecla.png';    D = 'Ventana de captura de tecla (Config -> Atajos -> editar)' }

    @{ F = '11-acerca';       A = '01-acerca-de.png';                D = 'Modo Acerca de (creditos)' }
)

# --- Codigos de tecla virtual ---
$VK_SHIFT = 0x10; $VK_CTRL = 0x11; $VK_F10 = 0x79; $VK_F11 = 0x7A; $VK_F12 = 0x7B

function Test-Down([int]$vk)
{
    return ([CapWin]::GetAsyncKeyState($vk) -band 0x8000) -ne 0
}

function Get-Bounds([IntPtr]$hwnd)
{
    $r = New-Object CapRect
    # DWMWA_EXTENDED_FRAME_BOUNDS = 9 -> bordes visibles reales (sin la sombra invisible)
    $ok = [CapWin]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16)
    if ($ok -ne 0) { [CapWin]::GetWindowRect($hwnd, [ref]$r) | Out-Null }
    return $r
}

function Save-Capture([IntPtr]$hwnd, [string]$path)
{
    [CapWin]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 350    # dejar repintar y soltar las teclas
    $r = Get-Bounds $hwnd
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $dir = Split-Path $path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $true
}

# --- Banner ---
Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  Asistente de capturas de LosasPlus' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '  Ctrl+Shift+F12  Capturar     Ctrl+Shift+F11  Saltar     Ctrl+Shift+F10  Terminar'
Write-Host ''

# --- Retomar desde la primera captura faltante ---
$indice = 0
for ($i = 0; $i -lt $targets.Count; $i++)
{
    $p = Join-Path $PSScriptRoot (Join-Path $targets[$i].F $targets[$i].A)
    if (-not (Test-Path $p)) { $indice = $i; break }
    $indice = $i + 1
}
if ($indice -ge $targets.Count)
{
    Write-Host '  Las 54 capturas ya existen. Re-ejecutando desde la 1 (se sobreescriben).' -ForegroundColor Yellow
    $indice = 0
}
elseif ($indice -gt 0)
{
    Write-Host "  Retomando: $indice ya capturadas, empezando en la $($indice + 1)." -ForegroundColor Yellow
}

$capturadas = 0; $saltadas = 0

function Show-Target([int]$i)
{
    $t = $targets[$i]
    Write-Host ''
    Write-Host ("  [{0:00}/{1}]  {2}\{3}" -f ($i + 1), $targets.Count, $t.F, $t.A) -ForegroundColor White
    Write-Host ("           Prepara:  {0}" -f $t.D) -ForegroundColor Gray
}

Show-Target $indice

$comboPrev = $false
while ($indice -lt $targets.Count)
{
    $ctrl = Test-Down $VK_CTRL
    $shift = Test-Down $VK_SHIFT
    $cap = $ctrl -and $shift -and (Test-Down $VK_F12)
    $skip = $ctrl -and $shift -and (Test-Down $VK_F11)
    $quit = $ctrl -and $shift -and (Test-Down $VK_F10)
    $comboNow = $cap -or $skip -or $quit

    if ($comboNow -and -not $comboPrev)
    {
        if ($quit) { break }

        $t = $targets[$indice]
        $destino = Join-Path $PSScriptRoot (Join-Path $t.F $t.A)

        if ($skip)
        {
            Write-Host ("  -> Saltada {0:00}/{1}: {2}" -f ($indice + 1), $targets.Count, $t.A) -ForegroundColor Yellow
            $saltadas++
            $indice++
            if ($indice -lt $targets.Count) { Show-Target $indice }
        }
        else
        {
            # Capturar: solo si la ventana al frente es de LosasPlus.
            $hwnd = [CapWin]::GetForegroundWindow()
            $pid2 = 0
            [CapWin]::GetWindowThreadProcessId($hwnd, [ref]$pid2) | Out-Null
            $proc = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
            if (-not $proc -or $proc.ProcessName -ne 'LosasPlus')
            {
                $nombre = '(desconocido)'
                if ($proc) { $nombre = $proc.ProcessName }
                Write-Host "  !! La ventana al frente no es LosasPlus ($nombre). Pulsacion ignorada." -ForegroundColor Red
            }
            elseif (Save-Capture $hwnd $destino)
            {
                Write-Host ("  OK  {0:00}/{1}  guardada -> {2}\{3}" -f ($indice + 1), $targets.Count, $t.F, $t.A) -ForegroundColor Green
                [console]::Beep(880, 120)
                $capturadas++
                $indice++
                if ($indice -lt $targets.Count) { Show-Target $indice }
            }
            else
            {
                Write-Host '  !! No se pudo medir la ventana. Reintenta.' -ForegroundColor Red
            }
        }
    }

    $comboPrev = $comboNow
    Start-Sleep -Milliseconds 60
}

# --- Resumen ---
$pendientes = $targets.Count - $capturadas - $saltadas
Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ("  Resumen:  {0} capturadas   {1} saltadas   {2} pendientes" -f $capturadas, $saltadas, $pendientes) -ForegroundColor Cyan
Write-Host '  Re-ejecuta el script para retomar las que falten.' -ForegroundColor Gray
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''
