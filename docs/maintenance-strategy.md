# Maintenance And Extension Strategy

## Repo maintenance priorities

### 1. Protect the read-only boundary

Any change that can mutate the Unity project should be rejected unless the repo goal changes explicitly.

### 2. Keep contracts stable

- Avoid breaking existing tool names
- Avoid renaming JSON fields unless there is a strong reason
- Prefer adding fields to removing fields

### 3. Keep responsibilities separated

- Query logic belongs in Unity query files
- Transport/routing belongs in the Unity server file
- MCP-only input validation and normalization belongs in `src/index.js`

### 4. Prefer best-effort, explicit behavior

If Unity data is incomplete or version-specific:

- return partial data
- keep shape stable
- avoid hard crashes when possible
- document the limitation

## How to extend the repo

Use this checklist for every new tool or major query.

### Step A. Define the TA workflow

State the exact user question the tool should answer.

Examples:

- Which materials use this shader?
- Which renderers in the loaded scene use those materials?
- What subgraphs and keywords exist in this shader graph?

### Step B. Decide the layer

- If it is pure Unity data retrieval, add it in `UnityReadOnlyMcpQueries.cs`
- If it needs a new route, wire it in `UnityReadOnlyMcpServer.cs`
- If it needs a user-facing tool, expose it in `src/index.js`

### Step C. Keep the output shaped for analysis

Prefer outputs that are easy for both humans and agents to consume:

- include `name`, `path`, `guid`, `type` when applicable
- include direct links between renderer, material, shader, shadergraph
- use arrays of objects rather than ad hoc nested strings

### Step D. Verify with real project data

Minimum smoke test:

- `/health`
- one query for the new route
- one nearby existing query to make sure no regression was introduced

## Result quality policy

When Unity returns awkward or mixed results, use this rule:

- fix at Unity layer if the issue is clearly a query problem
- fix at MCP layer if the issue is presentation/noise filtering and you want to preserve Unity query reuse

## Recommended docs to keep current

- `README.md`: setup and current tool surface
- `docs/architecture.md`: structural overview
- `docs/maintenance-strategy.md`: extension rules
- `docs/multi-version-compatibility.md`: cross-version abstraction plan
- `AGENTS.md`: fast session bootstrap for future coding agents

## Good next extensions

- Shader -> Material -> Renderer chain as a single summarized query
- More robust Shader Graph reference-name normalization
- Scene-level material usage summaries
- Import-settings readers for common rendering assets
- Dependency-chain helpers for render assets

## Avoid these until later

- platform-specific compiled shader output analysis
- anything that requires build steps or scene execution
- broad generic Unity tooling outside rendering TA workflows
