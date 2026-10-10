set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]
core_python := env_var_or_default("AURORAVIEW_CORE_PYTHON", justfile_directory() / "build~/core-venv/Scripts/python.exe")

default:
    @vx just --list

fetch-sdk:
    powershell.exe -NoProfile -File scripts/fetch-sdk.ps1

build: fetch-sdk
    vx cmake -S native -B build~/native -A x64 -DWEBVIEW2_SDK={{justfile_directory()}}/build~/deps/webview2
    vx cmake --build build~/native --config Release

test:
    vx node --test tests/*.test.mjs
    vx just test-editor-owner

test-editor-owner:
    New-Item -ItemType Directory -Force -Path 'build~/owner-check' | Out-Null; $check = Join-Path $PWD 'build~/owner-check/EditorOwnerCheck.exe'; vx uv run --offline --no-project --no-sync -- 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /warnaserror+ /r:System.Web.Extensions.dll "/out:$check" (Join-Path $PWD 'Editor/EditorOwner.cs') (Join-Path $PWD 'Editor/EditorStatus.cs') (Join-Path $PWD 'Editor/ContextResponse.cs') (Join-Path $PWD 'Editor/SceneContracts.cs') (Join-Path $PWD 'Editor/OwnedEditorExit.cs') (Join-Path $PWD 'tests~/EditorOwnerCheck.cs'); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; vx uv run --offline --no-project --no-sync -- $check -projectPath (Join-Path $PWD 'owned-project') -auroraviewOwnedTest ('a' * 32); exit $LASTEXITCODE

core-env:
    powershell.exe -NoProfile -File scripts/core-env.ps1

test-core: core-env
    vx uv run --no-project --no-sync -- "{{core_python}}" -m unittest discover -s tests -p test_core.py -v

test-core-service: core-env
    vx uv run --no-project --no-sync -- "{{core_python}}" -m unittest discover -s tests -p test_core_service.py -v

test-core-http: core-env
    $env:DCC_MCP_DISABLE_DEFAULT_SKILL_PATHS = '1'; $env:DCC_MCP_CHECKPOINT_IN_MEMORY = '1'; vx uv run --no-project --no-sync -- "{{core_python}}" -m unittest discover -s tests -p test_core_http.py -v

serve-core pid state_dir node='node': core-env
    vx uv run --no-project --no-sync -- "{{core_python}}" agent/core_server.py --pid {{pid}} --state-dir "{{state_dir}}" --node "{{node}}"

serve-owned-core pid state_dir owner_file node='node': core-env
    vx uv run --no-project --no-sync -- "{{core_python}}" agent/core_server.py --pid {{pid}} --state-dir "{{state_dir}}" --node "{{node}}" --editor-owner-file "{{owner_file}}"

test-native: build
    build~/native/Release/auroraview_native_test.exe

test-unity:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode test

compile-unity:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode compile

launch-owned-editor run_id candidate_receipt:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode owned -RunId "{{run_id}}" -CandidateReceipt "{{candidate_receipt}}"

exit-owned-editor owner_file node='node': core-env
    vx uv run --no-project --no-sync -- "{{core_python}}" agent/core.py --owner-file "{{owner_file}}" --node "{{node}}"

accept-unity:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode accept

accept-agent:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode agent

accept-core node='node': core-env
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode core -Node "{{node}}"

check: test test-core test-core-service test-core-http

package: test-native
    powershell.exe -NoProfile -File scripts/package.ps1

test-node version='22.23.3':
    vx node@{{version}} --test tests/agent.test.mjs
