# TA Unity MCP

Embedded Unity package for a material and rendering MCP workflow.

## Endpoints

- `GET /health`
- `GET /api/assets/info?path=...`
- `GET /api/assets/dependencies?path=...&recursive=true`
- `GET /api/assets/find?type=Material&filter=MyFilter`
- `GET /api/materials/info?path=...`
- `GET /api/materials/export-spec?path=...`
- `POST /api/materials/transfer-package`
- `GET /api/shaders/info?path=...`
- `GET /api/shaders/materials?shaderName=Universal Render Pipeline/Lit`
- `GET /api/shadergraphs/info?path=...`
- `GET /api/scenes/info`
- `GET /api/scenes/renderers?scenePath=Assets/Scenes/Main.unity`

## Behavior

- Auto-starts on editor load
- Listens on `http://127.0.0.1:51234` by default
- Optional port override via `UNITY_MCP_PORT`
- Uses Unity Editor APIs
- Returns structured JSON only

## Scope

- Asset metadata and dependencies
- Material and shader inspection
- Material migration artifact writing under `Assets/`
- Shader Graph structural parsing
- Loaded-scene renderer/material bindings
- Light, Reflection Probe, and Volume summaries

## Limits

- Scene inspection is limited to currently loaded scenes
- Shader Graph parsing is best-effort across package versions
- No arbitrary write APIs, build APIs, or arbitrary execution APIs
