param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$projectPath = Resolve-Path (Join-Path $PSScriptRoot "..")
$resultsPath = Join-Path $projectPath "TestResults-PlayMode.xml"
$logPath = Join-Path $projectPath "unity-playmode.log"

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity.exe was not found at: $UnityPath"
}

& $UnityPath `
    -batchmode `
    -automated `
    -projectPath $projectPath `
    -runTests `
    -testPlatform PlayMode `
    -testResults $resultsPath `
    -logFile $logPath

$exitCode = $LASTEXITCODE
$batchAborted = $false
if (Test-Path -LiteralPath $logPath) {
    $logText = Get-Content -LiteralPath $logPath -Raw
    if ($logText -match "Aborting batchmode due to fatal error") {
        $batchAborted = $true
    }
}

if ($batchAborted) {
    $exitCode = 1
}
elseif (-not (Test-Path -LiteralPath $resultsPath)) {
    $exitCode = 1
}
else {
    [xml]$results = Get-Content -LiteralPath $resultsPath -Raw
    $testRunResult = $results.SelectSingleNode("/test-run").GetAttribute("result")
    if ($testRunResult -match "^Passed") {
        $exitCode = 0
    }
    else {
        $exitCode = 1
    }
}

Write-Host "PlayMode test results: $resultsPath"
Write-Host "Unity log: $logPath"
exit $exitCode
