# Owned test Editor exit

Ordinary scene owners and the Node preview still expose only three scene tools.
The Core adapter can additionally expose `editor.exit` when its trusted bootstrap
supplies the exact identity from a newly launched owned test Editor.
This is a Unity lifecycle command, independent of native CUA window-close policy.

After the controller grants the host and desktop resource window, set
`UNITY_EDITOR` explicitly and run
`vx just launch-owned-editor <fresh-32-hex-run-id> <candidate-receipt>`.
The controller must first verify the exact-head CI and native artifact, then
freeze a receipt containing `source_commit`, `exact_head_ci="success"`,
`native_dll_sha256` and `native_dll_artifact_id`. Launch verifies the actual
HEAD, clean source tree and DLL against that receipt and records their identity.
The existing launcher refuses any running Editor and an existing evidence
directory. It passes `-auroraviewOwnedTest`, opts into AgentEndpoint, and writes
`owned-editor-owner.json` and `owned-editor-launch.json`. It does not establish
UI acceptance, automatically end the Editor, or activate a license.

Use `vx just exit-owned-editor <owner-file>` for a bounded one-shot invocation of
the public Core facade's typed tool. It probes the current session, calls only
`editor.exit`, and closes its tool owner without starting another MCP service.
Each of the two existing pipe calls has a 14-second client timeout.
An existing service can opt in with
`vx just serve-owned-core <pid> <new-state-dir> <owner-file>` or
`SceneTools(..., editor_owner=owner)` on Core's existing execution lane.
The supplied PID, decimal-string process birth, absolute project path and run ID
must match the real endpoint context. The tool accepts an empty argument object;
clients cannot choose another process, project, exit code or arbitrary script.
The host requires its current session and the same identity again at dispatch.
An ordinary Editor without the launch flag cannot use this command.

The endpoint executes validation on `EditorApplication.update`. It rejects
Play mode, compilation/import activity, dirty scenes, dirty persistent assets,
Editor windows with `hasUnsavedChanges`, and open Prefab stages. Window checks
also cover nonpersistent custom Editor windows. It never saves assets, clears dirty flags, performs
Undo, or discards changes. It validates again immediately before exiting.
The browser dispatcher cannot schedule exit.

An accepted response means only `exitRequested=true`. The transport writes and
flushes that response before enqueueing exit for the main thread. A timeout or
write failure does not enqueue exit. If work becomes dirty after acceptance,
the final check refuses exit and logs the reason.

Keep the same SDK alive when a native CUA task exists: actual application exit,
same-SDK canonical stop and native ACK, then SDK shutdown. For inventory-only
work with no task, use the reviewed zero-task dispose branch. In every case,
retain the original process handle early, observe its real exit and exit code,
and read back the owned endpoint/socket/registry state. ACK, missing HWND or a
late missing PID alone never proves complete shutdown or releases resources.

`vx just test-editor-owner` runs dependency-free identity and unsaved-work
guards without launching Unity. It compiles the production `OwnedEditorExit`
against controlled Unity API doubles and exercises a nonpersistent window's
unsaved flag through both `Validate` and the final `Exit` recheck. This does not
certify live Unity API behavior or transport write-failure/timeout sequencing.
`vx just compile-unity` uses the installed
Unity C# compiler and reference assemblies without starting an Editor.
EditMode tests and real lifecycle acceptance still require their own resource
grant. Freeze this new managed candidate and its actual checks separately;
the old 2a47af7 launch receipt cannot certify it. Current V4 SDK/Core/native
inputs remain unchanged.

Unity documents window state in
[`EditorWindow.hasUnsavedChanges`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorWindow-hasUnsavedChanges.html).
[`EditorApplication.Exit`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/EditorApplication.Exit.html)
exits immediately without asking to save, so these guards must run before it.
