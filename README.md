# AuroraView for Unity

A native Windows Unity Editor panel for web tools, with the same explicit scene contracts available to a human interface and an opted-in MCP client.

The included Scene Tools demo creates a real cube, reads the active scene and selection, and selects objects. Unity's main thread performs every operation; scene creation supports Undo. The browser is Microsoft WebView2 embedded as a **child HWND inside an EditorWindow**, not a browser launched beside Unity.

[中文说明](README.zh-CN.md) · [Architecture](docs/architecture.md) · [Validation](docs/validation.md) · [AuroraView](https://github.com/try-auroraview/auroraview)

## Run the demo

Requirements: Windows x64, Unity 2022.3 LTS or newer, and the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/). Unity 2022.3.62f3c1 is the initial local validation target. macOS, Linux, Unity Player builds, and render-texture rendering are not implemented.

1. Download a native package ZIP from [Releases](https://github.com/try-auroraview/auroraview-unity/releases). Extract it and use **Window → Package Manager → + → Add package from disk**, selecting `com.auroraview.unity/package.json`.
2. Open **Window → AuroraView → Scene Tools**. Dock the EditorWindow next to the Hierarchy or Inspector.
3. Enter a cube name and select **Create cube**. The GameObject appears in Unity's Hierarchy and becomes selected. Press Unity's Undo shortcut to remove it.
4. Select another scene object in Unity. The panel's live context updates through `window.auroraview.trigger('scene.selection', context)`.

For a Git-only UPM install, build the native plugin from source below: the source repository does not contain the generated DLL. A missing DLL is reported in the panel rather than replaced with simulated content. Release ZIPs include a SHA256 file for download verification.

## Build and test from source

Install [vx](https://github.com/loonghao/vx) and Visual Studio 2022 with Desktop development with C++. From this repository:

```powershell
vx just build          # Fetch publisher-verified WebView2 SDK and compile Windows x64 DLL
vx just test           # Official AuroraView bridge + MCP contract tests
vx just test-native    # Real WebView2 HWND/IPC/STA lifecycle smoke test
vx just test-unity     # Licensed installed Editor: EditMode scene, thread and Undo tests
vx just accept-unity   # Licensed installed Editor: native panel + browser round trip + scene/Undo
vx just accept-agent   # Real MCP tools → named pipe → Unity scene/selection readback + Undo
vx just package        # Native UPM ZIP for release
```

The SDK is pinned to Microsoft.Web.WebView2 1.0.3537.50. The fetch recipe verifies the official NuGet catalog SHA512 before extracting. If that catalog leaf is unavailable, it requires a trusted Microsoft author signature via `vx dotnet nuget verify --all` (an installed .NET SDK is then required). It does not substitute a locally observed digest or disable verification. Set `UNITY_EDITOR` to the installed `Unity.exe` if automatic discovery is unsuitable. Launch `Samples~/SceneTools` in Unity Hub to use the minimal project.

## Explicit agent tools — preview example

AuroraView owns rendering, native docking and the frontend bridge. The Node stdio server and local pipe in this repository are an opt-in **preview example**, not an integration with DCC-MCP Core. Production integration will use a thin layer to attach the existing DCC-MCP server, tools, Skills, host execution bridge and lifecycle. The shared Core facade belongs to the DCC-MCP integration work; this package does not introduce a replacement Core API. See [shared runtime boundaries](docs/shared-runtime.md).

The UI does not automatically turn controls into tools. This package registers exactly these methods:

| AuroraView contract | MCP tool | Behavior |
|---|---|---|
| `scene.context` | `unity_scene_context` | Read scene, selection, version, PID, main-thread ID |
| `scene.create_cube` | `unity_create_cube` | Create and select a cube; Unity Undo supported; refuses Play mode |
| `scene.select` | `unity_select_object` | Select a live scene GameObject by returned `objectId` |

The agent endpoint is **off by default**. Enable **Window → AuroraView → Enable Agent Endpoint** for the current Editor session. Unity logs its PID and pipe name. Configure an MCP client with an explicit PID:

```json
{
  "mcpServers": {
    "auroraview-unity": {
      "command": "vx",
      "args": ["node", "C:/path/to/auroraview-unity/agent/server.mjs", "--pid", "12345"]
    }
  }
}
```

The process must be this Windows user's Unity Editor. The named pipe grants access only to the current user; the server has no HTTP listener and provides no arbitrary script execution. Disabling the endpoint, reloading assemblies, or quitting closes it. Instance IDs are valid only for the current Editor session. This is a standalone MCP adapter; automatic registration with DCC-MCP is not implemented. A DCC-MCP deployment must explicitly configure these contracts and host identity.

## Integration boundary

Unity owns its event loop. `EditorApplication.update` dispatches bounded JSON calls to `SceneContracts`. A dedicated native STA owns the WebView2 environment, controller, child HWND, native message pump and JavaScript execution. C# only passes values through the C ABI; COM interfaces never cross threads. Unity's native root HWND is selected from windows belonging to the current Editor PID and containing the panel rectangle.

The JavaScript bridge is an unchanged, attributed copy from AuroraView's core, preserving `call`, `api`, `on`, `off` and `trigger`. The Unity adapter does not load Python or Qt, and does not currently link the Rust core: the main repository has no stable general-purpose C ABI. See [architecture](docs/architecture.md) for the boundary and native docking limitations.

## Acceptance

[Validation](docs/validation.md) separates source, Node tests, native build, WebView2 runtime, Unity EditMode, browser-to-host round trip, docking/input checks and release publication. Test results and receipts live in `build/evidence/`; they are not claims of performance, platform breadth or native dock interaction acceptance. Never treat a green native build as proof of a working Unity panel.

MIT. Upstream AuroraView attribution and bridge checksum are in [THIRD_PARTY.md](THIRD_PARTY.md).
