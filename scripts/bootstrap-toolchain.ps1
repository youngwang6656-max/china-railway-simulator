$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot '.tools'
$dotnetRoot = Join-Path $toolsRoot 'dotnet'
$godotRoot = Join-Path $toolsRoot 'godot'
$downloadRoot = Join-Path $toolsRoot 'downloads'

New-Item -ItemType Directory -Force -Path $dotnetRoot, $godotRoot, $downloadRoot | Out-Null

$dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
if (-not (Test-Path $dotnetExe)) {
    $installer = Join-Path $downloadRoot 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel '8.0' `
        -Quality 'GA' `
        -InstallDir $dotnetRoot `
        -NoPath
    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK installation failed with exit code $LASTEXITCODE"
    }
}

$godotExe = Get-ChildItem -Path $godotRoot -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
    Select-Object -First 1
if ($null -eq $godotExe) {
    $archive = Join-Path $downloadRoot 'godot-4.6.3-dotnet-win64.zip'
    Invoke-WebRequest `
        'https://github.com/godotengine/godot/releases/download/4.6.3-stable/Godot_v4.6.3-stable_mono_win64.zip' `
        -OutFile $archive
    Expand-Archive -Path $archive -DestinationPath $godotRoot -Force
    $godotExe = Get-ChildItem -Path $godotRoot -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
        Select-Object -First 1
}

if (-not (Test-Path $dotnetExe)) {
    throw "Missing local .NET SDK at $dotnetExe"
}
if ($null -eq $godotExe) {
    throw "Missing Godot 4.6.3 .NET executable under $godotRoot"
}

& $dotnetExe --info
if ($LASTEXITCODE -ne 0) {
    throw "Local dotnet --info failed"
}

& $godotExe.FullName --version
if ($LASTEXITCODE -ne 0) {
    throw "Godot --version failed"
}
