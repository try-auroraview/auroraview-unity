# Shared runtime boundary

This is the intended integration direction, not a claim that the shared facade is implemented by this Unity package.

AuroraView owns the WebView, HTML resources, frontend bridge, rendering and native dock region. A thin integration maps explicit page calls and subscriptions to the existing DCC-MCP host runtime. DCC-MCP Core owns the MCP server, tool and Skill discovery, host execution bridge, scheduling, service lifecycle and diagnostics. The Unity host adapter supplies Unity main-thread execution and scene APIs.

The `agent/server.mjs` and `AgentEndpoint` code shipped here are an optional preview example for validating the human/agent contract. They are not a new production Core, do not implement the shared facade, and are not automatically attached to DCC-MCP. Do not start a parallel preview endpoint when evaluating a future shared-runtime attachment.

## Attachment and ownership

- Prefer attaching to a DCC-MCP runtime already owned by the Unity host, through its public shared facade. Register only explicit tools with schemas and thread requirements.
- A borrowed service stays owned by the host. Closing a panel releases the panel's connections, subscriptions and tasks; it must not stop that service or its host execution bridge.
- If the integration explicitly creates a missing service, it owns that service and its workers/processes and must close them completely. Creation is a separate opt-in step, not a side effect of opening arbitrary UI.
- Panel-owned native WebView2/STA resources are always released by the panel. Frontend timeout/cancellation, reopen, assembly reload and Editor exit need coverage independently from service shutdown.
- The current preview pipe and transport thread are created and owned by this package. Disabling that preview endpoint does not imply any action on an external DCC-MCP service.

No proposed facade method names are documented here until its owning project publishes a real contract. Future acceptance must demonstrate page and agent queries against the same host instance, preserve a borrowed service when the panel closes, and verify cleanup of owned resources on success, failure, cancellation and repeated startup/shutdown.
