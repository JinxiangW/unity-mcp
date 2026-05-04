# Unity Shader Graph Analysis

Analyze Shader Graph structure, I/O connections, and subgraph relationships.

## Prerequisites

- Unity Editor open with MCP bridge running
- For deep analysis: a material export package already exists (see `material-export.md`)

## Quick inspection via MCP

Use `get_shadergraph_info` with `path` or `guid`:

```json
{ "path": "Assets/Shaders/Example.shadergraph" }
```

Returns: `properties`, `keywords`, `nodes` (with type, slots, position), `edges`, `targets`, `subGraphs`, `output` (fragment/vertex blocks), `summary`, `warnings`.

## Deep I/O analysis

First export the material (see `material-export.md`), then:

```bash
node scripts/summarize-shadergraph-io.js \
  --root "./exports/MaterialName" \
  --out "graph-io-summary.json"
```

### Summary output (schema `unity-shadergraph-io-summary/0.2`)

**Main graph:**
- `subgraphReferences` — which subgraphs are used and their input slots
- `fragmentOutputs` / `vertexOutputs` — what nodes feed into each block, with slot-level detail
- `subgraphLinks` — edges connecting subgraph references into the main graph

**Each subgraph:**
- `exposedProperties` — properties exposed to the parent graph
- `nestedSubgraphReferences` — deeper subgraph references
- `outputs` — edges feeding SubGraphOutputNode (the subgraph's return values)
- `propertyNodeConnections` — where each PropertyNode connects downstream

### Slot data fields

| Field | Description |
|-------|-------------|
| `slotId` | Numeric slot identifier |
| `displayName` | Visible name in Shader Graph editor |
| `direction` | `"input"` or `"output"` |
| `valueType` | Shader type (`Vector4`, `Vector3`, `Vector1`, `Boolean`, etc.) |
| `shaderOutputName` | Generated shader variable name (outputs only) |
| `stageCapability` | Which shader stages the slot is available in |

## Common analysis tasks

**"Where does this texture end up?"**
1. Find the Texture2D node by `displayName`
2. Trace its output slot through edges
3. Follow downstream nodes to fragment/vertex blocks

**"What subgraphs does this shader use?"**
1. Check `subGraphs` in the main graph
2. For each subgraph: exposed properties, outputs, `subgraphLinks`

**"How is the normal map processed?"**
1. Find the normal map property in `properties`
2. Find the PropertyNode via `propertyNodeConnections` in summary
3. Trace through sampler/blend nodes to the fragment output block

## Limitations

- Shader Graph parsing is structure-focused, not compiled output
- Legacy format graphs (pre-2020) may have incomplete slot/edge data
- Custom Function nodes: identified by GUID/path, internal logic not analyzed
- Keywords from legacy graphs are best-effort
