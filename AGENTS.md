# Unity Read-only MCP Playbook

This repo is a focused, read-only Unity MCP for material and rendering TA workflows.

## First read in a new session

1. `README.md`
2. `docs/architecture.md`
3. `docs/maintenance-strategy.md`
4. `docs/multi-version-compatibility.md`

## Project shape

- `src/index.js`: stdio MCP server, tool definitions, HTTP forwarding, response normalization
- `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpServer.cs`: Unity local HTTP host
- `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpQueries.cs`: query layer over Unity Editor APIs
- `unity-package/com.ta.readonly-unity-mcp/Editor/UnityShaderGraphTextParser.cs`: Shader Graph structure parser
- `docs/`: architecture, maintenance, extension guidance

## Non-negotiable constraints

- Read-only only
- No build control
- No playmode control
- No arbitrary C# execution
- No `set`, `modify`, `execute`, or hidden write side effects
- Return structured JSON only

## Design rule

Every feature should fit this path:

1. Unity query logic in `UnityReadOnlyMcpQueries.cs`
2. Unity HTTP route in `UnityReadOnlyMcpServer.cs`
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
- Existing read-only constraints still hold
