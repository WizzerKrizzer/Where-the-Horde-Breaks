param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$projectPath = Resolve-Path (Join-Path $PSScriptRoot "..")
$logPath = Join-Path $projectPath "unity-build-development.log"
$outputPath = Join-Path $projectPath "Builds\Development\WhereTheHordeBreaks.exe"

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity.exe was not found at: $UnityPath"
}

if (Test-Path -LiteralPath $logPath) {
    Remove-Item -LiteralPath $logPath -Force
}

& $UnityPath `
    -batchmode `
    -quit `
    -projectPath $projectPath `
    -executeMethod TowerDefense.Editor.BuildPlayerTools.BuildWindowsDevelopmentBatch `
    -logFile $logPath

$exitCode = $LASTEXITCODE
if (-not (Test-Path -LiteralPath $outputPath)) {
    $exitCode = 1
}

if (Test-Path -LiteralPath $logPath) {
    $logText = Get-Content -LiteralPath $logPath -Raw
    if ($logText -match "Application will terminate with return code 1" -or $logText -match "Build failed") {
        $exitCode = 1
    }
}

Write-Host "Development build log: $logPath"
Write-Host "Development build output: $outputPath"
exit $exitCode
