param([ValidateSet('test','accept','agent','core','owned','compile')][string]$Mode = 'test', [string]$Node = 'node', [string]$RunId, [string]$CandidateReceipt, [string]$ProjectPath)
$ErrorActionPreference = 'Stop'
function Resolve-UnityPath([string]$Path) {
    $windowsPath = $Path.Replace('/', '\')
    if ([string]::IsNullOrWhiteSpace($Path) -or
        ($windowsPath -notmatch '^[A-Za-z]:\\' -and $windowsPath -notmatch '^\\\\[^\\]+\\[^\\]+(?:\\|$)') -or
        $windowsPath.StartsWith('\\?\') -or $windowsPath.StartsWith('\\.\') -or $windowsPath.StartsWith('\??\')) {
        throw 'Unity path must be an ordinary absolute drive or UNC path.'
    }
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Get-UnityFileSha256([string]$Path) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}

function Read-UnityArguments([string]$CommandLine) {
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { throw 'Running Editor command line is unavailable; launch conflict is unknown.' }
    if (-not ('UnityLaunchArguments' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class UnityLaunchArguments {
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    public static string[] Parse(string commandLine) {
        int count;
        IntPtr memory = CommandLineToArgvW(commandLine, out count);
        if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            string[] result = new string[count];
            for (int index = 0; index < count; ++index)
                result[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, index * IntPtr.Size));
            return result;
        } finally { LocalFree(memory); }
    }
}
"@
    }
    return [UnityLaunchArguments]::Parse($CommandLine)
}

function Assert-UnityLaunchAvailable([object[]]$Processes, [string]$Project, [string]$Run) {
    $expectedProject = Resolve-UnityPath $Project
    foreach ($running in $Processes) {
        if (-not $running -or ($running.ProcessId -isnot [int] -and $running.ProcessId -isnot [uint32] -and $running.ProcessId -isnot [long]) -or
            $running.ProcessId -lt 1 -or $running.CommandLine -isnot [string]) { throw 'Running Editor metadata is unavailable; launch conflict is unknown.' }
        $tokens = @(Read-UnityArguments $running.CommandLine)
        $projectFlags = @(for ($index = 1; $index -lt $tokens.Count; ++$index) {
            if ([string]::Equals($tokens[$index], '-projectPath', [StringComparison]::OrdinalIgnoreCase)) { $index }
        })
        $runFlags = @(for ($index = 1; $index -lt $tokens.Count; ++$index) {
            if ([string]::Equals($tokens[$index], '-auroraviewOwnedTest', [StringComparison]::OrdinalIgnoreCase)) { $index }
        })
        if ($projectFlags.Count -ne 1 -or $projectFlags[0] + 1 -ge $tokens.Count -or $runFlags.Count -gt 1) {
            throw 'Running Editor project or owned-instance arguments are ambiguous; no Editor launched.'
        }
        $runningProject = Resolve-UnityPath $tokens[$projectFlags[0] + 1]
        if ([string]::Equals($runningProject, $expectedProject, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'An Editor already owns the requested project; no owned test Editor launched.'
        }
        if ($runFlags.Count) {
            if ($runFlags[0] + 1 -ge $tokens.Count -or $tokens[$runFlags[0] + 1] -cnotmatch '^[a-f0-9]{32}$') {
                throw 'Running Editor owned-instance arguments are invalid; no Editor launched.'
            }
            if ($tokens[$runFlags[0] + 1] -ceq $Run) { throw 'An Editor already owns the requested run ID; no owned test Editor launched.' }
        }
    }
}

function Assert-OwnedProject([string]$Root, [string]$Project, [string]$Output, [object]$Candidate, [bool]$ExplicitProject) {
    $bound = $ExplicitProject -or $null -ne $Candidate.project_path -or $null -ne $Candidate.evidence_dir -or
        $null -ne $Candidate.project_manifest_sha256 -or $null -ne $Candidate.project_version_sha256
    if (-not $bound) { return }
    if ($Candidate.project_path -isnot [string] -or $Candidate.evidence_dir -isnot [string] -or
        $Candidate.project_manifest_sha256 -isnot [string] -or $Candidate.project_manifest_sha256 -cnotmatch '^[a-f0-9]{64}$' -or
        $Candidate.project_version_sha256 -isnot [string] -or $Candidate.project_version_sha256 -cnotmatch '^[a-f0-9]{64}$' -or
        -not [string]::Equals((Resolve-UnityPath $Candidate.project_path), $Project, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals((Resolve-UnityPath $Candidate.evidence_dir), $Output, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Owned project and evidence paths must match the reviewed candidate receipt.'
    }
    $manifestPath = Join-Path $Project 'Packages/manifest.json'
    $versionPath = Join-Path $Project 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath (Join-Path $Project 'Assets') -PathType Container) -or
        (Get-UnityFileSha256 $manifestPath) -cne $Candidate.project_manifest_sha256 -or
        (Get-UnityFileSha256 $versionPath) -cne $Candidate.project_version_sha256) {
        throw 'Owned project configuration differs from the reviewed candidate receipt.'
    }
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    $dependency = $manifest.dependencies.'com.auroraview.unity'
    if ($dependency -isnot [string] -or -not $dependency.StartsWith('file:', [StringComparison]::Ordinal) -or $dependency.Length -le 5) {
        throw 'Owned project must reference this reviewed local package.'
    }
    $packagePath = $dependency.Substring(5)
    if (-not [IO.Path]::IsPathRooted($packagePath)) { $packagePath = Join-Path (Join-Path $Project 'Packages') $packagePath }
    if (-not [string]::Equals((Resolve-UnityPath $packagePath), (Resolve-UnityPath $Root), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Owned project resolves a different package source.'
    }
}

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
$project = if ($ProjectPath) { Resolve-UnityPath $ProjectPath } else { [IO.Path]::GetFullPath((Join-Path $root 'Samples~/SceneTools')) }
$output = $env:AURORAVIEW_UNITY_EVIDENCE_DIR
if (-not $output) { $output = Join-Path $root 'build~/evidence'; if ($Mode -eq 'core') { $output = Join-Path $output 'core' } }
if ($Mode -eq 'owned') {
    if (-not $CandidateReceipt) { throw 'Owned launch requires the controller-reviewed exact-head CI and native DLL receipt.' }
    $candidate = Get-Content -LiteralPath $CandidateReceipt -Raw | ConvertFrom-Json
    if ($candidate.source_commit -isnot [string] -or $candidate.source_commit.Length -ne 40 -or
        $candidate.source_commit -cnotmatch '^[0-9a-f]{40}$' -or $candidate.exact_head_ci -cne 'success' -or
        $candidate.native_dll_sha256 -isnot [string] -or $candidate.native_dll_sha256.Length -ne 64 -or
        $candidate.native_dll_sha256 -cnotmatch '^[0-9a-f]{64}$' -or
        ($candidate.native_dll_artifact_id -isnot [long] -and $candidate.native_dll_artifact_id -isnot [int]) -or
        $candidate.native_dll_artifact_id -lt 1) { throw 'Incomplete candidate source/CI/artifact identity.' }
    $sourceHead = @(& vx --cache-mode offline --no-auto-install git -C $root rev-parse HEAD | Where-Object { $_ -match '^[0-9a-f]{40}$' })
    if ($LASTEXITCODE -ne 0 -or $sourceHead.Count -ne 1 -or $sourceHead[0] -cne $candidate.source_commit) { throw 'Owned launch source differs from the reviewed CI receipt.' }
    $changes = @(& vx --cache-mode offline --no-auto-install git -C $root status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $changes.Count) { throw 'Owned launch requires the unchanged reviewed source tree.' }
    $dll = Get-UnityFileSha256 (Join-Path $root 'Editor/Plugins/x86_64/auroraview_unity.dll')
    if ($dll -cne $candidate.native_dll_sha256) { throw 'Owned launch native DLL differs from the reviewed artifact.' }
    if (-not $env:AURORAVIEW_UNITY_EVIDENCE_DIR) { $output = Join-Path $output ('owned-' + $RunId) }
    $project = Resolve-UnityPath $project
    $output = Resolve-UnityPath $output
    Assert-OwnedProject $root $project $output $candidate ([bool]$ProjectPath)
    if (Test-Path -LiteralPath $output) { throw 'Owned launch needs a new evidence directory.' }
    Assert-UnityLaunchAvailable @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop) $project $RunId
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
    [ordered]@{ editorOwner = $owner; executable = $editor; arguments = $arguments; log = $log; sourceCommit = $sourceHead[0]; nativeDllSha256 = $dll; nativeDllArtifactId = $candidate.native_dll_artifact_id; candidateReceipt = [IO.Path]::GetFullPath($CandidateReceipt); evidenceDirectory = $output; projectManifestSha256 = $candidate.project_manifest_sha256; projectVersionSha256 = $candidate.project_version_sha256; exitObserved = $false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'owned-editor-launch.json') -Encoding UTF8
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
