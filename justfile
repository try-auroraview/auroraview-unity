set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]
core_python := env_var_or_default("AURORAVIEW_CORE_PYTHON", justfile_directory() / "build/core-venv/Scripts/python.exe")

default:
    @vx just --list

fetch-sdk:
    powershell.exe -NoProfile -File scripts/fetch-sdk.ps1

build: fetch-sdk
    vx cmake -S native -B build/native -A x64 -DWEBVIEW2_SDK={{justfile_directory()}}/build/deps/webview2
    vx cmake --build build/native --config Release

test:
    vx node --test tests/*.test.mjs

core-env:
    powershell.exe -NoProfile -File scripts/core-env.ps1

test-core: core-env
    vx uv run --no-project --no-sync -- "{{core_python}}" -m unittest discover -s tests -p test_core.py -v

test-core-http: core-env
    $env:DCC_MCP_DISABLE_DEFAULT_SKILL_PATHS = '1'; $env:DCC_MCP_CHECKPOINT_IN_MEMORY = '1'; vx uv run --no-project --no-sync -- "{{core_python}}" -m unittest discover -s tests -p test_core_http.py -v

serve-core pid state_dir node='node': core-env
    vx uv run --no-project --no-sync -- "{{core_python}}" agent/core_server.py --pid {{pid}} --state-dir "{{state_dir}}" --node "{{node}}"

test-native: build
    build/native/Release/auroraview_native_test.exe

test-unity:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode test

accept-unity:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode accept

accept-agent:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode agent

accept-core node='node': core-env
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode core -Node "{{node}}"

check: test test-core test-core-http

package: test-native
    powershell.exe -NoProfile -File scripts/package.ps1
