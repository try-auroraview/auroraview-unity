# Shared runtime boundary

The source candidate consumes the published shared facade through an external Python integration. Real Editor Core and GUI acceptance remain separate gates; the original native ZIP predates this addition.

AuroraView owns the WebView, HTML resources, frontend bridge, rendering and native dock region. A thin integration maps explicit page calls and subscriptions to the existing DCC-MCP host runtime. DCC-MCP Core owns the MCP server, tool and Skill discovery, host execution bridge, scheduling, service lifecycle and diagnostics. The Unity host adapter supplies Unity main-thread execution and scene APIs.

The original `agent/server.mjs` remains a standalone preview. `agent/core.py` consumes public `Tool`/`ToolSet.attach` contracts and reuses its current-user pipe client; `agent/core_server.py` explicitly composes public Core service, queue and lifecycle APIs. Neither integration starts automatically with a panel. Do not start the standalone preview MCP server in parallel with a Core evaluation. See the [Core tutorial and fixed dependencies](core-runtime.md).

## Attachment and ownership

- Prefer attaching to a DCC-MCP runtime already owned by the Unity host, through its public shared facade. Register only explicit tools with schemas and thread requirements.
- A borrowed service stays owned by the host. Closing a panel releases the panel's connections, subscriptions and tasks; it must not stop that service or its host execution bridge.
- If the integration explicitly creates a missing service, it owns that service and its workers/processes and must close them completely. Creation is a separate opt-in step, not a side effect of opening arbitrary UI.
- Panel-owned native WebView2/STA resources are always released by the panel. Frontend timeout/cancellation, reopen, assembly reload and Editor exit need coverage independently from service shutdown.
- The current preview pipe and transport thread are created and owned by this package. Disabling that preview endpoint does not imply any action on an external DCC-MCP service.

The borrowed Core HTTP regression verifies that owner closure leaves its service alive and stale tool calls fail. Real Editor acceptance must still demonstrate page and agent queries against the same host instance, preserve a borrowed service when the panel closes, and verify cleanup of owned resources on success, failure, cancellation and repeated startup/shutdown.
