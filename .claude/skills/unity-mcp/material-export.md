# Unity Material Export

Export a Unity material as a transfer package for downstream engines (Unreal Engine, custom renderers, etc.).

## Prerequisites

- Unity Editor is open with the MCP bridge running (`curl http://127.0.0.1:51234/health` returns 200)
- The material exists in the project

## Full export (recommended)

Run the export script directly. It communicates with the Unity HTTP bridge:

```bash
node scripts/export-material-package.js \
  --path "Assets/Art/Characters/Hero.mat" \
  --out "./exports" \
  --export-profile ue-pbr \
  --include-shadergraph true \
  --recursive-shadergraphs true
```

If you only have the GUID:

```bash
node scripts/export-material-package.js \
  --guid "abc123def456..." \
  --out "./exports"
```

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `--path` | — | Unity asset path |
| `--guid` | — | Unity asset GUID (alternative to `--path`) |
| `--out` | `exports` | Output root directory |
| `--base-url` | `http://127.0.0.1:51234` | Unity MCP bridge URL |
| `--export-profile` | `ue-pbr` | Transfer profile name |
| `--include-shadergraph` | `true` | Include Shader Graph bundle |
| `--recursive-shadergraphs` | `true` | Recursively include subgraphs |
| `--include-raw-properties` | `true` | Include raw Unity material properties |

## Preview before export

Use MCP tools to inspect the material before running the full export:

1. **Inspect the material:** `get_material_info` with `path` or `guid`
2. **Inspect the shader:** `get_shader_info` for properties, keywords, passes
3. **Inspect the Shader Graph:** `get_shadergraph_info` for nodes, edges, targets
4. **Check dependencies:** `get_asset_dependencies` and `get_texture_info`

## Export profiles

The default `ue-pbr` profile maps Unity Standard/URP PBR properties:

| Unity property | Transfer semantic | Target channel |
|---------------|-------------------|---------------|
| `_BaseMap` / `_BaseColor` | `baseColor` | RGB + A (opacity) |
| `_BumpMap` | `normal` | RG (BC5) |
| `_MetallicGlossMap` | `metallicRoughnessMask` | R=metallic, A=roughness |
| `_OcclusionMap` | `occlusion` | R |
| `_EmissionMap` | `emission` | RGB |

Material surface classification (opaque / alpha-clip / transparent) is auto-detected from shader keywords, render queue, and alpha settings.

## Output structure

```
exports/MaterialName/
  manifest.json            ← full material analysis (without graph payload)
  shadergraph.json         ← main Shader Graph structure with slot data
  subgraphs/
    index.json             ← subgraph index with GUID → file mapping
    <subgraph>.json        ← each referenced subgraph
  textures/
    *.png                  ← copied textures
  shader_sources/
    **/*.hlsl              ← custom function sources with resolved #include chains
```

## Troubleshooting

- **Timeout**: Check Unity is open and MCP bridge is listening
- **Missing textures**: Verify material references valid texture assets
- **Incomplete Shader Graph data**: Graph may be in legacy format — parser handles this best-effort
- **Missing shader sources** (`missingShaderSources`): `#include` chain referenced files outside project/packages
- The script does NOT modify original material or source assets
