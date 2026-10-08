# Validation ledger

This ledger records independent gates. Source availability, native compilation, real WebView2, Unity runtime, docking interaction, CI and release publication are separate claims.

| Gate | Reproduction | Initial status |
|---|---|---|
| Official AuroraView JS bridge interoperability | `vx just test` | Passed locally; 9 Node tests across bridge and MCP |
| Explicit MCP discovery, mapping and refusal | `vx just test` | Passed locally; preview adapter only |
| Publisher-verified WebView2 SDK acquisition | `vx just fetch-sdk` | Passed; trusted Microsoft author signature |
| Windows x64 native DLL | `vx just build` | Passed locally; MSVC 19.44 |
| Real WebView2 child HWND, IPC and STA close | `vx just test-native` | Passed locally; real runtime and clean child HWND shutdown |
| Unity main-thread scene, selection, refusal and Undo | `vx just test-unity` | Pending; restricted command wrapper blocked before Editor launch |
| Unity native parent HWND + browser→context→browser + scene/Undo | `vx just accept-unity` | Pending licensed Editor execution |
| MCP stdio→named pipe→real Editor scene context/mutation | Enable endpoint; invoke the three tools against explicit PID | Pending live transport execution |
| Dock/undock, tab switches, resize, keyboard/mouse, high DPI | Manual project dcc-cua acceptance | Pending native UI acceptance |
| CI on published commit | GitHub Actions run | Pending publication |
| Release native ZIP and public asset readback | Release workflow and asset SHA256 | Pending publication |

Local prerequisite inspection on 2026-10-08 found Unity 2022.3.62f3c1, a Unity license file and Visual Studio 2022 Community. File existence does not validate license activation or Editor startup. No license contents are read or published.

The official NuGet catalog URL for SDK 1.0.3537.50 returned 404. The package was instead verified with `dotnet nuget verify --all`: the trusted Microsoft author and NuGet repository signatures passed. No locally observed hash was substituted for publisher verification.

The successful native smoke run reported `PASS: real WebView2 child HWND, upstream bridge, inbound/outbound IPC, STA shutdown` and exited 0. A later invocation through a restricted local command wrapper failed before running the recipe because vx could not write its metrics/cache state and reported no offline bundle. That wrapper failure does not invalidate the completed native runtime test and does not establish a Unity Editor failure. No Unity host PID was launched by that failed invocation. Editor acceptance remains open until a permitted command environment executes the recipes below.

`test-unity` emits `build/evidence/editmode.xml` and a Unity log. `accept-unity` launches a non-batch Editor project and emits `build/evidence/unity-acceptance.json`, including Editor version, PID, main-thread ID, native parent HWND, browser round trip, scene mutation and Undo. It returns failure if required fields do not pass. It does not certify visual docking, performance, multi-monitor input or DCC-MCP registry integration.

The acceptance project is isolated from a user's project and generated files are ignored. No runtime player, macOS or Linux support is claimed.
