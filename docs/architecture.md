# Architecture

## Goal

Build a small, stable, read-only Unity MCP for rendering TA workflows in Unity 6.3.

## Layers

### 1. MCP layer

File: `src/index.js`

- Exposes stdio MCP tools
- Validates user input shape
- Forwards requests to Unity local HTTP service
- Normalizes a small amount of output when Unity returns noisy results
- Should stay thin and predictable

### 2. Unity transport layer

File: `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpServer.cs`

- Runs inside Unity Editor
- Starts local HTTP listener on `127.0.0.1:51234`
- Routes GET requests to query functions
- Marshals work onto Unity main thread
- Returns structured JSON envelopes

### 3. Unity query layer

File: `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpQueries.cs`

- Talks to `AssetDatabase`, scene APIs, renderer/material/shader APIs
- Owns domain logic
- Should be the main place for new read-only capabilities

### 4. Shader Graph parsing layer

File: `unity-package/com.ta.readonly-unity-mcp/Editor/UnityShaderGraphTextParser.cs`

- Parses `.shadergraph` and `.shadersubgraph` text data
- Best-effort by design
- Focuses on graph structure, not compiled platform output

## Core invariants

- Unity package is embedded and self-hosted inside the Editor
- HTTP is local-only and read-only
- MCP server is stateless and restartable
- Query code is allowed to be version-aware and best-effort
- Tool contracts should be stable even if Unity internals vary slightly

## Current capability map

- Asset: info, dependencies, reverse references, search
- Material: shader linkage, keywords, property values
- Material export: transferable semantics, texture export planning, optional recursive Shader Graph bundle data
- Shader: properties, keywords, passes, usage lookup
- Shader Graph: properties, keywords, nodes, edges, subgraphs, targets
- Scene: active/loaded scenes, renderer-material bindings, lights, probes, volumes

## Recommended growth order

1. Strengthen current result quality
2. Add more render-analysis queries
3. Add relation-tracing helpers
4. Only then consider heavier diagnostics

## Things intentionally out of scope

- Build pipeline
- Enter/exit play mode
- Menu execution
- Arbitrary editor scripting
- Asset mutation
- Final compiled shader variant analysis as a first-class feature

## Planned compatibility abstraction

If the repo expands to support multiple Unity versions, keep the protocol stable and isolate version differences in a `Compat` layer inside the Unity package. See `docs/multi-version-compatibility.md`.
