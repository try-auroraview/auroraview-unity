# Validation ledger

This ledger records independent gates. Source availability, native compilation, real WebView2, Unity runtime, docking interaction, CI and release publication are separate claims.

| Gate | Reproduction | Status |
|---|---|---|
| Official AuroraView JS bridge interoperability | `vx just test` | Passed locally; 9 Node tests across bridge and MCP |
| Explicit MCP discovery, mapping and refusal | `vx just test` | Passed locally; preview adapter only |
| Shared-facade consumer, PID/session rejection, stale calls and timeout bounds | `vx just test-core` | 11 offline tests passed locally against public facade 0.1.0 and Core 0.20.41; fake Unity transport |
| Core HTTP discovery, queue lane, borrowed cleanup and owned shutdown | `vx just test-core-http` | Passed locally on numeric loopback with public dependencies; fake Unity transport |
| Core one-shot named-pipe transport | `vx just test` | Passed locally against a controlled current-user local pipe; no Unity process |
| Core → real Unity session → scene readback/Undo/cleanup | Pending licensed Editor acceptance | Not yet accepted for this candidate |
| Publisher-verified WebView2 SDK acquisition | `vx just fetch-sdk` | Passed; trusted Microsoft author signature |
| Windows x64 native DLL | `vx just build` | Passed locally; MSVC 19.44 |
| Real WebView2 child HWND, IPC and STA close | `vx just test-native` | Passed locally; current-user protected pipe ACL, exclusive creation/reopen, real runtime and clean child HWND shutdown |
| Unity main-thread scene, selection, refusal and Undo | `vx just test-unity` | Passed locally; 7/7 EditMode tests on Unity 2022.3.62f3c1, including endpoint close/reopen |
| Unity native parent HWND + browser→context→browser + scene/Undo | `vx just accept-unity` | Blocked locally: non-batch Editor startup exceeded four minutes without a main HWND or acceptance receipt |
| MCP stdio→named pipe→real Editor scene context/mutation | `vx just accept-agent` | Passed locally on Unity 2022.3.62f3c1: discovery, three tools, object/selection readback, Undo and owned endpoint shutdown |
| Dock/undock, tab switches, resize, keyboard/mouse, high DPI | Manual project dcc-cua acceptance | Pending native UI acceptance |
| CI on published commit | [Per-commit Actions results](https://github.com/try-auroraview/auroraview-unity/actions/workflows/ci.yml) | Bridge digest, native build/smoke and package checks; exact-head success required before prerelease publication |
| Release native ZIP and public asset readback | [Prerelease downloads](https://github.com/try-auroraview/auroraview-unity/releases) | Native ZIPs are accompanied by SHA256 files; publication is separate from native UI acceptance |

Local prerequisite inspection on 2026-10-08 found Unity 2022.3.62f3c1, a Unity license file and Visual Studio 2022 Community. File existence does not validate license activation or Editor startup. No license contents are read or published.

The official NuGet catalog URL for SDK 1.0.3537.50 returned 404. The package was instead verified with `dotnet nuget verify --all`: the trusted Microsoft author and NuGet repository signatures passed. No locally observed hash was substituted for publisher verification.

Local verification on 2026-10-09 passed the extended native smoke: current-user protected DACL, exclusive pipe creation, close/reopen, real WebView2 child HWND, upstream bridge, inbound/outbound IPC and STA shutdown. Unity 2022.3.62f3c1 then passed all seven EditMode tests. The original endpoint reopening failure used Mono's unimplemented managed pipe ACL path; the replacement creates the DACL through Win32 and wraps the handle in the implemented managed stream constructor. `build/evidence/editmode.xml` records the seven passing tests.

The initial published CI run found a changed bridge digest because Windows checkout converted its line endings. `.gitattributes` now disables text conversion for the vendored bridge. Its unchanged local SHA256 remains `4550b027e400c6500fca5e64dd8c50348bde15848c513d2738dd6e443b4a17ff`; CI must verify the same bytes after checkout.

`test-unity` emits `build/evidence/editmode.xml` and a Unity log. `accept-unity` launches a non-batch Editor project and emits `build/evidence/unity-acceptance.json`, including Editor version, PID, main-thread ID, native parent HWND, browser round trip, scene mutation and Undo. It returns failure if required fields do not pass. It does not certify visual docking, performance, multi-monitor input or DCC-MCP registry integration.

The 2026-10-09 non-batch `accept-unity` attempt exceeded the four-minute startup deadline. The owned Editor had no main-window HWND and produced neither the requested log nor the acceptance JSON. This establishes a startup/acceptance gap, not its cause. The separate native WebView2 smoke and seven passing batch EditMode tests do not close this gate.

The first batch MCP acceptance attempt exceeded its original 90-second readiness limit. Its log subsequently recorded 130 seconds of project loading and a valid endpoint readiness receipt. The readiness limit is now four minutes. A new owned Editor completed MCP initialization/discovery and the three registered tools; Unity verified the created GameObject, active selection and main-thread identity, then Undo removed the object. The endpoint was disabled and its worker/listening state confirmed stopped before Editor exit. `build/evidence/mcp-live-result.json` and `unity-agent-acceptance.json` record that live round trip, including `endpointStopped: true`. This validates the standalone preview adapter, not DCC-MCP Core attachment.

The acceptance project is isolated from a user's project and generated files are ignored. No runtime player, macOS or Linux support is claimed.
