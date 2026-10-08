# Architecture and boundaries

The adapter serves one concrete demo: use a Web UI inside Unity to inspect the scene, create an undoable cube, and select an object. The identical allowlist is exposed through an explicitly enabled MCP endpoint.

| Layer | Owns | Does not own |
|---|---|---|
| HTML + upstream AuroraView bridge | UI state, promise calls, named events | Unity objects or tool discovery |
| Native C ABI | Bounded string queues and numeric view handles | Managed callbacks or scene operations |
| Native STA | Child HWND, WebView2 COM objects, browser message pump | Unity event loop |
| Unity EditorWindow | Panel rectangle, dock host, HWND/PID binding, disposal | COM interface pointers |
| SceneContracts on Editor main thread | Allowlist, validation, scene context, selection, Undo | Network or browser plumbing |
| Opt-in named-pipe endpoint | Current-user ACL, one bounded request per connection | Scene access on the transport thread |
| MCP stdio adapter | Explicit tool schemas, PID-selected transport | Arbitrary code or automatic UI-to-tool conversion |

## Native panel

`HostWindow.Find` enumerates top-level windows from the current Unity PID and chooses the smallest visible client area containing the panel. It does not use a global foreground HWND. `GUIToScreenPoint` and `pixelsPerPoint` determine the panel rectangle. The native STA creates a `WS_CHILD` window parented to that Unity HWND and sizes its WebView2 controller to the child's client rectangle. Moving a dock to a different native Unity window reparents the child on its owning STA.

Inactive dock tabs stop repainting; a short visibility timeout hides the native child. This overlay approach uses native child windows, not Unity texture rendering or Unity's undocumented internal browser. It needs visual validation for dock-tab changes, DPI scaling, monitor moves and modal windows. Until those checks pass, it is a Windows Editor preview, not a blanket claim that every docking configuration works.

All COM calls occur on the native thread that initialized STA. Unity only enqueues placement and script values, or polls message strings. Shutdown closes the controller and child on that STA. While waiting, the calling thread dispatches Win32 sent messages so child destruction can notify Unity without deadlocking. Assembly reload and window disable dispose the native view before managed code unloads.

## Protocol

The unchanged upstream bridge posts a string via `window.ipc.postMessage`, backed by WebView2 `chrome.webview.postMessage`. A request is:

```json
{"type":"call","id":"av_call_…","method":"scene.create_cube","params":{"name":"Demo cube"}}
```

`EditorApplication.update` polls at most 32 browser messages per tick. It dispatches only `scene.context`, `scene.create_cube`, and `scene.select`, then sends `window.auroraview.trigger('__auroraview_call_result', response)`. Success has `{id,ok:true,result}`; failure has `{id,ok:false,error:{code,message}}`. Selection changes use `window.auroraview.trigger('scene.selection', context)`.

Requests and native scripts are limited to 64 KiB; native queues contain at most 256 items. Calls time out through the official bridge. Native page navigation is restricted to the installed local document; new windows are blocked and WebView2 messages must originate from that document. The demo's CSP disallows remote resources, connections and frames. The injected bridge only runs in the top-level document.

## Agent path

The following named-pipe/stdin path is a preview demonstration only. It is not attached to DCC-MCP Core. AuroraView's production responsibility remains UI/render/dock; the thin integration must reuse DCC-MCP's existing server, tools, Skills, host bridge, scheduler and lifecycle through the shared facade. See [ownership and attachment rules](shared-runtime.md).

The endpoint is session-only, disabled by default, and closed on domain reload. The named pipe name includes the Editor PID. Win32 creates its protected DACL with one allow entry for the process user's SID, rejects remote clients, and refuses an existing pipe of that name. Unity Mono wraps the overlapped handle with an owning `SafePipeHandle`; it does not use Mono's unimplemented `PipeSecurity` constructor. A background thread reads one bounded JSON line, queues a main-thread request and waits up to 10 seconds. A queued request cancelled by timeout is not executed later. The Node MCP server translates three documented tools to the same methods, using newline-delimited stdio JSON-RPC and an explicit `--pid`.

This package implements a standalone MCP server. A DCC-MCP host registry is a separate integration: register the three methods with their schemas, instance identity and main-thread requirements. No code assumes a rendered button is automatically an MCP tool.

## Shared AuroraView source

The reusable compatibility boundary today is the official bridge and message envelope. The Rust `auroraview-dcc` API in the main repository is not exported as a general C ABI and is designed around its host timer. This adapter consequently owns a small WebView2 C++ native backend without loading Python/Qt. Introducing a maintained shared native ABI later should replace that backend while preserving the scene contract and frontend APIs. It must not move STA COM interfaces into shared cross-thread state.
