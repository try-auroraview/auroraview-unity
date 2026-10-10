param([switch]$RecipeOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($RecipeOnly) {
    # Execute the real recipe against a disposable fake entry, never the launcher.
    $fixture = Join-Path $root ('build~/launcher-check/recipe-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'scripts') | Out-Null
    $mock = @"
param([string]`$Mode, [string]`$RunId, [string]`$CandidateReceipt, [string]`$ProjectPath)
`$result = [ordered]@{
    mode = `$Mode
    runId = `$RunId
    candidateReceipt = `$CandidateReceipt
    projectPathBound = `$PSBoundParameters.ContainsKey('ProjectPath')
    projectPath = `$ProjectPath
    nativeArgv = [Environment]::GetCommandLineArgs()
}
[IO.File]::WriteAllText(`$env:UNITY_LAUNCHER_ARGV_OUTPUT, (`$result | ConvertTo-Json -Depth 4))
Write-Output 'Fake entry completed; no launcher, CIM or Unity invoked.'
exit [int]`$env:UNITY_LAUNCHER_ARGV_EXIT
"@
    [IO.File]::WriteAllText((Join-Path $fixture 'scripts/unity.ps1'), $mock, (New-Object Text.UTF8Encoding($false)))
    $run = 'a' * 32
    $candidate = Join-Path $fixture 'candidate receipt.json'
    $project = Join-Path $fixture 'independent project'
    [IO.File]::WriteAllText($candidate, '{}')
    $vx = (Get-Command vx -CommandType Application | Select-Object -First 1).Source
    $cases = @(
        [pscustomobject]@{ name = 'old-two-parameters'; explicitProject = $false; exitCode = 0 },
        [pscustomobject]@{ name = 'new-three-parameters'; explicitProject = $true; exitCode = 23 }
    )
    foreach ($case in $cases) {
        $output = Join-Path $fixture ($case.name + '.entry.json')
        $arguments = @('--cache-mode', 'offline', '--no-auto-install', 'just', '--justfile',
            (Join-Path $root 'justfile'), '--working-directory', $fixture, 'launch-owned-editor', $run, $candidate)
        if ($case.explicitProject) { $arguments += $project }
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $vx
        $start.Arguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
        $start.WorkingDirectory = $fixture
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.EnvironmentVariables['UNITY_LAUNCHER_ARGV_OUTPUT'] = $output
        $start.EnvironmentVariables['UNITY_LAUNCHER_ARGV_EXIT'] = [string]$case.exitCode
        $process = New-Object Diagnostics.Process
        $process.StartInfo = $start
        $stdoutPath = Join-Path $fixture ($case.name + '.stdout.txt')
        $stderrPath = Join-Path $fixture ($case.name + '.stderr.txt')
        $stdout = [IO.File]::Create($stdoutPath)
        $stderr = [IO.File]::Create($stderrPath)
        try {
            if (-not $process.Start()) { throw 'Fake recipe process failed to start.' }
            $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
            $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderr)
            if (-not $process.WaitForExit(35000)) { throw 'Fake recipe timed out; no retry or force was attempted.' }
            if (-not $stdoutTask.Wait(5000) -or -not $stderrTask.Wait(5000)) { throw 'Fake recipe output drain timed out.' }
            $stdoutTask.GetAwaiter().GetResult()
            $stderrTask.GetAwaiter().GetResult()
            $actualExit = $process.ExitCode
        } finally {
            $stdout.Dispose()
            $stderr.Dispose()
            $process.Dispose()
        }
        $record = [ordered]@{ case = $case.name; argv = $arguments; actualExitCode = $actualExit; expectedExitCode = $case.exitCode;
            stdout = $stdoutPath; stderr = $stderrPath; fakeEntryReceipt = $output; actualUnityOrCimCalls = 0 }
        [IO.File]::WriteAllText((Join-Path $fixture ($case.name + '.actual.json')), ($record | ConvertTo-Json -Depth 4))
        if ($actualExit -ne $case.exitCode -or -not (Test-Path -LiteralPath $output)) { throw 'Fake entry failed or its exit code was not propagated.' }
        $entry = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        if ($entry.mode -cne 'owned' -or $entry.runId -cne $run -or $entry.candidateReceipt -cne $candidate -or
            $entry.projectPathBound -ne $case.explicitProject -or
            ($case.explicitProject -and $entry.projectPath -cne $project)) { throw 'Actual recipe arguments differ from the requested entry parameters.' }
        $projectFlags = @($entry.nativeArgv | Where-Object { $_ -ceq '-ProjectPath' })
        if ($projectFlags.Count -ne [int]$case.explicitProject) { throw 'Native ProjectPath argument presence is incorrect.' }
        Write-Output ("PASS actual recipe " + $case.name + "; actual exit " + $actualExit + "; " + $output)
    }
    Write-Output 'Unity launcher recipe checks passed: 2; actual launcher, CIM and Unity calls: 0.'
    return
}
$launcher = Join-Path $root 'scripts/unity.ps1'
$parseErrors = $null
$tokens = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($launcher, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Launcher syntax is invalid.' }
# Import only the production pure guards. Never run launcher entry, CIM or Start-Process.
$names = @('Resolve-UnityPath', 'Get-UnityFileSha256', 'Read-UnityArguments', 'Assert-UnityLaunchAvailable', 'Assert-OwnedProject')
foreach ($name in $names) {
    $definitions = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }.GetNewClosure(), $true))
    if ($definitions.Count -ne 1) { throw "Missing unique production guard: $name" }
    . ([scriptblock]::Create($definitions[0].Extent.Text))
}
function Get-CimInstance { throw 'Real process queries are forbidden in the offline launcher check.' }
function Start-Process { throw 'Process launches are forbidden in the offline launcher check.' }
$script:passed = 0
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    ++$script:passed
    Write-Output ("PASS " + $Name)
}
function Refuses([scriptblock]$Action) {
    $refused = $false
    try { & $Action } catch { $refused = $true }
    if (-not $refused) { throw 'Expected guard refusal.' }
}
function Process([string]$CommandLine) { return [pscustomobject]@{ ProcessId = [uint32]42; CommandLine = $CommandLine } }
$run = 'a' * 32
$otherRun = 'b' * 32
$project = 'F:\Projects\owned'
Check 'no running Editor' { Assert-UnityLaunchAvailable @() $project $run }
Check 'ordinary unrelated Editor with spaces' {
    Assert-UnityLaunchAvailable @((Process '"C:\Program Files\Unity\Unity.exe" -projectPath "F:\Other Projects\scene" -logFile "F:\logs\owned.log"')) $project $run
}
Check 'distinct owned instance and project' {
    Assert-UnityLaunchAvailable @((Process ('Unity.exe -projectPath F:\Projects\other -auroraviewOwnedTest ' + $otherRun))) $project $run
}
Check 'multiple unrelated Editors' {
    Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath F:\Projects\one'), (Process 'Unity.exe -PROJECTPATH F:/Projects/two')) $project $run
}
Check 'argv boundaries ignore flag text inside another argument' {
    Assert-UnityLaunchAvailable @((Process '"Unity.exe" -projectPath "F:\Projects\other" -logFile "-projectPath F:\Projects\owned"')) $project $run
}
Check 'non-ASCII unrelated project' {
    $unicode = [string][char]0x72ec + [char]0x7acb + [char]0x573a + [char]0x666f
    Assert-UnityLaunchAvailable @((Process ('Unity.exe -projectPath "F:\Projects\' + $unicode + '"'))) $project $run
}
Check 'ordinary UNC project' {
    Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath "\\server\share\other"')) '\\server\share\owned' $run
}
Check 'forward-slash UNC project' {
    Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath "//server/share/other"')) '\\server\share\owned' $run
}
Check 'same project' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath F:\Projects\owned')) $project $run } }
Check 'same canonical project case slashes dot segments and trailing slash' {
    Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -PROJECTPATH "f:/Projects/other/../OWNED/"')) $project $run }
}
Check 'same UNC project' {
    Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath "//SERVER/share/other/../owned/"')) '\\server\share\owned' $run }
}
Check 'same run on different project' {
    Refuses { Assert-UnityLaunchAvailable @((Process ('Unity.exe -projectPath F:\Projects\other -auroraviewOwnedTest ' + $run))) $project $run }
}
Check 'conflict after unrelated peer' {
    Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath F:\Projects\other'), (Process 'Unity.exe -projectPath F:\Projects\owned')) $project $run }
}
Check 'missing process metadata' { Refuses { Assert-UnityLaunchAvailable @([pscustomobject]@{ ProcessId = 42 }) $project $run } }
Check 'empty command line' { Refuses { Assert-UnityLaunchAvailable @((Process '')) $project $run } }
Check 'missing project flag' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -logFile F:\log.txt')) $project $run } }
Check 'duplicate project flag' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath F:\Projects\other -PROJECTPATH F:\Projects\else')) $project $run } }
Check 'missing project value' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath')) $project $run } }
Check 'relative project is unknown' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath other')) $project $run } }
Check 'drive-relative project is unknown' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath F:other')) $project $run } }
Check 'unsupported device project is unknown' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath "\\?\F:\Projects\other"')) $project $run } }
Check 'duplicate owned run flag' { Refuses { Assert-UnityLaunchAvailable @((Process ('Unity.exe -projectPath F:\Projects\other -auroraviewOwnedTest ' + $otherRun + ' -auroraviewOwnedTest ' + $run))) $project $run } }
Check 'missing owned run value' { Refuses { Assert-UnityLaunchAvailable @((Process 'Unity.exe -projectPath F:\Projects\other -auroraviewOwnedTest')) $project $run } }
Check 'invalid PID metadata' { Refuses { Assert-UnityLaunchAvailable @([pscustomobject]@{ ProcessId = '42'; CommandLine = 'Unity.exe -projectPath F:\Projects\other' }) $project $run } }

# These are disposable text-only fixtures, not Unity projects that can be launched.
$fixture = Join-Path $root ('build~/launcher-check/fixtures-' + [guid]::NewGuid().ToString('N'))
$fakeSource = Join-Path $fixture 'source'
$fakeProject = Join-Path $fixture 'project'
$output = Resolve-UnityPath (Join-Path $fixture 'new-evidence')
foreach ($directory in @($fakeSource, (Join-Path $fakeProject 'Assets'), (Join-Path $fakeProject 'Packages'), (Join-Path $fakeProject 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$manifest = Join-Path $fakeProject 'Packages/manifest.json'
$version = Join-Path $fakeProject 'ProjectSettings/ProjectVersion.txt'
[IO.File]::WriteAllText($manifest, '{"dependencies":{"com.auroraview.unity":"file:../../source"}}')
[IO.File]::WriteAllText($version, 'm_EditorVersion: fake')
$fakeProject = Resolve-UnityPath $fakeProject
$receipt = [pscustomobject]@{
    project_path = $fakeProject
    evidence_dir = $output
    project_manifest_sha256 = Get-UnityFileSha256 $manifest
    project_version_sha256 = Get-UnityFileSha256 $version
}
Check 'reviewed independent project paths hashes and local source' { Assert-OwnedProject $fakeSource $fakeProject $output $receipt $true }
Check 'old default entry receipt remains compatible' { Assert-OwnedProject $fakeSource $fakeProject $output ([pscustomobject]@{}) $false }
Check 'explicit project requires bound receipt' { Refuses { Assert-OwnedProject $fakeSource $fakeProject $output ([pscustomobject]@{}) $true } }
Check 'project receipt mismatch' { Refuses { Assert-OwnedProject $fakeSource ($fakeProject + '-other') $output $receipt $true } }
Check 'output receipt mismatch' { Refuses { Assert-OwnedProject $fakeSource $fakeProject ($output + '-other') $receipt $true } }
Check 'receipt configuration hash mismatch' {
    $saved = $receipt.project_version_sha256
    $receipt.project_version_sha256 = '0' * 64
    Refuses { Assert-OwnedProject $fakeSource $fakeProject $output $receipt $true }
    $receipt.project_version_sha256 = $saved
}
Check 'different local package source refused even with its hash' {
    [IO.File]::WriteAllText($manifest, '{"dependencies":{"com.auroraview.unity":"file:../../elsewhere"}}')
    $receipt.project_manifest_sha256 = Get-UnityFileSha256 $manifest
    Refuses { Assert-OwnedProject $fakeSource $fakeProject $output $receipt $true }
}
$source = [IO.File]::ReadAllText($launcher)
Check 'entry retains source CI DLL fresh output and owner protections' {
    foreach ($required in @('$candidate.exact_head_ci -cne ''success''', '$sourceHead[0] -cne $candidate.source_commit',
        'git -C $root status --porcelain', '$dll -cne $candidate.native_dll_sha256',
        'if (Test-Path -LiteralPath $output)', 'Assert-UnityLaunchAvailable @(Get-CimInstance',
        'processCreationFileTime = $process.StartTime.ToFileTimeUtc()', 'projectPath = [IO.Path]::GetFullPath($project)',
        'Assert-OwnedProject $root $project $output $candidate ([bool]$ProjectPath)')) {
        if (-not $source.Contains($required)) { throw "Entry protection missing: $required" }
    }
}
Write-Output ("Unity launcher offline checks passed: " + $script:passed + "; actual process queries and launches: 0.")