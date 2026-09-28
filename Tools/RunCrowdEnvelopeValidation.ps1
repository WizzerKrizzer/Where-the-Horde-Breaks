param(
    [ValidateSet('Short','Order','Full','Build')][string]$Mode = 'Short',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe',
    [string]$TestProject = '.codex-temp\jitter-project'
)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRoot = [IO.Path]::GetFullPath((Join-Path $workspace $TestProject))
if (-not $testRoot.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The isolated test project must be inside this workspace.'
}
if ($testRoot -eq $workspace) { throw 'Use an isolated test project while the main editor is open.' }
$output = Join-Path $workspace 'Logs\CrowdEnvelope'
New-Item -ItemType Directory -Force $output | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings')) {
    & robocopy (Join-Path $workspace $folder) (Join-Path $testRoot $folder) /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed to copy $folder" }
}
$log = Join-Path $output "$Mode.log"
$arguments = @('-batchmode','-automated','-projectPath',('"'+$testRoot+'"'),'-logFile',('"'+$log+'"'))
if ($Mode -eq 'Build') {
    $arguments += @('-quit','-executeMethod','TowerDefense.Editor.CrowdValidationTools.BuildAcceptancePlayer')
} else {
    $filter = switch ($Mode) {
        'Short' { 'DenseThreeStreamMerge_StageAudit;TightlyPackedThreeStreamMerge_StageAudit;PackedGoblinQueue_StartsTogetherOnFirstTickAfterBlockerRemoval;RedGoblinQueue_TouchesBodiesAndSettlesWithoutRunningInPlace;DenseQueue_FlowsThroughNarrowRightAngleWithoutDeadlock;RunningBake_RendersAndMovesOnTheExistingGpuBackend;HardwareFrameTiming_Probe;SpawnClearance_RejectsMovedQueriesAndDifferentPositions' }
        'Order' { 'SameSeed_ForwardRepeatReverse_AfterOpeningSpace' }
        'Full' { 'ActualTenThousand_ThroughMergeAndTail' }
    }
    $results = Join-Path $output "$Mode.xml"
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results }
    $arguments += @('-runTests','-testPlatform','PlayMode','-testFilter',$filter,'-testResults',('"'+$results+'"'))
}
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
foreach ($evidence in @('MergeValidation','CrowdEnvelope')) {
    $from = Join-Path $testRoot "Logs\$evidence"
    if (Test-Path -LiteralPath $from) {
        $destination = Join-Path $output $evidence
        New-Item -ItemType Directory -Force $destination | Out-Null
        Copy-Item -Path (Join-Path $from '*') -Destination $destination -Recurse -Force
    }
}
if ($Mode -ne 'Build') {
    if (-not (Test-Path -LiteralPath $results)) { throw "No test results. Inspect $log" }
    [xml]$xml = Get-Content -LiteralPath $results -Raw
    if ($xml.'test-run'.result -ne 'Passed') { throw "Validation failed. Inspect $results" }
} elseif ($process.ExitCode -ne 0) { throw "Build failed. Inspect $log" }
Write-Output "Completed $Mode. Evidence: $output"
