import { assertPathOrGuid, normalizeMaterialSearchResult } from "../lib/tool-utils.js";

export const shaderTools = [
  {
    name: "get_shader_info",
    description: "Read shader metadata, properties, keywords, passes, and usage.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
        includeUsage: { type: "boolean", default: true },
      },
      additionalProperties: false,
    },
  },
  {
    name: "find_materials_using_shader",
    description: "Find materials, and optionally loaded-scene renderers, using a shader.",
    inputSchema: {
      type: "object",
      properties: {
        shaderName: { type: "string", description: "Shader.name" },
        guid: { type: "string", description: "Shader asset GUID" },
        includeRenderers: { type: "boolean", default: true },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_shadergraph_info",
    description: "Read Shader Graph structure, properties, keywords, nodes, edges, and targets.",
    inputSchema: {
      type: "object",
      properties: { path: { type: "string" }, guid: { type: "string" } },
      additionalProperties: false,
    },
  },
];

export async function callShaderTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_shader_info":
      assertPathOrGuid(args);
      return client.get("/api/shaders/info", args);
    case "find_materials_using_shader":
      if (!args.shaderName && !args.guid) {
        throw new Error("Either 'shaderName' or 'guid' is required.");
      }
      return normalizeMaterialSearchResult(await client.get("/api/shaders/materials", args));
    case "get_shadergraph_info":
      assertPathOrGuid(args);
      return client.get("/api/shadergraphs/info", args);
    default:
      return null;
  }
}
