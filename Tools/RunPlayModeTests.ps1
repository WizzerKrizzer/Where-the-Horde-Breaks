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
    -projectPath $projectPath `
    -runTests `
    -testPlatform PlayMode `
    -testResults $resultsPath `
    -logFile $logPath `
    -quit

$exitCode = $LASTEXITCODE
if (Test-Path -LiteralPath $logPath) {
    $logText = Get-Content -LiteralPath $logPath -Raw
    if ($logText -match "Aborting batchmode due to fatal error") {
        $exitCode = 1
    }
}

if (-not (Test-Path -LiteralPath $resultsPath)) {
    $exitCode = 1
}

Write-Host "PlayMode test results: $resultsPath"
Write-Host "Unity log: $logPath"
exit $exitCode
