---
name: unity-mcp
description: Unity Editor data access and TA workflows. Use when the user asks about Unity materials, shaders, scenes, textures, prefabs, animations, project settings, or wants to export Unity materials for downstream transfer.
when_to_use: |
  Trigger phrases: Unity material/shader/scene/prefab/texture/project info, Unity asset lookup, Shader Graph analysis, export Unity material to UE/Unreal, find materials using a shader, Unity render pipeline info.
---

## What this skill covers

Entry point for Unity Editor data access via MCP. The MCP server exposes 17 read-only query tools. Higher-level TA workflows are documented in supporting files — read them when the corresponding task comes up.

## MCP tools (17 read-only queries)

### Asset
- `get_asset_info` — asset metadata, dependencies, reverse references
- `get_asset_dependencies` — direct/recursive dependency tree
- `find_assets` — search by type and name filter

### Material
- `get_material_info` — material properties, shader linkage, render queue

### Shader
- `get_shader_info` — properties, keywords, passes, usage
- `find_materials_using_shader` — reverse lookup by shader name or GUID
- `get_shadergraph_info` — graph structure, nodes, edges, targets

### Scene
- `get_scene_info` — loaded scenes, renderer/light/probe/volume counts
- `get_scene_renderers` — renderer → material bindings
- `get_pipeline_info` — active render pipeline and quality levels
- `get_scene_lights` — lights with type, intensity, color, shadows
- `get_scene_volumes` — post-process volumes with profile overrides

### Prefab / Texture / Animation
- `get_prefab_info` — hierarchy, components, material references
- `get_texture_info` — importer settings, platform overrides
- `get_animation_info` — AnimatorController / AnimationClip structure

### Project
- `get_project_settings` — player, graphics, quality settings
- `get_project_packages` — manifest.json package list

## Sub-workflows

When one of these tasks comes up, read the corresponding file:

- **[material-export.md](material-export.md)** — Load when the user asks to **export a material** for Unreal Engine or another downstream engine, needs a transfer package with textures/shader sources, or asks about PBR semantic mapping (`ue-pbr`). Uses `scripts/export-material-package.js`.
- **[shadergraph-analysis.md](shadergraph-analysis.md)** — Load when the user needs **deep Shader Graph I/O tracing**: slot-level data flow, subgraph input/output mapping, fragment/vertex block wiring, or property node → downstream connectivity. For quick graph overview use `get_shadergraph_info` directly.

## Prerequisites

- Unity Editor open with MCP bridge on `http://127.0.0.1:51234`
- `curl http://127.0.0.1:51234/health` returns 200

## Notes

- All MCP tools are read-only. No write/modify/builder operations.
- Shader Graph parsing is best-effort across Unity versions (legacy schema normalization built in).
- For Unity 2021.3–2022.1, shader introspection uses ShaderUtil reflection fallbacks.
- Scene queries only inspect currently loaded scenes.
