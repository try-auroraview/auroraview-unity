$ErrorActionPreference = 'Stop'
if ($env:AURORAVIEW_CORE_PYTHON) {
    if (-not (Test-Path -LiteralPath $env:AURORAVIEW_CORE_PYTHON)) { throw 'AURORAVIEW_CORE_PYTHON must name an existing interpreter with the locked dependencies installed.' }
    return
}
$root = Split-Path -Parent $PSScriptRoot
$environment = Join-Path $root 'build/core-venv'
vx uv venv --allow-existing --python 3.11 $environment
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
vx uv pip sync --python (Join-Path $environment 'Scripts/python.exe') --require-hashes (Join-Path $root 'agent/requirements-core.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
