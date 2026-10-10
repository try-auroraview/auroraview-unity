# Official DCC-MCP Unity panel

Import the **Official DCC-MCP Scene Tools** sample from Package Manager after installing a matching official `dcc-mcp-unity` candidate that exposes `DccMcpCommands.Execute` and `editor.inspect_dirty_assets`. The published 0.14.0 package does not include these additions yet. Importing the sample against that older package will fail to compile; the base AuroraView package has no DCC-MCP dependency.

Open **Window > AuroraView > DCC-MCP Scene Tools**. The sample uses the existing AuroraView call/result bridge and invokes the official fixed command dispatcher on the Editor main thread. It opens no server or new transport. Project inspection, Console reading, object creation and object readback retain the official command validation and Undo behavior. Console entries only cover the period after the official plugin loads.

Dirty asset inspection is read-only. `complete` concerns enumeration; `identity_complete` concerns identities. Neither proves ownership, saveability or permission to discard an asset. An over-budget diagnostic returns an explicit incomplete result with no partial item list. The panel displays the entire returned result and its completeness flags.

Build and install the matching AuroraView native candidate as well: the older DLL accepts only 65,536 script characters. This candidate accepts up to 1 Mi UTF-16 characters per result while retaining the previous total queue memory budget. Larger results fail explicitly without a partial list. Source tests do not qualify an older installed DLL for this route.

The sample intentionally exposes no save, discard, refresh, exit or arbitrary code command. Close and reopen its menu after an assembly reload; its delegate cannot survive reload and never falls back to the standalone Scene Tools dispatcher.

Install/load acceptance still requires the same Editor's current identity, compilation status, unsaved-state preservation and actual panel readback. Source and frontend tests do not establish that a running Editor loaded either candidate.
