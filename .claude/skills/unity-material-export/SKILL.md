---
name: unity-material-export
description: Export Unity materials for downstream transfer (UE, etc.). Produces manifest, textures, shader sources, and shader graph data.
---

## What this skill is for

Export a Unity material as a transfer package for downstream engines (Unreal Engine, custom renderers, etc.). The output includes:

- `manifest.json` — structured material analysis (PBR semantics, surface info, texture plans)
- `shadergraph.json` — Shader Graph structure (when applicable)
- `subgraphs/` — recursive subgraph JSON files
- `textures/` — copied texture assets
- `shader_sources/` — HLSL custom function sources with resolved `#include` chains
- `graph-io-summary.json` — optional I/O connection summary

This skill uses the `unity-mcp` MCP server for read queries and `scripts/export-material-package.js` for the full export workflow.

## Prerequisites

- Unity Editor is open with the MCP bridge running (`curl http://127.0.0.1:51234/health` returns 200)
- The material exists in the project

## Workflow A: Full export (recommended)

Run the export script directly. It communicates with the Unity HTTP bridge:

```bash
node scripts/export-material-package.js \
  --path "Assets/Art/Characters/Hero.mat" \
  --out "./exports" \
  --export-profile ue-pbr \
  --include-shadergraph true \
  --recursive-shadergraphs true
```

If you only have the GUID instead of the asset path:

```bash
node scripts/export-material-package.js \
  --guid "abc123def456..." \
  --out "./exports"
```

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `--path` | — | Unity asset path (e.g. `Assets/Materials/Wall.mat`) |
| `--guid` | — | Unity asset GUID (alternative to `--path`) |
| `--out` | `exports` | Output root directory |
| `--base-url` | `http://127.0.0.1:51234` | Unity MCP bridge URL |
| `--export-profile` | `ue-pbr` | Transfer profile name |
| `--include-shadergraph` | `true` | Include Shader Graph bundle |
| `--recursive-shadergraphs` | `true` | Recursively include subgraphs |
| `--include-raw-properties` | `true` | Include raw Unity material properties |

## Workflow B: Preview before export

Use MCP tools to inspect the material before running the full export:

1. **Inspect the material:**
   Use MCP tool `get_material_info` with `path` or `guid` to read material properties, shader linkage, and render queue.

2. **Inspect the shader:**
   Use MCP tool `get_shader_info` to read shader properties, keywords, and passes.

3. **Inspect the Shader Graph (if applicable):**
   Use MCP tool `get_shadergraph_info` to read the graph structure — nodes, edges, properties, keywords, targets.

4. **Check dependencies:**
   Use `get_asset_dependencies` and `get_texture_info` to review texture import settings.

After previewing, run Workflow A to produce the full export.

## Export profiles

The default profile is `ue-pbr`. It maps Unity Standard/URP PBR properties:

| Unity property | Transfer semantic | Target channel |
|---------------|-------------------|---------------|
| `_BaseMap` / `_BaseColor` | `baseColor` | RGB + A (opacity) |
| `_BumpMap` | `normal` | RG (BC5) |
| `_MetallicGlossMap` | `metallicRoughnessMask` | R=metallic, A=roughness |
| `_OcclusionMap` | `occlusion` | R |
| `_EmissionMap` | `emission` | RGB |

Material surface classification (opaque / alpha-clip / transparent) is automatically detected from shader keywords, render queue, and alpha settings.

## What the export produces

Example output structure:

```
exports/Hero/
  manifest.json            ← full material analysis (without graph payload)
  shadergraph.json         ← main Shader Graph structure with slot data
  subgraphs/
    index.json             ← subgraph index with GUID → file mapping
    <subgraph>.json        ← each referenced subgraph
  textures/
    Hero_BaseColor.png
    Hero_Normal.png
    Hero_ORM.png
  shader_sources/
    Assets/Shaders/Custom/
      CustomLighting.hlsl   ← custom function source with resolved includes
```

## Troubleshooting

- If the script times out (`Unity bridge request timed out`), check that Unity is open and the MCP bridge is listening on the expected port
- If textures are missing from the output, verify the material references valid texture assets
- If Shader Graph data is incomplete, the graph may be in a legacy format — the parser handles this best-effort
- Missing shader sources in `missingShaderSources` means the `#include` chain referenced files outside the project or in packages

## Notes

- The script does NOT modify the original material or any source assets
- Custom function HLSL files are copied and `#include` chains are resolved recursively
- The GUID map is built by scanning all `.meta` files under `Assets/` and `Packages/`
- For materials without Shader Graph (built-in shaders, custom code shaders), the export still produces manifest and textures
