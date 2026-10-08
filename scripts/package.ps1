$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'build/package'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$files = @('package.json','Editor','agent','README.md','README.zh-CN.md','LICENSE','THIRD_PARTY.md')
$stage = Join-Path $output 'com.auroraview.unity'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage -Recurse -Force }
$sdkLicense = Join-Path $root 'build/deps/webview2/LICENSE.txt'
if (-not (Test-Path -LiteralPath $sdkLicense)) { throw 'Verified WebView2 SDK license is missing.' }
Copy-Item -LiteralPath $sdkLicense -Destination (Join-Path $stage 'WEBVIEW2-LICENSE.txt')
$archive = Join-Path $output 'auroraview-unity-0.1.0.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List Algorithm,Hash,Path
