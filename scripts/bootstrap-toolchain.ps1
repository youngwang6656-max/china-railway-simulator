$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot '.tools'
$dotnetRoot = Join-Path $toolsRoot 'dotnet'
$godotRoot = Join-Path $toolsRoot 'godot'
$downloadRoot = Join-Path $toolsRoot 'downloads'

New-Item -ItemType Directory -Force -Path $dotnetRoot, $godotRoot, $downloadRoot | Out-Null

$localDotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
$userDotnetExe = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnetExe = if (Test-Path $localDotnetExe) {
    $localDotnetExe
} elseif (Test-Path $userDotnetExe) {
    $userDotnetExe
} else {
    $localDotnetExe
}
if (-not (Test-Path $dotnetExe)) {
    $installer = Join-Path $downloadRoot 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' `
        -UseBasicParsing `
        -OutFile $installer
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel '8.0' `
        -Quality 'GA' `
        -InstallDir $dotnetRoot `
        -NoPath
    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK installation failed with exit code $LASTEXITCODE"
    }

    $dotnetExe = $localDotnetExe
}

$godotExe = Get-ChildItem -Path $godotRoot -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
    Select-Object -First 1
if ($null -eq $godotExe) {
    $archive = Join-Path $downloadRoot 'godot-4.6.3-dotnet-win64.zip'
    Invoke-WebRequest `
        'https://github.com/godotengine/godot/releases/download/4.6.3-stable/Godot_v4.6.3-stable_mono_win64.zip' `
        -UseBasicParsing `
        -OutFile $archive
    Expand-Archive -Path $archive -DestinationPath $godotRoot -Force
    $godotExe = Get-ChildItem -Path $godotRoot -Recurse -Filter 'Godot*v4.6.3*mono*win64*.exe' |
        Select-Object -First 1
}

if (-not (Test-Path $dotnetExe)) {
    throw "Missing .NET SDK at $dotnetExe"
}
if ($null -eq $godotExe) {
    throw "Missing Godot 4.6.3 .NET executable under $godotRoot"
}

$dotnetVersion = (& $dotnetExe --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "dotnet --version failed"
}
if ($dotnetVersion -notmatch '^8\.[0-9]+\.[0-9]+(?:[-+].*)?$') {
    throw "Expected .NET SDK 8.x, but found '$dotnetVersion'."
}

$godotVersion = (& $godotExe.FullName --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Godot --version failed"
}
if ($godotVersion -notmatch '^4\.6\.3\.stable\.mono(?:\..+)?$') {
    throw "Expected Godot 4.6.3 stable .NET, but found '$godotVersion'."
}

Write-Host "TOOLCHAIN_OK dotnet=$dotnetVersion godot=$godotVersion"
