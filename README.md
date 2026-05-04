# Unity MCP

This project implements a focused Unity MCP for material and rendering TA workflows. The MCP layer exposes only basic low-level read queries. Higher-level workflows (material export, Shader Graph I/O analysis) are available as **Claude Code skills** that orchestrate MCP tools and standalone scripts.

It is split into three pieces:

- `src/index.js`: a stdio MCP server that exposes 17 read-only Unity query tools.
- `unity-package/com.ta.unity-mcp`: a Unity Editor package that serves local HTTP JSON endpoints backed by `AssetDatabase`, `SceneManager`, `EditorSceneManager`, and `ShaderUtil`.
- `.claude/skills/`: Claude Code skills for high-level TA workflows (material export, shader graph analysis).

## What it supports

- Asset info, dependencies, and reverse-reference lookup
- Material info and shader linkage
- Shader properties, keywords, fallback, custom editor, and usage lookup
- Shader Graph structure inspection
- Loaded scene info and renderer-material bindings
- Render pipeline and active quality-level bindings
- Detailed scene light and volume inspection with filters
- Prefab hierarchy/component/material inspection
- Texture importer and platform override inspection
- AnimatorController / AnimationClip structure inspection
- Project settings and package manifest inspection

## What it does not do

- No build or playmode control
- No menu-command execution
- No arbitrary C# execution
- No arbitrary write or modify endpoints; V1 writes only material migration artifacts under `Assets/`

## Folder layout

- `src/index.js`
- `package.json`
- `unity-package/com.ta.unity-mcp/package.json`
- `unity-package/com.ta.unity-mcp/Editor/UnityMcpBootstrap.cs`
- `unity-package/com.ta.unity-mcp/Editor/UnityMcpServer.cs`
- `unity-package/com.ta.unity-mcp/Editor/UnityMcpQueries.cs`
- `unity-package/com.ta.unity-mcp/Editor/UnityShaderGraphTextParser.cs`

## Local setup

1. Install Node dependencies:

```bash
npm install
```

2. Add the Unity package as an embedded package in your Unity project, for example by copying `unity-package/com.ta.unity-mcp` to `Packages/com.ta.unity-mcp`.

3. Open the Unity project in the Editor. The package auto-starts a local HTTP service on `http://127.0.0.1:51234` by default.

4. Run the MCP server:

```bash
npm start
```

Optional: export a material package from the Unity HTTP bridge:

```bash
npm run export-material -- --path "Assets/Art/Wings/Wing_L.mat" --out "D:/exports"
```

5. Point your MCP client to the stdio server. Example config:

```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": ["D:/unity-mcp/src/index.js"],
      "env": {
        "UNITY_MCP_BASE_URL": "http://127.0.0.1:51234"
      }
    }
  }
}
```

If you need a different port, set `UNITY_MCP_PORT` in both the Unity Editor environment and the MCP client environment.

## Exposed MCP tools (17 read-only queries)

- `get_asset_info`
- `get_asset_dependencies`
- `find_assets`
- `get_material_info`
- `get_shader_info`
- `find_materials_using_shader`
- `get_shadergraph_info`
- `get_scene_info`
- `get_scene_renderers`
- `get_pipeline_info`
- `get_scene_lights`
- `get_scene_volumes`
- `get_prefab_info`
- `get_texture_info`
- `get_animation_info`
- `get_project_settings`
- `get_project_packages`

Higher-level workflows (material export, Shader Graph I/O analysis) are available as skills — see `.claude/skills/`.

## Notes

- `get_scene_info` and `get_scene_renderers` only inspect currently loaded scenes in the Editor.
- `get_scene_lights` and `get_scene_volumes` support optional `scenePath`, `layers`, and `tag` filters.
- Shader Graph parsing is structure-focused.
- Shader usage lookups are exact by shader asset when a shader path or GUID is available. Name-only lookups remain best-effort and can still be ambiguous if a project contains multiple shaders with the same `Shader.name`.
- Shader Graph output is best-effort across Unity package versions; malformed or drifting graph files return stable JSON with `warnings` and, when needed, `parseError` instead of failing the whole route.
- For material export and Shader Graph I/O analysis, use the skills in `.claude/skills/` or the scripts in `scripts/`.
- `/health` includes timeout and compatibility metadata for quick diagnostics.
- `UNITY_MCP_TIMEOUT_MS` configures the JS-side timeout and is mirrored by the Unity-side main-thread timeout with a small safety buffer.
- `UNITY_MCP_LOG_REQUESTS=1` enables Unity-side request/response timing logs for diagnostics.
- The Unity HTTP bridge is intended for local tooling, not browser clients. It does not enable cross-origin browser access, and `/health` avoids returning the project absolute path.

## Maintenance docs

- `AGENTS.md`
- `docs/architecture.md`
- `docs/maintenance-strategy.md`
- `docs/multi-version-compatibility.md`

## Claude Code skills

- `.claude/skills/unity-material-export/SKILL.md` — Material export workflow for downstream transfer
- `.claude/skills/unity-shadergraph-analysis/SKILL.md` — Shader Graph I/O analysis and subgraph tracing

## Agent playbooks (OpenCode)

- `.opencode/skills/unity-mcp-playbook/SKILL.md`
- `.opencode/skills/unity-version-adaptation-playbook/SKILL.md`
