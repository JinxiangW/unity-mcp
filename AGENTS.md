# Unity MCP Playbook

This repo is a focused, Unity MCP for material and rendering TA workflows.

## First read in a new session

1. `README.md`
2. `docs/architecture.md`
3. `docs/maintenance-strategy.md`
4. `docs/multi-version-compatibility.md`

## Project shape

- `src/index.js`: stdio MCP server, tool definitions, HTTP forwarding, response normalization
- `unity-package/com.ta.unity-mcp/Editor/UnityMcpServer.cs`: Unity local HTTP host
- `unity-package/com.ta.unity-mcp/Editor/UnityMcpQueries.cs`: query layer over Unity Editor APIs
- `unity-package/com.ta.unity-mcp/Editor/MaterialTransferPackageWriter.cs`: scoped material migration artifact writer
- `unity-package/com.ta.unity-mcp/Editor/UnityShaderGraphTextParser.cs`: Shader Graph structure parser
- `docs/`: architecture, maintenance, extension guidance

## Non-negotiable constraints

- General write scope is limited to material migration artifacts under `Assets/`, plus the explicitly authorized bounded Editor workflow below
- No build control
- No Play control outside the explicit `enter`/`exit`/`pause`/`resume` workflow
- No arbitrary C# execution
- No broad `set`, `modify`, `execute`, or hidden write side effects
- Return structured JSON only

## Design rule

Every feature should fit this path:

1. Unity query logic in `UnityMcpQueries.cs`
2. Unity HTTP route in `UnityMcpServer.cs`
3. MCP tool contract in `src/index.js`
4. Docs update in `README.md` or `docs/`

## Fast verification

When Unity is open, verify in this order:

1. `curl http://127.0.0.1:51234/health`
2. `curl http://127.0.0.1:51234/api/scenes/info`
3. One asset-level query relevant to the change
4. One shader or shadergraph query relevant to the change

## Extension policy

- Prefer additive APIs over changing existing response shape
- If a field is unstable across Unity versions, mark it best-effort and keep raw-safe output
- Normalize noisy Unity results in MCP layer only when that avoids breaking Unity-side query simplicity
- Keep TA-facing tools scoped to retrieval, analysis, tracing, and usage lookup

## Definition of done

- Unity compiles cleanly
- `/health` responds
- New endpoint returns JSON and fails safely
- README/docs mention the new capability
- Write-scope constraints still hold

## Explicit local Editor validation extension (2026-09-12)

The user authorized `get_editor_state`, `refresh_editor`, `set_editor_play_mode`,
and `run_lobby_aa_validation` for the current Editor and twelve fixed validation entries.
See [docs/editor-validation.md](docs/editor-validation.md) for exact names and paths.
No arbitrary C#, menus, builds, or broad asset mutation is permitted.

Normal refresh is automatic through the update pump, without focus. On transient
reload disconnect, poll the same refresh request ID rather than repeating the mutation.
HTTP accepted is not passed; inspect finished/passed and compiler state.
Keep the background Play lease temporary and restore the original runtime flag on
completion/interruption.
