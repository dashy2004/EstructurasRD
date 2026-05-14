# requires -Version 5.1
# Capture smoke screenshots for docs/referencia/ui-design/.
# Uses PrintWindow API to render the WPF window directly to a memory
# bitmap, avoiding DWM extended-frame issues that gave black borders
# in the previous CopyFromScreen approach.
#
# ASCII-only on purpose: PowerShell 5.1 reads .ps1 as ANSI without BOM,
# so any UTF-8 multibyte char breaks the parser.

[CmdletBinding()]
param(
    [string]$ExePath  = "$PSScriptRoot\..\src.Memoria\bin\Debug\net8.0-windows\MemoriaPlus.exe",
    [string]$OutDir   = "$PSScriptRoot\..\docs\referencia\ui-design",
    [double]$RenderWaitSeconds = 8.0
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;

public struct RECT { public int Left, Top, Right, Bottom; }

public class Win32 {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
}
"@

# PW_RENDERFULLCONTENT = 0x00000002 -- includes WPF/DirectComposition.
$PW_RENDERFULLCONTENT = 2

function Capture-WindowToPng {
    param([System.IntPtr]$Hwnd, [string]$OutPath)

    # DWMWA_EXTENDED_FRAME_BOUNDS = 9 returns the actual visible rect,
    # excluding the invisible drop-shadow margins.
    $rect = New-Object RECT
    $hr = [Win32]::DwmGetWindowAttribute($Hwnd, 9, [ref]$rect, [System.Runtime.InteropServices.Marshal]::SizeOf($rect))
    if ($hr -ne 0) {
        if (-not [Win32]::GetWindowRect($Hwnd, [ref]$rect)) {
            throw ("GetWindowRect failed: " + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
        }
    }
    $w = $rect.Right  - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { throw ("Invalid rect: " + $w + ", " + $h) }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    $ok = [Win32]::PrintWindow($Hwnd, $hdc, [uint32]$PW_RENDERFULLCONTENT)
    $gfx.ReleaseHdc($hdc)
    $gfx.Dispose()

    if (-not $ok) {
        Write-Warning "  PrintWindow failed -- falling back to CopyFromScreen"
        $bmp.Dispose()
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $gfx = [System.Drawing.Graphics]::FromImage($bmp)
        $gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
        $gfx.Dispose()
    }

    $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("  -> " + $OutPath)
}

function Capture-Smoke {
    param([string]$Modo, [string]$Tab, [string]$OutName, [string[]]$ExtraArgs = @())

    Get-Process MemoriaPlus -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300

    $cliArgs = @()
    if ($Modo) { $cliArgs += "--modo=$Modo" }
    if ($Tab)  { $cliArgs += "--tab=$Tab" }
    $cliArgs += $ExtraArgs

    Write-Host ("Capturing: " + $OutName + "  (" + ($cliArgs -join ' ') + ")")

    $proc = Start-Process -FilePath $ExePath -ArgumentList $cliArgs -PassThru
    Start-Sleep -Seconds $RenderWaitSeconds

    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
        $proc.Refresh()
        if ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
            Start-Sleep -Seconds 2
            $proc.Refresh()
        }
    }
    $hwnd = $proc.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Warning ("  No window handle for " + $OutName + ". Skip.")
        $proc.Kill()
        return
    }

    [Win32]::ShowWindowAsync($hwnd, 9) | Out-Null  # SW_RESTORE
    [Win32]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 800

    $out = Join-Path $OutDir $OutName
    Capture-WindowToPng -Hwnd $hwnd -OutPath $out

    $proc.Kill()
    Start-Sleep -Milliseconds 200
}

$Captures = @(
    @{ Modo='calculos';      Tab='datos';   Out='smoke_datos_generales_v1.png' },
    @{ Modo='calculos';      Tab='cargas';  Out='smoke_cargas_v1.png' },
    @{ Modo='calculos';      Tab='niveles'; Out='smoke_niveles_v1.png' },
    @{ Modo='calculos';      Tab='generar'; Out='smoke_generar_v1.png' },
    @{ Modo='explorador';    Tab=$null;     Out='smoke_explorador_v1.png' },
    @{ Modo='busqueda';      Tab=$null;     Out='smoke_busqueda_v1.png' },
    @{ Modo='plantillas';    Tab=$null;     Out='smoke_plantillas_v1.png' },
    @{ Modo='configuracion'; Tab=$null;     Out='smoke_configuracion_v1.png' }
)

Write-Host ("Capturing " + $Captures.Count + " screenshots...")
Write-Host ("ExePath: " + $ExePath)
Write-Host ("OutDir:  " + $OutDir)
Write-Host ""

if (-not (Test-Path $ExePath)) {
    throw ("Missing exe: " + $ExePath + ". Run: dotnet build")
}
if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}

foreach ($c in $Captures) {
    Capture-Smoke -Modo $c.Modo -Tab $c.Tab -OutName $c.Out
}

Get-Process MemoriaPlus -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host ""
Write-Host ("Done. " + $Captures.Count + " screenshots updated in " + $OutDir)
