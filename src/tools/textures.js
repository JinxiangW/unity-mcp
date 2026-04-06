import { assertPathOrGuid } from "../lib/tool-utils.js";

export const textureTools = [
  {
    name: "get_texture_info",
    description: "Read texture asset metadata and importer/platform override settings.",
    inputSchema: {
      type: "object",
      properties: { path: { type: "string" }, guid: { type: "string" } },
      additionalProperties: false,
    },
  },
];

export async function callTextureTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_texture_info":
      assertPathOrGuid(args);
      return client.get("/api/textures/info", args);
    default:
      return null;
  }
}
