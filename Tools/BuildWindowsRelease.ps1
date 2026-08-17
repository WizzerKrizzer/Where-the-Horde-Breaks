param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$projectPath = Resolve-Path (Join-Path $PSScriptRoot "..")
$logPath = Join-Path $projectPath "unity-build-release.log"
$outputPath = Join-Path $projectPath "Builds\Release\WhereTheHordeBreaks.exe"

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
    -executeMethod TowerDefense.Editor.BuildPlayerTools.BuildWindowsReleaseBatch `
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

Write-Host "Release build log: $logPath"
Write-Host "Release build output: $outputPath"
if ($exitCode -ne 0) {
    Write-Error "Release build failed. See log: $logPath"
}
exit $exitCode
