$ErrorActionPreference = 'Stop'
$version = '1.0.3537.50'
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root 'build~/deps'
$destination = Join-Path $directory 'webview2'
$receipt = Join-Path $destination 'verified.json'
if (Test-Path -LiteralPath $receipt) {
    $previous = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    if ($previous.version -eq $version -and $previous.verification -in @('NuGet catalog SHA512','Microsoft author signature') -and (Test-Path -LiteralPath (Join-Path $destination 'build/native/include/WebView2.h'))) { return }
}
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$leaf = Invoke-RestMethod -Uri "https://api.nuget.org/v3/registration5-gz-semver2/microsoft.web.webview2/$version.json"
$catalogReference = $leaf.catalogEntry
$catalog = $null
try {
    if ($catalogReference -is [string]) { $catalog = Invoke-RestMethod -Uri $catalogReference }
    elseif ($catalogReference.packageHash) { $catalog = $catalogReference }
    elseif ($catalogReference.'@id') { $catalog = Invoke-RestMethod -Uri $catalogReference.'@id' }
} catch { Write-Warning 'Official NuGet catalog entry is unavailable; require a trusted Microsoft author signature instead.' }
$url = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$version/microsoft.web.webview2.$version.nupkg"
$package = Join-Path $directory "microsoft.web.webview2.$version.nupkg"
Invoke-WebRequest -Uri $url -OutFile $package
if ($catalog.packageHashAlgorithm -eq 'SHA512' -and $catalog.packageHash) {
    $stream = [IO.File]::OpenRead($package)
    $algorithm = [Security.Cryptography.SHA512]::Create()
    try { $bytes = $algorithm.ComputeHash($stream) }
    finally { $algorithm.Dispose(); $stream.Dispose() }
    if ([Convert]::ToBase64String($bytes) -cne $catalog.packageHash) { throw 'WebView2 SDK does not match the publisher catalog SHA512.' }
    $verification = 'NuGet catalog SHA512'
} else {
    $savedErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $signature = vx dotnet nuget verify --all $package 2>&1
    $signatureExit = $LASTEXITCODE
    $ErrorActionPreference = $savedErrorAction
    $signature | Set-Content -LiteralPath (Join-Path $directory 'webview2-signature.txt') -Encoding utf8
    if ($signatureExit -ne 0 -or ($signature -join "`n") -notmatch 'Microsoft Corporation') { throw 'WebView2 SDK requires a valid trusted Microsoft author signature. See build~/deps/webview2-signature.txt.' }
    $verification = 'Microsoft author signature'
}
$archive = Join-Path $directory "microsoft.web.webview2.$version.zip"
Copy-Item -LiteralPath $package -Destination $archive -Force
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
@{ version = $version; source = $url; verification = $verification; catalog = $catalogReference; hash = $catalog.packageHash } | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding utf8
Write-Output "Verified Microsoft.Web.WebView2 ${version}: $verification."
