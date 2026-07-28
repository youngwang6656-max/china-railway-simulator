$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$localGodot = Get-ChildItem (Join-Path $repoRoot '.tools\godot') `
    -Recurse `
    -Filter 'Godot*v4.6.3*mono*win64*.exe' `
    -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
$godot = if ($null -ne $localGodot) {
    $localGodot
} else {
    $command = Get-Command godot, godot4 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $command) {
        throw "Godot 4.6.3 .NET was not found. Run scripts/bootstrap-toolchain.ps1."
    }

    $command.Source
}
$godot = (Resolve-Path $godot).Path

Push-Location $repoRoot
try {
    & $dotnet restore RailSim.sln
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed"
    }

    & $dotnet format RailSim.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format verification failed"
    }

    & $dotnet build RailSim.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed"
    }

    & $dotnet test RailSim.sln --configuration Release --no-build `
        --collect:"XPlat Code Coverage"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed"
    }

    $smokeLog = Join-Path $repoRoot '.tools\godot-smoke.log'
    $smokeStdout = Join-Path $repoRoot '.tools\godot-smoke.stdout.log'
    $smokeStderr = Join-Path $repoRoot '.tools\godot-smoke.stderr.log'
    $smokeStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $smokeStartInfo.FileName = $godot
    $smokeStartInfo.Arguments =
        "--headless --log-file `"$smokeLog`" --path game -- --smoke-test"
    $smokeStartInfo.UseShellExecute = $false
    $smokeStartInfo.CreateNoWindow = $true
    $smokeStartInfo.RedirectStandardOutput = $true
    $smokeStartInfo.RedirectStandardError = $true
    $smokeProcess = [System.Diagnostics.Process]::Start($smokeStartInfo)
    $stdoutTask = $smokeProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $smokeProcess.StandardError.ReadToEndAsync()

    if (-not $smokeProcess.WaitForExit(30000)) {
        Stop-Process -Id $smokeProcess.Id -Force
        $smokeProcess.WaitForExit()
        throw "Godot smoke test timed out after 30 seconds."
    }

    $smokeProcess.WaitForExit()
    $smokeOutput = $stdoutTask.GetAwaiter().GetResult() +
        $stderrTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($smokeStdout, $stdoutTask.Result)
    [System.IO.File]::WriteAllText($smokeStderr, $stderrTask.Result)
    if ($smokeProcess.ExitCode -ne 0) {
        throw "Godot smoke test exited with $($smokeProcess.ExitCode).`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_SMOKE tick=4') {
        throw "Godot smoke marker missing.`n$smokeOutput"
    }

    Write-Host 'VERIFY_OK'
} finally {
    Pop-Location
}
