# Owned test Editor exit

Ordinary scene owners and the Node preview still expose only three scene tools.
The Core adapter can additionally expose `editor.exit` when its trusted bootstrap
supplies the exact identity from a newly launched owned test Editor.
This is a Unity lifecycle command, independent of native CUA window-close policy.

After the controller grants the host resource window, set
`UNITY_EDITOR` explicitly and run
`vx just launch-owned-editor <fresh-32-hex-run-id> <candidate-receipt>`.
The controller must first verify the exact-head CI and native artifact, then
freeze a receipt containing `source_commit`, `exact_head_ci="success"`,
`native_dll_sha256` and `native_dll_artifact_id`. Launch verifies the actual
HEAD, clean source tree and DLL against that receipt and records their identity.
The launcher refuses a running Editor for the same canonical project or owned
run ID, and refuses an existing evidence directory. Unrelated Editors can run
concurrently; unavailable or ambiguous process arguments leave the conflict
unknown and refuse launch. It passes `-auroraviewOwnedTest`, opts into AgentEndpoint, and writes
`owned-editor-owner.json` and `owned-editor-launch.json`. It does not establish
UI acceptance, automatically end the Editor, or activate a license.
Physical UI observation and input additionally require a desktop resource grant.

For an independent project, pass the optional third `project_path` argument:
`vx just launch-owned-editor <run-id> <candidate-receipt> <project-path>`.
Omitting it retains `Samples~/SceneTools`. An explicit project requires four
additional receipt fields: `project_path`, `evidence_dir`,
`project_manifest_sha256` and `project_version_sha256`. The paths must match the
canonical project and output selected by `AURORAVIEW_UNITY_EVIDENCE_DIR` (or the
default fresh run directory); the hashes bind `Packages/manifest.json` and
`ProjectSettings/ProjectVersion.txt`. The project's local `file:` dependency for
`com.auroraview.unity` must resolve to the reviewed package checkout. Its `Assets`
directory must exist. The launch receipt records the output and configuration
hashes alongside the unchanged Editor owner identity.

`vx just test-launcher` checks these production guards using fake process
metadata and disposable text fixtures, without querying or launching an Editor.

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
