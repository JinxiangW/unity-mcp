---
name: unity-shadergraph-analysis
description: Analyze Unity Shader Graph structure, I/O connections, and subgraph relationships. Trace data flow through nodes and blocks.
---

## What this skill is for

Analyze Shader Graph structure to understand data flow, I/O connections, and subgraph composition. Two complementary approaches:

- **Quick inspection**: Use MCP `get_shadergraph_info` for direct readout of graph properties, keywords, nodes, edges, and targets
- **Deep analysis**: Run `scripts/summarize-shadergraph-io.js` on an exported material package to trace slot-level connections, block wiring, and subgraph I/O

## Prerequisites

- Unity Editor is open with the MCP bridge running
- For deep analysis: a material export package already exists (see `unity-material-export` skill)

## Workflow A: Quick graph inspection via MCP

Use MCP tool `get_shadergraph_info` with `path` or `guid`:

```json
{
  "path": "Assets/Shaders/Example.shadergraph"
}
```

Returns:
- `properties` — exposed graph properties (color, float, texture, etc.)
- `keywords` — shader keywords with stage info
- `nodes` — all graph nodes with type, slots, and position
- `edges` — connections between node slots
- `targets` — active graph targets (e.g., URP Lit, HDRP Lit)
- `subGraphs` — referenced subgraphs with paths
- `output` — fragment and vertex output blocks
- `summary` — node/edge counts
- `warnings` — parse warnings (legacy schema normalization, etc.)

## Workflow B: Deep I/O analysis

First export the material (see `unity-material-export` skill), then run:

```bash
node scripts/summarize-shadergraph-io.js \
  --root "./exports/MaterialName" \
  --out "graph-io-summary.json"
```

### What the summary includes

**Main graph:**
- `subgraphReferences` — which subgraphs are used and their input slots
- `fragmentOutputs`/`vertexOutputs` — what nodes feed into each fragment/vertex block, with slot-level detail
- `subgraphLinks` — edges connecting subgraph references into the main graph

**Each subgraph:**
- `exposedProperties` — properties exposed to the parent graph
- `nestedSubgraphReferences` — subgraph references within the subgraph
- `outputs` — what feeds into the SubGraphOutputNode (the subgraph's return values)
- `propertyNodeConnections` — where each PropertyNode connects downstream

### Summary schema

```json
{
  "schemaVersion": "unity-shadergraph-io-summary/0.2",
  "material": { "name": "...", "path": "..." },
  "shader": { "name": "...", "path": "..." },
  "surface": { "type": "...", "alphaMode": "..." },
  "semantics": { "keys": ["baseColor", "normal", ...], "custom": {} },
  "mainGraph": {
    "summary": { "nodeCount": 42, "edgeCount": 58, ... },
    "subgraphReferences": [...],
    "fragmentOutputs": [...],
    "vertexOutputs": [...],
    "subgraphLinks": [...]
  },
  "subgraphs": [
    {
      "name": "CustomDissolve",
      "exposedProperties": [...],
      "nestedSubgraphReferences": [...],
      "outputs": [...],
      "propertyNodeConnections": [...]
    }
  ]
}
```

## Understanding slot data

Each slot has:
- `slotId` — numeric slot identifier
- `displayName` — visible name in Shader Graph editor
- `direction` — `"input"` or `"output"`
- `valueType` — shader type (`Vector4`, `Vector3`, `Vector1`, `Boolean`, etc.)
- `shaderOutputName` — the generated shader variable name (outputs only)
- `stageCapability` — which shader stages the slot is available in

## Common analysis tasks

**"Where does this texture end up?"**
1. Find the Texture2D node by `displayName`
2. Trace its output slot through edges
3. Follow downstream nodes to fragment/vertex blocks

**"What subgraphs does this shader use?"**
1. Check `subGraphs` in the main graph
2. For each subgraph, the summary shows exposed properties and outputs
3. Check `subgraphLinks` to see how subgraph outputs connect to the main graph

**"How is the normal map processed?"**
1. Find the normal map property in `properties`
2. Find the PropertyNode feeding from it via `propertyNodeConnections` in summary
3. Trace through sampler nodes and blend nodes
4. Follow to the fragment output block

## Limitations

- Shader Graph parsing is structure-focused, not compiled output
- Legacy format graphs (pre-2020) may have incomplete slot/edge data
- Custom Function nodes are identified by GUID/path but their internal logic is not analyzed
- Keywords from legacy graphs are best-effort and may not match the shader's actual keyword set
