# AuroraView for Unity

Windows Unity Editor adapter. Use `vx just <recipe>` for all tasks and `vx` for tools.
Git identity: loonghao <hal.long@outlook.com>. Use English Conventional Commits.

- `Editor/`: Unity main-thread contracts, native panel, explicit opt-in agent endpoint.
- `native/`: Windows child HWND and WebView2, owned by a dedicated STA.
- `Editor/WebAssets/vendor/`: unchanged upstream AuroraView bridge; provenance in THIRD_PARTY.md.
- `agent/`: dependency-free MCP stdio adapter; named-pipe transport to an opted-in Editor.
- `agent/core.py`: public shared-facade consumer; borrowed attachment never stops its service.
- `agent/core_server.py`: explicitly owned external Python Core sample, using Core's queue/driver.
- `Tests/Editor/`: Unity EditMode contract tests. `tests/`: bridge and MCP protocol tests.

Never touch Unity objects from native callbacks or transport threads. Never move COM objects off their STA.
Scene mutation must be explicit, validate input, and register Unity Undo. Dispose before assembly reload.
Do not claim runtime player, macOS/Linux, or docking acceptance from unit tests.
Keep generated binaries and Unity Library directories out of Git; release the native DLL as an asset.
UI automation uses project dcc-cua only, with runtime/PID/HWND binding before observation or input.
`vx just test-core` is offline after locked dependency setup; `test-core-http` explicitly starts numeric-loopback Core with a fake Unity transport. Neither proves real Editor acceptance.
