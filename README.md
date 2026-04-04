# Read-only Unity MCP

This project implements a focused Unity 6.3 MCP for material and rendering TA workflows.

It is split into two pieces:

- `src/index.js`: a stdio MCP server that exposes read-only tools.
- `unity-package/com.ta.readonly-unity-mcp`: a Unity Editor package that serves local HTTP JSON endpoints backed by `AssetDatabase`, `SceneManager`, `EditorSceneManager`, and `ShaderUtil`.

## What it supports

- Asset info, dependencies, and reverse-reference lookup
- Material info and shader linkage
- Material export specs for downstream transfer workflows
- Shader properties, keywords, fallback, custom editor, and usage lookup
- Shader Graph structure inspection
- Loaded scene info and renderer-material bindings

## What it does not do

- No build or playmode control
- No menu-command execution
- No arbitrary C# execution
- No write or modify endpoints

## Folder layout

- `src/index.js`
- `package.json`
- `unity-package/com.ta.readonly-unity-mcp/package.json`
- `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpBootstrap.cs`
- `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpServer.cs`
- `unity-package/com.ta.readonly-unity-mcp/Editor/UnityReadOnlyMcpQueries.cs`
- `unity-package/com.ta.readonly-unity-mcp/Editor/UnityShaderGraphTextParser.cs`

## Local setup

1. Install Node dependencies:

```bash
npm install
```

2. Add the Unity package as an embedded package in your Unity project, for example by copying `unity-package/com.ta.readonly-unity-mcp` to `Packages/com.ta.readonly-unity-mcp`.

3. Open the Unity project in the Editor. The package auto-starts a local read-only HTTP service on `http://127.0.0.1:51234` by default.

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
    "readonly-unity": {
      "command": "node",
      "args": ["D:/unity-mcp/src/index.js"],
      "env": {
        "UNITY_MCP_BASE_URL": "http://127.0.0.1:51234"
      }
    }
  }
}
```

If you need a different port, set `UNITY_READONLY_MCP_PORT` or `UNITY_MCP_PORT` in both the Unity Editor environment and the MCP client environment.

## Exposed MCP tools

- `get_asset_info`
- `get_asset_dependencies`
- `find_assets`
- `get_material_info`
- `get_material_export_spec`
- `get_shader_info`
- `find_materials_using_shader`
- `get_shadergraph_info`
- `get_scene_info`
- `get_scene_renderers`

## Notes

- `get_scene_info` and `get_scene_renderers` only inspect currently loaded scenes in the Editor.
- Shader Graph parsing is intentionally read-only and structure-focused.
- Shader usage lookups are exact by shader asset when a shader path or GUID is available. Name-only lookups remain best-effort and can still be ambiguous if a project contains multiple shaders with the same `Shader.name`.
- `get_material_export_spec` returns a transfer-oriented JSON spec, suggested export filenames, and optional recursive Shader Graph bundle data, but it does not write files or copy textures.
- `get_material_export_spec` only maps base-map alpha to `opacity` when the material is transparent or alpha-clipped; opaque materials no longer claim opacity from base-map alpha by default.
- `scripts/export-material-package.js` is the write-side bridge that consumes `get_material_export_spec` and writes `manifest.json`, optional Shader Graph bundle files, and copied textures into an output directory.
- Shader Graph output is best-effort across Unity package versions; malformed or drifting graph files return stable JSON with `warnings` and, when needed, `parseError` instead of failing the whole route.
- The Unity HTTP bridge is intended for local tooling, not browser clients. It does not enable cross-origin browser access, and `/health` avoids returning the project absolute path.

## Maintenance docs

- `AGENTS.md`
- `docs/architecture.md`
- `docs/maintenance-strategy.md`
- `docs/multi-version-compatibility.md`

## Repo-local skill

- `.opencode/skills/unity-readonly-mcp-playbook/SKILL.md`
- `.opencode/skills/unity-version-adaptation-playbook/SKILL.md`
