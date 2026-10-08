Windows Unity Editor preview with a native WebView2 child panel, the upstream AuroraView JavaScript bridge, undoable scene tools, and an opt-in MCP stdio adapter backed by explicit main-thread scene contracts.

Install the ZIP as a local UPM package; the source Git package alone has no generated DLL. Requires Unity 2022.3+ Windows x64 and Microsoft Edge WebView2 Runtime. See the repository validation ledger for current native-host and docking acceptance. Unity Player, macOS and Linux are not implemented.

This is a prerelease. Local native WebView2/IPC/STA and seven Unity EditMode tests pass. Real MCP discovery and scene calls pass through the current-user pipe to Unity's main thread, with object/selection readback and Undo. The non-batch Editor acceptance attempt timed out before producing a main HWND or receipt, so the in-Editor browser round trip, visual docking, input and high-DPI gates remain open. The optional MCP adapter is standalone and is not DCC-MCP Core integration.
