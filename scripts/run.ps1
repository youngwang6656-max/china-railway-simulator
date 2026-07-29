$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot '.tools'
$logsRoot = Join-Path $toolsRoot 'logs'
$gamePath = Join-Path $repoRoot 'game'
New-Item -ItemType Directory -Force -Path $logsRoot | Out-Null

$godot = Get-ChildItem (Join-Path $toolsRoot 'godot') `
    -Recurse `
    -Filter 'Godot*v4.6.3*mono*win64*.exe' `
    -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
if ($null -eq $godot) {
    throw 'Godot 4.6.3 .NET was not found. Run scripts/bootstrap-toolchain.ps1 first.'
}

$runtimeLog = Join-Path $logsRoot 'godot-runtime.log'
$arguments = "--log-file `"$runtimeLog`" --path `"$gamePath`""
Start-Process -FilePath $godot -ArgumentList $arguments
