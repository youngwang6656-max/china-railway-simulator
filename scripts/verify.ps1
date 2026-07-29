$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot '.tools'
$logsRoot = Join-Path $toolsRoot 'logs'
$testResultsRoot = Join-Path $toolsRoot 'test-results'
$gamePath = Join-Path $repoRoot 'game'
New-Item -ItemType Directory -Force -Path $toolsRoot, $logsRoot, $testResultsRoot |
    Out-Null

$localDotnet = Join-Path $toolsRoot 'dotnet\dotnet.exe'
$userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) {
    $localDotnet
} elseif (Test-Path $userDotnet) {
    $userDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}
$dotnet = (Resolve-Path $dotnet).Path

$localGodot = Get-ChildItem (Join-Path $toolsRoot 'godot') `
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

$dotnetVersion = (& $dotnet --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "dotnet --version failed with exit code $LASTEXITCODE"
}
if ($dotnetVersion -notmatch '^8\.[0-9]+\.[0-9]+(?:[-+].*)?$') {
    throw "Expected .NET SDK 8.x, but found '$dotnetVersion'."
}

$godotVersion = (& $godot --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Godot --version failed with exit code $LASTEXITCODE"
}
if ($godotVersion -notmatch '^4\.6\.3\.stable\.mono(?:\..+)?$') {
    throw "Expected Godot 4.6.3 stable .NET, but found '$godotVersion'."
}

Push-Location $repoRoot
try {
    & $dotnet restore RailSim.sln --ignore-failed-sources `
        -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed"
    }

    & $dotnet format RailSim.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet format verification failed"
    }

    & $dotnet build RailSim.sln --configuration Release --no-restore `
        -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed"
    }

    & $dotnet build game\RailSim.App.csproj --configuration Debug --no-restore `
        -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) {
        throw "Godot Debug runtime build failed"
    }

    $testRunName = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') +
        '-' + [Guid]::NewGuid().ToString('N')
    $testRunRoot = Join-Path $testResultsRoot $testRunName
    New-Item -ItemType Directory -Force -Path $testRunRoot | Out-Null

    & $dotnet test RailSim.sln --configuration Release --no-build `
        -p:NuGetAudit=false `
        --results-directory $testRunRoot `
        --logger 'trx;LogFileName=verification.trx' `
        --collect:"XPlat Code Coverage"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed"
    }

    $trxFiles = @(Get-ChildItem $testRunRoot -Recurse -Filter '*.trx' -File)
    if ($trxFiles.Count -ne 1) {
        throw "Expected exactly one TRX file, but found $($trxFiles.Count)."
    }

    [xml]$trx = Get-Content -Raw -Encoding UTF8 $trxFiles[0].FullName
    $counters = $trx.TestRun.ResultSummary.Counters
    if ($null -eq $counters) {
        throw "TRX result counters are missing."
    }

    $totalTests = [int]$counters.total
    $passedTests = [int]$counters.passed
    $failedTests = [int]$counters.failed
    if ($failedTests -ne 0 -or $totalTests -lt 126 -or $passedTests -lt 126) {
        throw "Invalid test totals: total=$totalTests passed=$passedTests failed=$failedTests."
    }

    $coverageFiles = @(
        Get-ChildItem $testRunRoot -Recurse -Filter 'coverage.cobertura.xml' -File
    )
    if ($coverageFiles.Count -lt 1) {
        throw "Expected at least one coverage.cobertura.xml in the current test run."
    }

    $linesValid = 0
    $linesCovered = 0
    foreach ($coverageFile in $coverageFiles) {
        [xml]$coverage = Get-Content -Raw -Encoding UTF8 $coverageFile.FullName
        if ($null -eq $coverage.coverage) {
            throw "Cobertura root element is missing in $($coverageFile.FullName)."
        }

        $currentLinesValid = [int]$coverage.coverage.'lines-valid'
        $currentLinesCovered = [int]$coverage.coverage.'lines-covered'
        if ($currentLinesValid -le 0 -or $currentLinesCovered -le 0) {
            throw "Invalid coverage totals in $($coverageFile.FullName): " +
                "lines-valid=$currentLinesValid lines-covered=$currentLinesCovered."
        }

        $linesValid = [Math]::Max($linesValid, $currentLinesValid)
        $linesCovered = [Math]::Max($linesCovered, $currentLinesCovered)
    }
    $lineCoverage = $linesCovered / [double]$linesValid
    if ($lineCoverage -lt 0.75) {
        throw "Line coverage $($lineCoverage.ToString('P2')) is below 75%."
    }

    $smokeLog = Join-Path $logsRoot 'godot-smoke.log'
    $smokeStdout = Join-Path $logsRoot 'godot-smoke.stdout.log'
    $smokeStderr = Join-Path $logsRoot 'godot-smoke.stderr.log'
    $smokeStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $smokeStartInfo.FileName = $godot
    $smokeStartInfo.Arguments =
        "--headless --log-file `"$smokeLog`" --path `"$gamePath`" -- --smoke-test"
    $smokeStartInfo.WorkingDirectory = $repoRoot
    $smokeStartInfo.UseShellExecute = $false
    $smokeStartInfo.CreateNoWindow = $true
    $smokeStartInfo.RedirectStandardOutput = $true
    $smokeStartInfo.RedirectStandardError = $true
    $smokeProcess = [System.Diagnostics.Process]::Start($smokeStartInfo)
    $stdoutTask = $smokeProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $smokeProcess.StandardError.ReadToEndAsync()
    $smokeTimedOut = -not $smokeProcess.WaitForExit(30000)

    if ($smokeTimedOut) {
        $smokeProcess.Kill()
    }

    $smokeProcess.WaitForExit()
    $smokeOutput = $stdoutTask.GetAwaiter().GetResult() +
        $stderrTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($smokeStdout, $stdoutTask.Result)
    [System.IO.File]::WriteAllText($smokeStderr, $stderrTask.Result)
    if ($smokeTimedOut) {
        throw "Godot smoke test timed out after 30 seconds."
    }
    if ($smokeProcess.ExitCode -ne 0) {
        throw "Godot smoke test exited with $($smokeProcess.ExitCode).`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_SMOKE tick=4') {
        throw "Godot smoke marker missing.`n$smokeOutput"
    }
    if (
        $smokeOutput -notmatch
        'RAILSIM_WORLD_SMOKE cities=64 stations=64 tracks=12'
    ) {
        throw "Godot world smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_HISTORY_SMOKE rebuild=True') {
        throw "Godot history smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_SNAPSHOT_SMOKE published=True') {
        throw "Godot snapshot smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_PERSISTENCE_SMOKE roundtrip=True') {
        throw "Godot persistence smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_HOST_COMMAND_SMOKE dispatched=True') {
        throw "Godot host command smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_REPLACEMENT_SMOKE current=True') {
        throw "Godot replacement smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_MANUAL_WARNING_SMOKE confirmed=True') {
        throw "Godot manual warning smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_MANUAL_CONNECTOR_SMOKE built=True') {
        throw "Godot manual connector smoke marker missing.`n$smokeOutput"
    }
    if ($smokeOutput -notmatch 'RAILSIM_EDIT_TOOLS_SMOKE complete=True') {
        throw "Godot edit tools smoke marker missing.`n$smokeOutput"
    }

    Write-Host "TOOLCHAIN_OK dotnet=$dotnetVersion godot=$godotVersion"
    Write-Host "TESTS_OK total=$totalTests passed=$passedTests failed=$failedTests"
    Write-Host (
        "COVERAGE_OK lines-valid=$linesValid lines-covered=$linesCovered " +
        "line-rate=$($lineCoverage.ToString('P2'))"
    )
    Write-Host 'GODOT_SMOKE_OK tick=4'
    Write-Host 'GODOT_WORLD_SMOKE_OK cities=64 stations=64 tracks=12'
    Write-Host 'GODOT_HISTORY_SMOKE_OK rebuild=True'
    Write-Host 'GODOT_SNAPSHOT_SMOKE_OK published=True'
    Write-Host 'GODOT_PERSISTENCE_SMOKE_OK roundtrip=True'
    Write-Host 'GODOT_HOST_COMMAND_SMOKE_OK dispatched=True'
    Write-Host 'GODOT_REPLACEMENT_SMOKE_OK current=True'
    Write-Host 'GODOT_MANUAL_WARNING_SMOKE_OK confirmed=True'
    Write-Host 'GODOT_MANUAL_CONNECTOR_SMOKE_OK built=True'
    Write-Host 'GODOT_EDIT_TOOLS_SMOKE_OK complete=True'
    Write-Host 'VERIFY_OK'
} finally {
    Pop-Location
}
