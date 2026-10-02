# Bounded Editor validation contract

The server exposes 21 MCP tools: the existing 17 rendering queries and four explicit
Editor workflow tools. This local extension was authorized for Endfield Lobby AA.
It adds no arbitrary C# evaluator, general menu command, build endpoint, window input,
or permanent ProjectSettings change.

## Tools and routes

| MCP tool | Unity HTTP route | Contract |
|---|---|---|
| `get_editor_state` | GET `/api/editor/state` | Read compiler/Play state, last refresh/job, runtime counters and bounded camera/display/object parameters. |
| `refresh_editor` | POST `/api/editor/refresh` | Queue one refresh; return `requestId`, then observe that same request to completion. |
| `set_editor_play_mode` | POST `/api/editor/play-mode` | Only `enter`, `exit`, `pause`, `resume`; no scene save or replacement. |
| `run_lobby_aa_validation` | POST `/api/editor/validation` | Fixed entries below; fresh output under `D:/Endfield/Delivery/evidence/Validation/LobbyAA`. |

## Fixed validation entries

| Entry | Installed method | Result |
|---|---|---|
| `camera` | `HGLobbyAACameraValidation.Run(string)` | Edit-mode camera/history/projection checks; `camera-report.json`. |
| `reference` | `HGLobbyAAReferenceValidation.Run(source,native,output)` | Source/native roots are fixed in the bridge; `report.json` with `accepted`. |
| `configure` | `HGLobbyAAIntegration.Configure()` | Saves only the explicitly requested loaded Lobby configuration; rejects dirty loaded scenes and the wrong active scene. |
| `preview` | `HGLobbyAAIntegration.RunPreview(string)` | Existing unpaused Play; asynchronous `report.json`. |
| `preview-restart` | `HGLobbyAAIntegration.RunPreviewWithRestart(string)` | Same preview with the finite restart prerequisite; same background lease and async report. |
| `history-clear` | `HGLobbyAAHistoryClearValidation.Run(string)` | Isolated preview-camera sentinel diagnostic; public histories read after Render and before Commit, permitted without exiting Play. |
| `diagnostic-preview` | `HGLobbyAAIntegration.RunDiagnosticPreview(string)` | Diagnostic collection; keeps the report's failure/diagnostic status. |
| `dynamic` | `HGLobbyAAIntegration.RunDynamic(string)` | Existing unpaused Play; asynchronous `report.json`. |
| `lifecycle` | `HGLobbyAAAcceptanceValidation.RunLifecycle(string)` | Existing unpaused Play; asynchronous `report.json`. |
| `regression` | `HGLobbyAAAcceptanceValidation.RunRegression(string)` | Fixed saved-scene checks with restoration; asynchronous `report.json`. |
| `repair-check` | `HGLobbyRuntimeRepairValidation.Run(string)` | Fixed camera cleanup/material authority/readiness cases; saves complete caller state and restores it with 64 AA frames (or 1 frame with AA off). |
| `repair-restore` | `HGLobbyRuntimeRepairValidation.Restore(statePath,output)` | Restores `LobbyRuntimeRepairState1` from a saved `caller-state.json` inside the evidence root after a separately completed Play transition; same stable-frame gate. |

The namespace is `HG.Rendering.Validation.Editor`. The caller cannot supply a type,
method name, source directory, script body or arbitrary scene path. Output is
canonicalized and rejected if outside the task root or already present. Configure
is an explicit mutation; read-only status calls never configure or save a scene.

## Refresh completion without focus

Both bridge bootstrap after reload and request dispatch use one-shot
`EditorApplication.update` callbacks. The older Inspector-dependent `delayCall`
stall was fixed; ordinary operation requires no focus or manual Refresh.

`refresh_editor` returns acceptance and a request ID. `get_editor_state.refresh`
persists the same ID, phase, timestamps, requested/completed assembly MVID, compile
and reload observations, focus booleans, `finished`, `passed`, and any error through
SessionState. A short HTTP disconnect during domain reload is expected: continue
read-only polling and never duplicate the mutation. A prior request's delayed state
must not be mistaken for the newly accepted request.

`scripts/refresh-editor-and-wait.js` uses the official MCP SDK, sends the mutation
once, and polls for the same ID for up to 120 seconds. `scripts/call-editor-tool.js`
is a bounded one-call MCP client when the host's cached tool list cannot hot-reload.
Neither script uses a private file-command queue.

Compiler diagnostics from compilation events persist across reload. Current
`EditorUtility.scriptCompilationFailed` remains authoritative for C# failure; bounded
console compiler errors are a best-effort fallback, and may include historical messages.
Empty diagnostics do not prove success: require idle compiler/import state,
`compilerFailed=false`, and the requested operation's actual completion report.

## Background Play and asynchronous validation

Long validation entries temporarily own `Application.runInBackground=true` and call
`EditorApplication.QueuePlayerLoopUpdate`. They do not inject animation Evaluate,
change target FPS, or replace natural rendering with a generic camera-render loop.
The prior runtime flag is saved in SessionState and restored on completion, failure,
Play interruption, reload or quit. The actual job record includes start/end frame
counts, queue request count and the restored flag when completed in that domain.

`validation.running` remains true until the report says `finished=true`; the bridge
then preserves the report's `passed` result. Conflicting refresh, Play control,
configuration and a second validation are refused while a validation runs. An
interruption produces a failed `mcp-job.json`; it is never treated as acceptance.
The report is authoritative for render/animation coverage, not HTTP acceptance.

## Read-only view diagnostics

The existing state tool reads Time/sample counters, initialization/failure status,
Camera enable/target, transform, FOV/clips/aspect/projection mode, desired AA from the matching `HGLobbySample.antialiasing` (`antialiasing`/`desiredAA`),
actual temporal-context readiness (`temporalEnabled`), Lobby failure, buffer view/display override and the same final texture choice used by sample OnGUI. It
also reads at most eight captured objects' Camera24/32 vectors with
`Material.GetVectorArray` and the temporal owner's last jittered/nonjittered VP.
These are last-submitted material values, not GPU buffer readback. Matrix float[16]
follows Unity's column-major indexer. The first installation did not have a complete
pre-install transform snapshot and does not invent one.

## Recorded verification and limits

Evidence root: `D:/Endfield/Delivery/evidence/Validation/LobbyAA`.

- `MCPBackgroundRefresh01/proof-before.json`, `proof-request.json`, `proof-after.json`,
  `report.json`: real request `65e3de5dbae6416e89549af37a0c0ddd`; same PID78036, all
  request/execution/completion focus flags false, compiler and reload observed,
  new assembly MVID and source marker loaded in 3.167 seconds; clean Lobby unchanged.
- `Dynamic01/mcp-job.json`: natural frame9604→11754, 2151 queued updates, original
  `runInBackground=false` restored. `Lifecycle01` and `Regression01` likewise record
  restoration. These prove the tool lease at their recorded source revisions; they
  do not validate later AA/render changes or every interruption scenario.
- `MCPReadonlyCameraState01/report.json` and `UserViewAfterHotRefresh01/result.json`:
  source/install identity, standalone compile and actual newly exposed read-only state.
- `CameraImplementationFinal01/node-tests.log` records eight Node tests;
  `MCPDiagnosticPreviewEntry01/report.json` records the later fixed diagnostic-entry
  targeted tests. Standalone C# compilation is explicitly separate from Unity import
  and GPU execution.

Retained failures include absent initial bridge, optional NUnit test dependency
blocking reload, the abandoned debugger bootstrap, the old delayCall pending state,
and Preview01's background zero-frame watchdog. Optional NUnit package tests are
compiled only when `com.unity.test-framework` is installed; their absence is not a
claim that those Unity unit tests ran. No dependency was fetched/upgraded for this
workflow: the installed Newtonsoft 3.2.1 came from the local Unity cache.

Repair reports require `finished && passed && restored`, zero `unexpected` errors, and `stableFrames >= 64` with saved AA on (otherwise 1). `checks` includes actual successful-render deltas and exact 128-float source comparisons with only row 11.z/w adjustable. Play exit/re-entry is performed only between finished jobs; original interruption protection remains active. Audit appended Editor.log bytes separately, including Unity/shader errors outside the job callback; do not clear historical Console entries.

### General Console errors (read only)

`get_editor_state.consoleErrors` complements the existing compiler fields. It scans
at most the newest 2000 accessible Console rows and returns the newest 20 Error,
Exception or Assert-class entries (including native error-class flags). Each returned
entry keeps the full original message and the native UTF16 message/stack split, raw
mode, file/line and repeat count. Returned text is not character-truncated.

`countsByType` comes directly from Unity's native error/warning/log counters; it is
separate from row counts and collapse semantics. `totalRows`, `scannedRows`,
`matchingRowsInScannedRange`, `returnedEntries` and `truncated` report the bounded
scan. Existing Console filtering and collapse settings are not changed. The response
also reports filter/collapse/error-visibility flags and `filtersUnchanged`; filtered
empty results are not evidence of no errors. `available=false` means read capability
failed, not zero errors. The read uses balanced StartGettingEntries/EndGettingEntries
and never clears the Console, changes filters or opens/focuses a window.


## Endfield URP migration validation (2026-09-14)
Authorized fixed entries: urp-scene, urp-game, urp-baseline-scene, urp-setup-fault, urp-post-fault, urp-tone, urp-overlay-reload, urp-material-workflow. They invoke installed project diagnostic APIs only, with fresh output restricted to D:/Endfield/Delivery/evidence/Audits/URPMigration20260914_01. No arbitrary code/menu execution. Accepted response includes jobId; get_editor_state.validation exposes running/finished/passed/result and material workflow progress. The material workflow deliberately crosses its own reload and exit phases; those two declared phases retain the job, all other interruption rules remain. Tests preserve original dirty Scene, and may allocate temporary diagnostic fixtures or update owned compatibility assets only within their documented validation scope.

Additional fixed migration entries: urp-shadow-flags (temporarily toggle one original Renderer and restore, no Scene save), urp-bound-response (eight existing variants, visible temporary material edit/revert), urp-cloth-inputs (same-frame read-only input capture), urp-irradiance-import (Edit-only persistent GI import with source/GPU/reload checks; release Scene frames after resource replacement). Same bounded evidence root/job protocol applies.

Controlled cancellation: cancel_urp_validation requires the exact active jobId and currently supports only urp-bound-response, whose installed Cancel restores temporary edits/animation/AA and writes a failed report. Unknown job IDs, other suites, and compilation/update windows are refused. Existing Play/refresh guards remain unchanged.

`urp-scene-lifecycle` executes the installed bounded SceneView suite in dedicated Play, exposes progress text via get_editor_state.validation, and supports exact-job cancellation after pending GPU work and owned-view/fixture restoration. It exports native Orientation Gizmo render targets through Unity APIs, not desktop screenshots or simulated OS input.

Bounded baseline handoff: `urp-baseline-hop-check` verifies the fixed live safety snapshot and inventories persistent dirty assets plus ShaderUtil batching compatibility. `urp-baseline-hop-exit` repeats those guards and schedules EditorApplication.Exit only if safe; it never saves the original scene.
