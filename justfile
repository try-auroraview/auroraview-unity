set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]

default:
    @vx just --list

fetch-sdk:
    powershell.exe -NoProfile -File scripts/fetch-sdk.ps1

build: fetch-sdk
    vx cmake -S native -B build/native -A x64 -DWEBVIEW2_SDK={{justfile_directory()}}/build/deps/webview2
    vx cmake --build build/native --config Release

test:
    vx node --test tests/*.test.mjs

test-native: build
    build/native/Release/auroraview_native_test.exe

test-unity:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode test

accept-unity:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode accept

accept-agent:
    powershell.exe -NoProfile -File scripts/unity.ps1 -Mode agent

check: test

package: test-native
    powershell.exe -NoProfile -File scripts/package.ps1
