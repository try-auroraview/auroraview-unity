# Consume DCC-MCP Core

For explicitly launched test instances, see [owned test Editor exit](owned-editor-exit.md).

The optional external Python integration registers the same three `SceneContracts` used by the WebView. Unity still owns scene access, selection, Undo, its update loop and the native panel. DCC-MCP Core owns MCP/HTTP, discovery, dispatch, service registration and shutdown. No Python interpreter is embedded in Unity.

## Fixed public dependencies

`agent/requirements-core.txt` locks every dependency and SHA256. The consumer uses [dcc-mcp-core 0.20.41](https://pypi.org/project/dcc-mcp-core/0.20.41/) and the public [auroraview-dcc-mcp 0.1.0 preview wheel](https://github.com/try-auroraview/auroraview/releases/tag/auroraview-dcc-mcp-v0.1.0-preview.1). The shared wheel's SHA256 is `450b74fd7c11c9247f076b456197a4c5b299c9edb34f851fda5ccc5cf1bf3558`. There is no dependency on an unpublished local Core patch.

Use Windows x64, Python 3.11 and Node 22. `vx just core-env` creates an external Python environment and installs the hash-verified lock. This setup can download the fixed public packages. Subsequent unit tests perform no network calls. An existing isolated interpreter can be selected with `AURORAVIEW_CORE_PYTHON`; its dependencies must already match the lock.

The lock separately pins the transitive `dcc-mcp-server` distribution to 0.20.42. The Python HTTP smoke executes the installed `dcc-mcp-core` 0.20.41 native extension; it does not certify a separately launched server binary.

## Borrow an existing service

On the existing service's registered Core execution lane:

```python
from core import SceneTools  # agent/core.py, available on your integration's import path

tools = SceneTools(editor_pid, node="C:/path/to/node.exe")
binding = tools.attach(existing_server)
# Keep tools and binding alive while this integration is enabled.
# Later, on the same execution lane:
tools.close()
```

Attachment does not start another MCP server or replace the borrowed server's dispatcher. Closing the owner unloads only its skill, revokes stale wrappers and releases its temporary scripts. The existing service remains running. The caller supplies the execution lane and must retain this owner until unload; do not construct it on an unrelated HTTP worker.

The exact MCP names are returned by `binding.method_names`. Their Core arguments use the shared facade's `{"params": {...}}` envelope. There is no arbitrary script or automatic UI-to-tool exposure.

## Explicitly create a missing service

1. Load this source candidate with the existing native DLL and explicitly enable **Window → AuroraView → Enable Agent Endpoint** in the intended Editor.
2. Record that Editor's PID. Node 22 must be available on the external Python process's PATH, or pass an absolute executable with `agent/core_server.py --node`.
3. Run `vx just serve-core 12345 C:/path/to/isolated-state C:/path/to/node.exe`. The explicit command creates a Core service, its `QueueDispatcher` and `StandaloneHost`. It prints the bound PID, session id, MCP URL and tool names. Discover and call those tools through Core.
4. Stop the sample with Ctrl+C. It unloads its tool owner, stops only its created Core service and stops its queue driver. The Unity pipe endpoint is separately owned by the Editor and can be disabled from its menu. Opening or closing the WebView does not start or stop a borrowed Core service.

The pipe grants access only to the current Windows user. The consumer probes `scene.context`, requires the requested PID, Unity version, main-thread id and a session GUID, then carries that GUID on every call. Unity checks it before scene dispatch. Endpoint re-enable, assembly reload or Editor restart invalidates old consumers; explicitly attach a new owner. Core receives the bound host PID for registry liveness. The original Node preview remains compatible, and its standalone server must not run in parallel with this Core evaluation.

The Python transport reuses the existing Node `pipeCall` through a bounded one-shot client. It does not run `agent/server.mjs` as an MCP server. Pipe calls have a 12-second deadline, the Python child has a 14-second deadline and is killed/reaped on timeout. Unity skips queued requests after its 10-second dispatch timeout. Already-running native scene operations cannot be safely preempted; the three operations remain bounded.

## Read Editor exit blockers

The existing read-only `scene.context` result adds `editorStatus`; its arguments and the registered tools stay unchanged. Python and Node preserve the complete result, and the WebView continues to render its existing fields. Reading this status never requests exit or changes scene, assets, selection or windows. Selection events retain lightweight context; request `scene.context` for a diagnostic sample.

Unity captures the status on its main thread. `sampledAtUtc` is the actual UTC sample time; `editorOwner`, `sessionId` and `mainThreadId` match the outer context identity. For an explicitly owned test Editor, `editorOwner` includes its PID, native process creation time, project path and run id. Check these identities against the intended Editor before using a diagnostic result.

The snapshot contains `isCompiling`, `isUpdating`, `isPlayingOrWillChangePlaymode`, `dirtyScenes` (name, path, handle), `dirtyPersistentAssets` (instanceId, path, type, name), `unsavedWindows` (instanceId, type) and `prefabStage` (isOpen, assetPath, scenePath, rootInstanceId). Unity object IDs may be negative, and empty paths or names are preserved. A closed Prefab stage has `isOpen=false`, empty paths and `rootInstanceId=0`.

`dirtyScenesTotal`, `dirtyPersistentAssetsTotal` and `unsavedWindowsTotal` report the complete local scan counts; returned arrays may contain fewer entries. Status `incomplete` is true if any diagnostic entry is omitted or its display text is shortened. The outer `selectionTotal` reports the complete selection count, while `contextIncomplete` also covers omitted selection entries, shortened scene/object display text or an incomplete status. Empty returned arrays do not prove clean state when these flags or counts indicate omissions.

Response serialization initially retains at most 32 entries per array and 256 UTF-8 bytes per display string, then reduces them as needed until the entire success JSON, including its envelope, is at most 61,440 UTF-8 bytes. This leaves room for CRLF below the 65,536-byte transport limit. Owner/session/PID/time identity and valid correlation IDs are never shortened. If the identity envelope cannot fit, the existing call-failure shape returns a bounded error message instead of a partial success.

Normal owned `editor.exit` uses the same snapshot logic for its clean-state guard and samples again immediately before quitting. It refuses incomplete status or count/array mismatches and uses a fresh, complete local snapshot. A prior clean diagnostic or exit acknowledgment does not prove process exit or bypass a later blocker. Diagnostic data grants no save, discard, force-exit or retry authority; no operation here clears dirty flags.

## Trusted private gateway bootstrap

An owner evaluating a qualified Core build with typed remote gateway options can explicitly select a private gateway:

```python
service = CoreService(
    editor_pid,
    isolated_state,
    gateway_port=private_gateway_port,
    gateway_remote_host="127.0.0.1",
    gateway_remote_port=0,
    enable_gateway_failover=False,
)
```

The main gateway port must be a positive, unused private port to obtain Core's registered `instance_id`. Setting the main port to zero disables registration and returns no UUID. The separate remote port of zero disables only the second listener. Disabling failover skips Python's detached gateway daemon while retaining native registration in the owned Core process. The registry remains under `isolated_state/registry`.

The three new keywords default to `None` and are omitted unless explicitly supplied, preserving the fixed public dependency path. The owner must verify its qualified Core build, actual UUID and listener ownership, then read back registry removal and socket release after stopping. Option forwarding and a successful stop call alone do not prove those runtime outcomes.

## Trusted UI bootstrap

An owner evaluating a Core version that supports `UiControlRuntimeOptions` can supply that typed object through `CoreService(..., ui_control=options, skill_root=canonical_skill_root)`. The owner selects and verifies the runtime executable, hash, exact version and bounded action scope before starting a new service. `skill_root` must be an existing canonical Core skill directory; Core receives it through its public built-in skill discovery argument. On the service's registered execution lane, call `server.skill_discovery.register_builtin_actions(include_bundled=False)` and `server.load_skill("ui-control")` before discovering its tools. Disable default and accumulated skill paths when isolating an evaluation.

Both keywords default to `None`. The normal path retains its private skill directory and omits the `ui_control` keyword when constructing the published Core 0.20.41 options. The current dependency lock is unchanged. The optional pixels runtime contracts require the separately reviewed [Core runtime options](https://github.com/dcc-mcp/dcc-mcp-core/pull/2721) and [foreground preparation](https://github.com/dcc-mcp/dcc-mcp-core/pull/2724); forwarding options does not establish runtime or Editor acceptance.

Runtime selection, grants and skill sources are trusted bootstrap configuration. They are not parameters of scene tools or public UI calls. Keep recording disabled unless an owner explicitly configures its separate grant. Application UI continues through canonical `ui-control`, with a fresh exact PID/HWND binding and cleanup through `stop_computer_use`.

`CoreService` requires an already enabled Unity agent endpoint to attach its scene tools. For an initial application modal before that endpoint exists, use the canonical Core SDK's UI-only bootstrap. Stop its UI task and service before changing the target, then create a fresh binding for the ready Editor and attach its scene tools.

## Evidence

`vx just test-core` runs offline contracts for explicit methods, input rejection, PID/session checks, stale calls, owner closure and bounded transport. `vx just test-core-service` checks trusted option forwarding and default compatibility with service construction replaced by test doubles; it starts no HTTP service or native task. `vx just test-core-http` explicitly starts numeric-loopback Core with a fake Unity transport and verifies MCP discovery, execution lane, borrowed cleanup and owned shutdown. The Node suite also exercises the one-shot client against a controlled local named pipe.

These checks establish public dependency consumption and transport composition. They do not establish real Editor Core calls, Unity EditMode results for this candidate, WebView round trips, docking, input or high-DPI acceptance. The published `v0.1.0-preview.1` ZIP predates this integration; see the [validation ledger](validation.md) before choosing it for a workflow.
