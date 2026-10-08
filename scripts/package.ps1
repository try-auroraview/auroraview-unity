$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'build/package'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$files = @('package.json','Editor','agent','docs','README.md','README.zh-CN.md','LICENSE','THIRD_PARTY.md')
$stage = Join-Path $output 'com.auroraview.unity'
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $expectedStage = [IO.Path]::GetFullPath((Join-Path $root 'build/package/com.auroraview.unity'))
    if ($resolvedStage -ne $expectedStage) { throw 'Package stage is outside the expected build directory.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage -Recurse -Force }
$sdkLicense = Join-Path $root 'build/deps/webview2/LICENSE.txt'
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
