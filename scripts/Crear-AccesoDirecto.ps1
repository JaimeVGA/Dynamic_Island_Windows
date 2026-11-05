<#
.SYNOPSIS
    Crea un acceso directo de Dynamic Island en el Escritorio (y opcionalmente en el Menú Inicio).

.PARAMETER ExePath
    Ruta al DynamicIsland.exe. Por defecto busca la compilación Release (o Debug) del proyecto.

.PARAMETER StartMenu
    Crea también el acceso directo en el Menú Inicio.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\Crear-AccesoDirecto.ps1 -StartMenu
#>
param(
    [string]$ExePath,
    [switch]$StartMenu
)

$root = Split-Path -Parent $PSScriptRoot

if (-not $ExePath) {
    $ExePath = Get-ChildItem -Path $root -Recurse -Filter DynamicIsland.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\(Release|publish)\\' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $ExePath -or -not (Test-Path $ExePath)) {
    Write-Error "No se encontró DynamicIsland.exe. Compila primero con: dotnet build -c Release"
    exit 1
}

$shell = New-Object -ComObject WScript.Shell

function New-Shortcut($folder) {
    $lnk = Join-Path $folder "Dynamic Island.lnk"
    $s = $shell.CreateShortcut($lnk)
    $s.TargetPath = $ExePath
    $s.WorkingDirectory = Split-Path -Parent $ExePath
    $s.IconLocation = "$ExePath,0"
    $s.Description = "Dynamic Island para Windows 11"
    $s.Save()
    Write-Host "Acceso directo creado: $lnk"
}

New-Shortcut ([Environment]::GetFolderPath('Desktop'))
if ($StartMenu) {
    New-Shortcut (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs')
}
