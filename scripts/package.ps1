$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$nativeDll = Join-Path $root 'build~/native/Release/auroraview_unity.dll'
if (-not (Test-Path -LiteralPath $nativeDll -PathType Leaf)) { throw 'Built native DLL is missing. Run vx just build before packaging.' }
$output = Join-Path $root 'build~/package'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$files = @('package.json','Editor','agent','docs','README.md','README.zh-CN.md','LICENSE','THIRD_PARTY.md','Samples~/DccMcpSceneTools')
$stage = Join-Path $output 'com.auroraview.unity'
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $expectedStage = [IO.Path]::GetFullPath((Join-Path $root 'build~/package/com.auroraview.unity'))
    if ($resolvedStage -ne $expectedStage) { throw 'Package stage is outside the expected build directory.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
function Copy-PackageItem([string]$Source, [string]$Destination) {
    if (Test-Path -LiteralPath $Source -PathType Container) {
        if ([IO.Path]::GetFileName($Source) -eq '__pycache__') { return }
        New-Item -ItemType Directory -Force -Path $Destination | Out-Null
        foreach ($child in Get-ChildItem -LiteralPath $Source) {
            Copy-PackageItem $child.FullName (Join-Path $Destination $child.Name)
        }
    } else {
        $name = [IO.Path]::GetFileName($Source)
        if ($name -eq '__pycache__.meta' -or $name -match '\.py[co](\.meta)?$') { return }
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}
foreach ($file in $files) { Copy-PackageItem (Join-Path $root $file) (Join-Path $stage $file) }
$stagedDll = Join-Path $stage 'Editor/Plugins/x86_64/auroraview_unity.dll'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $stagedDll) | Out-Null
Copy-Item -LiteralPath $nativeDll -Destination $stagedDll -Force
$sdkLicense = Join-Path $root 'build~/deps/webview2/LICENSE.txt'
if (-not (Test-Path -LiteralPath $sdkLicense)) { throw 'Verified WebView2 SDK license is missing.' }
Copy-Item -LiteralPath $sdkLicense -Destination (Join-Path $stage 'WEBVIEW2-LICENSE.txt')
$archive = Join-Path $output 'auroraview-unity-0.1.0.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $archive -Force
$stream = [IO.File]::OpenRead($archive)
$algorithm = [Security.Cryptography.SHA256]::Create()
try { $digest = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
finally { $algorithm.Dispose(); $stream.Dispose() }
$digest + '  ' + [IO.Path]::GetFileName($archive) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Output "SHA256 $digest  $archive"
