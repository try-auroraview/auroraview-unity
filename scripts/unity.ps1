param([ValidateSet('test','accept','agent')][string]$Mode = 'test')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$editor = $env:UNITY_EDITOR
if (-not $editor) {
    $editors = Get-ChildItem -LiteralPath 'C:\Program Files\Unity\Hub\Editor' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
    foreach ($candidate in $editors) { $path = Join-Path $candidate.FullName 'Editor/Unity.exe'; if (Test-Path -LiteralPath $path) { $editor = $path; break } }
}
if (-not $editor -or -not (Test-Path -LiteralPath $editor)) { throw 'Set UNITY_EDITOR to an installed licensed Windows Unity 2022.3+ Editor executable.' }
$project = Join-Path $root 'Samples~/SceneTools'
$output = Join-Path $root 'build/evidence'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log = Join-Path $output "unity-$Mode.log"
$arguments = @('-projectPath', $project, '-logFile', $log)
if ($Mode -eq 'test') {
    $prior = Join-Path $output 'editmode.xml'
    if (Test-Path -LiteralPath $prior) { Remove-Item -LiteralPath $prior -Force }
    $arguments += @('-batchmode', '-runTests', '-testPlatform', 'EditMode', '-testResults', (Join-Path $output 'editmode.xml'))
} elseif ($Mode -eq 'accept') {
    $prior = Join-Path $output 'unity-acceptance.json'
    if (Test-Path -LiteralPath $prior) { Remove-Item -LiteralPath $prior -Force }
    # Interactive rendering is essential for native HWND acceptance; this is not batchmode.
    $arguments += @('-executeMethod', 'AuroraView.Unity.Acceptance.Run')
} else {
    # Remove only known receipts in this isolated package evidence directory.
    foreach ($name in @('unity-agent-ready.json','mcp-live-result.json','unity-agent-acceptance.json')) {
        $file = Join-Path $output $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    $arguments += @('-batchmode', '-executeMethod', 'AuroraView.Unity.AgentAcceptance.Run')
}
$process = Start-Process -FilePath $editor -ArgumentList ($arguments | ForEach-Object { '"' + $_ + '"' }) -PassThru -WindowStyle Hidden
if ($Mode -eq 'agent') {
    $ready = Join-Path $output 'unity-agent-ready.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(240)
    while (-not (Test-Path -LiteralPath $ready)) {
        if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "Unity agent endpoint did not initialize. See $log." }
        Start-Sleep -Milliseconds 250
    }
    $receipt = Get-Content -LiteralPath $ready -Raw | ConvertFrom-Json
    if ($process.HasExited -or $receipt.processId -ne $process.Id) { throw 'Agent readiness does not belong to the newly launched live Editor. No mutation was sent.' }
    vx node (Join-Path $root 'agent/live-test.mjs') --pid $receipt.processId --output (Join-Path $output 'mcp-live-result.json')
    if ($LASTEXITCODE -ne 0) { throw 'MCP live host acceptance failed.' }
}
if (-not $process.WaitForExit(240000)) { throw "Unity exceeded four minutes. Inspect PID $($process.Id) and $log; process is retained for diagnosis." }
if ($process.ExitCode -ne 0) { throw "Unity exited with code $($process.ExitCode). See $log." }
if ($Mode -eq 'test') {
    [xml]$result = Get-Content -LiteralPath (Join-Path $output 'editmode.xml') -Raw
    if ([int]$result.'test-run'.failed -ne 0 -or [int]$result.'test-run'.passed -lt 1) { throw 'EditMode tests did not pass.' }
} elseif ($Mode -eq 'accept') {
    $result = Get-Content -LiteralPath (Join-Path $output 'unity-acceptance.json') -Raw | ConvertFrom-Json
    if ($result.processId -ne $process.Id -or -not $result.browserRoundTrip -or -not $result.browserCreateRoundTrip -or -not $result.sceneMutation -or -not $result.undoVerified) { throw 'Unity native acceptance is incomplete or belongs to a different process.' }
} else {
    $result = Get-Content -LiteralPath (Join-Path $output 'unity-agent-acceptance.json') -Raw | ConvertFrom-Json
    if ($result.processId -ne $process.Id -or -not $result.unityObjectReadback -or -not $result.undoVerified -or -not $result.endpointStopped) { throw 'Unity agent acceptance is incomplete or belongs to a different process.' }
}
Write-Output "Unity $Mode passed. Evidence: $output"
