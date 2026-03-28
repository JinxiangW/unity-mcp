---
name: unity-readonly-mcp-playbook
description: Maintain and extend this Unity 6.3 read-only MCP without breaking its read-only boundary, layering, or TA-focused API strategy.
license: MIT
compatibility: opencode
metadata:
  repo: unity-mcp
  audience: coding-agents
  focus: maintenance-and-extension
---

## What this skill is for

Use this skill when working on this repo's architecture, maintenance, extension planning, or implementation of new read-only Unity MCP capabilities.

This repo is not a general Unity automation tool. It is a focused, read-only Unity MCP for material and rendering TA workflows.

## First read in a new session

1. `README.md`
2. `docs/architecture.md`
3. `docs/maintenance-strategy.md`
4. `docs/multi-version-compatibility.md`
5. `AGENTS.md`

## Mental model

Keep the repo split into four layers:

1. `src/index.js`
   - stdio MCP server
   - tool definitions
   - input validation
   - HTTP forwarding
   - small output normalization

2. `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpServer.cs`
   - Unity local HTTP host
   - route wiring
   - main-thread dispatch
   - JSON envelopes

3. `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpQueries.cs`
   - real Unity query logic
   - asset/scene/material/shader inspection
   - the main place to add new retrieval features

4. `unity-package/com.ta.readonly-unity-mcp/Editor/UnityShaderGraphTextParser.cs`
   - best-effort Shader Graph structure parsing
   - graph-focused, not compiled-platform analysis

## Non-negotiable constraints

- Read-only only
- No build control
- No playmode control
- No arbitrary C# execution
- No `set`, `modify`, `execute`, or hidden write side effects
- Return structured JSON only
- Stay focused on TA workflows: retrieval, analysis, tracing, usage lookup

## Extension rule

Every feature should follow this path:

1. Add query logic in `UnityReadOnlyMcpQueries.cs`
2. Expose a Unity HTTP route in `UnityReadOnlyMcpServer.cs`
3. Add/update the MCP tool contract in `src/index.js`
4. Update `README.md` or `docs/`

Do not start by changing MCP output shape unless you already understand the Unity-side source of truth.

## Where to fix things

- Fix in Unity query layer when the raw data selection is wrong
- Fix in Unity server layer when routing or transport behavior is wrong
- Fix in MCP layer when the issue is input validation, UX, or noisy result normalization

If Unity returns unstable or version-specific data, prefer best-effort output over hard failure.

## Output design guidance

Prefer stable analysis-friendly objects:

- include `name`, `path`, `guid`, `type` when relevant
- make renderer -> material -> shader relationships explicit
- prefer additive fields over renaming or deleting old ones
- keep arrays of structured objects instead of flattening into strings

## Fast verification

When Unity is open, verify in this order:

1. `curl http://127.0.0.1:51234/health`
2. `curl http://127.0.0.1:51234/api/scenes/info`
3. one asset-level query relevant to the change
4. one shader or shadergraph query relevant to the change

If a feature changes material/shader/scene relations, test against a real open Unity project instead of only reasoning from code.

## Definition of done

- Unity compiles cleanly
- `/health` responds
- changed or new route returns JSON and fails safely
- docs mention the capability
- read-only constraints still hold

## Good future directions

- stronger Shader -> Material -> Renderer chain summaries
- better Shader Graph reference-name normalization
- render-asset dependency helpers
- scene-level material/shader usage summaries
- importer readers for rendering assets

## Avoid unless the repo goal changes

- build or player tooling
- playmode automation
- generic Unity editor execution features
- compiled shader variant/platform output analysis as a first-class early feature

## If you are asked to plan changes

Produce plans in this order:

1. user workflow solved
2. affected layer(s)
3. response shape
4. verification steps
5. docs updates
