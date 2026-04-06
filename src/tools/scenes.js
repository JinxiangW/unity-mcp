export const sceneTools = [
  {
    name: "get_scene_info",
    description: "Read currently loaded Unity scene information.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  },
  {
    name: "get_scene_renderers",
    description: "Read renderer to material bindings for loaded scenes or one loaded scene path.",
    inputSchema: {
      type: "object",
      properties: { scenePath: { type: "string", description: "Optional loaded scene path" } },
      additionalProperties: false,
    },
  },
  {
    name: "get_pipeline_info",
    description: "Read the active render pipeline asset and available quality-level pipeline bindings.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  },
  {
    name: "get_scene_lights",
    description: "Read detailed light data for loaded scenes, with optional scene/layer/tag filters.",
    inputSchema: {
      type: "object",
      properties: {
        scenePath: { type: "string" },
        layers: { type: "array", items: { type: "string" } },
        tag: { type: "string" },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_scene_volumes",
    description: "Read detailed volume data for loaded scenes, with optional scene/layer/tag filters.",
    inputSchema: {
      type: "object",
      properties: {
        scenePath: { type: "string" },
        layers: { type: "array", items: { type: "string" } },
        tag: { type: "string" },
      },
      additionalProperties: false,
    },
  },
];

export async function callSceneTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_scene_info":
      return client.get("/api/scenes/info", args);
    case "get_scene_renderers":
      return client.get("/api/scenes/renderers", args);
    case "get_pipeline_info":
      return client.get("/api/pipeline/info", args);
    case "get_scene_lights":
      return client.get("/api/scenes/lights", args);
    case "get_scene_volumes":
      return client.get("/api/scenes/volumes", args);
    default:
      return null;
  }
}
