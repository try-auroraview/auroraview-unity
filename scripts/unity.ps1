param([ValidateSet('test','accept','agent','core','owned','compile')][string]$Mode = 'test', [string]$Node = 'node', [string]$RunId)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$editor = $env:UNITY_EDITOR
if ($Mode -eq 'owned' -and (-not $editor -or $RunId.Length -ne 32 -or $RunId -cnotmatch '^[a-f0-9]{32}$')) {
    throw 'Owned test launch requires explicit UNITY_EDITOR and a fresh lowercase 32-hex run ID.'
}
if (-not $editor) {
    $editors = Get-ChildItem -LiteralPath 'C:\Program Files\Unity\Hub\Editor' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
    foreach ($candidate in $editors) { $path = Join-Path $candidate.FullName 'Editor/Unity.exe'; if (Test-Path -LiteralPath $path) { $editor = $path; break } }
}
if (-not $editor -or -not (Test-Path -LiteralPath $editor)) { throw 'Set UNITY_EDITOR to an installed licensed Windows Unity 2022.3+ Editor executable.' }
if ($Mode -eq 'compile') {
    $data = Join-Path (Split-Path -Parent $editor) 'Data'
    $framework = Join-Path $data 'MonoBleedingEdge/lib/mono/4.7.1-api'
    $compiler = Join-Path $data 'DotNetSdkRoslyn/csc.dll'
    $dotnet = Join-Path $data 'NetCoreRuntime/dotnet.exe'
    foreach ($path in @($framework, $compiler, $dotnet)) { if (-not (Test-Path -LiteralPath $path)) { throw "Unity compile dependency unavailable: $path" } }
    $output = Join-Path $root 'build~/compile'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $references = @(Get-ChildItem -LiteralPath $framework -Filter '*.dll' -File) +
        @(Get-ChildItem -LiteralPath (Join-Path $framework 'Facades') -Filter '*.dll' -File) +
        @(Get-ChildItem -LiteralPath (Join-Path $data 'Managed') -Filter '*.dll' -File -Recurse | Where-Object { $_.Name -match '^Unity(Engine|Editor)(\.|$)' })
    $compileArgs = @('/nologo', '/nostdlib+', '/langversion:9', '/target:library', ('/out:' + (Join-Path $output 'AuroraView.Editor.dll')))
    $compileArgs += @($references | ForEach-Object { '/reference:' + $_.FullName })
    $compileArgs += @(Get-ChildItem -LiteralPath (Join-Path $root 'Editor') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    $response = Join-Path $output 'compile.rsp'
    [IO.File]::WriteAllLines($response, @($compileArgs | ForEach-Object { '"' + $_ + '"' }), (New-Object Text.UTF8Encoding($false)))
    vx --cache-mode offline --no-auto-install uv run --offline --no-project --no-sync -- $dotnet $compiler /noconfig ('@' + $response)
    exit $LASTEXITCODE
}
$project = Join-Path $root 'Samples~/SceneTools'
$output = $env:AURORAVIEW_UNITY_EVIDENCE_DIR
if (-not $output) { $output = Join-Path $root 'build~/evidence'; if ($Mode -eq 'core') { $output = Join-Path $output 'core' } }
if ($Mode -eq 'owned') {
    if (-not $env:AURORAVIEW_UNITY_EVIDENCE_DIR) { $output = Join-Path $output ('owned-' + $RunId) }
    if (Test-Path -LiteralPath $output) { throw 'Owned launch needs a new evidence directory.' }
    if (@(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'").Count) { throw 'An Editor is already running; no owned test Editor launched.' }
}
$env:AURORAVIEW_UNITY_EVIDENCE_DIR = $output
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log = Join-Path $output "unity-$Mode.log"
$arguments = @('-projectPath', $project, '-logFile', $log)
if ($Mode -eq 'owned') {
    $arguments += @('-auroraviewOwnedTest', $RunId, '-executeMethod', 'AuroraView.Unity.AgentEndpoint.Enable')
} elseif ($Mode -eq 'test') {
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
$windowStyle = if ($Mode -eq 'owned') { 'Normal' } else { 'Hidden' }
$process = Start-Process -FilePath $editor -ArgumentList ($arguments | ForEach-Object { '"' + $_ + '"' }) -PassThru -WindowStyle $windowStyle
if ($Mode -eq 'owned') {
    $owner = [ordered]@{ processId = $process.Id; processCreationFileTime = $process.StartTime.ToFileTimeUtc().ToString([Globalization.CultureInfo]::InvariantCulture); projectPath = [IO.Path]::GetFullPath($project); runId = $RunId }
    $owner | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'owned-editor-owner.json') -Encoding UTF8
    [ordered]@{ editorOwner = $owner; executable = $editor; arguments = $arguments; log = $log; exitObserved = $false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'owned-editor-launch.json') -Encoding UTF8
    Write-Output ("Owned Editor launched; retain its process handle and verify real exit separately: " + $process.Id)
    return
}
if ($Mode -in @('agent','core')) {
    $ready = Join-Path $output 'unity-agent-ready.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(240)
    while (-not (Test-Path -LiteralPath $ready)) {
        if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "Unity agent endpoint did not initialize. See $log." }
        Start-Sleep -Milliseconds 250
    }
    $receipt = Get-Content -LiteralPath $ready -Raw | ConvertFrom-Json
    if ($process.HasExited -or $receipt.processId -ne $process.Id) { throw 'Agent readiness does not belong to the newly launched live Editor. No mutation was sent.' }
    if ($Mode -eq 'core') {
        $python = $env:AURORAVIEW_CORE_PYTHON
        if (-not $python) { $python = Join-Path $root 'build~/core-venv/Scripts/python.exe' }
        vx uv run --no-project --no-sync -- $python (Join-Path $root 'agent/core_live_test.py') --pid $receipt.processId --node $Node --state-dir (Join-Path $output 'state') --output (Join-Path $output 'mcp-live-result.json')
    } else {
        vx node (Join-Path $root 'agent/live-test.mjs') --pid $receipt.processId --output (Join-Path $output 'mcp-live-result.json')
    }
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
