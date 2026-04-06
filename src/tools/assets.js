import { assertPathOrGuid, normalizeFindAssetsResult } from "../lib/tool-utils.js";

export const assetTools = [
  {
    name: "get_asset_info",
    description: "Read basic Unity asset info by path or GUID.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string", description: "Unity asset path, for example Assets/Materials/M_Wall.mat" },
        guid: { type: "string", description: "Unity asset GUID" },
        includeDependencies: { type: "boolean", description: "Include direct dependencies", default: false },
        includeReferencedBy: { type: "boolean", description: "Include assets that directly reference this asset", default: false },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_asset_dependencies",
    description: "Read asset dependency information by path or GUID.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
        recursive: { type: "boolean", default: true },
        includeReferencedBy: { type: "boolean", default: false },
      },
      additionalProperties: false,
    },
  },
  {
    name: "find_assets",
    description: "Find Unity assets by type and search filter.",
    inputSchema: {
      type: "object",
      properties: {
        type: { type: "string", description: "Unity type filter, for example Material, Shader, Texture2D" },
        filter: { type: "string", description: "AssetDatabase filter text" },
        limit: { type: "integer", minimum: 1, maximum: 1000, default: 200 },
      },
      additionalProperties: false,
    },
  },
];

export async function callAssetTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_asset_info":
      assertPathOrGuid(args);
      return client.get("/api/assets/info", args);
    case "get_asset_dependencies":
      assertPathOrGuid(args);
      return client.get("/api/assets/dependencies", args);
    case "find_assets":
      return normalizeFindAssetsResult(await client.get("/api/assets/find", args), args);
    default:
      return null;
  }
}
