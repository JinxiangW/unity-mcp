import { assertPathOrGuid } from "../lib/tool-utils.js";

export const materialTools = [
  {
    name: "get_material_info",
    description: "Read material info, shader linkage, and property values.",
    inputSchema: {
      type: "object",
      properties: { path: { type: "string" }, guid: { type: "string" } },
      additionalProperties: false,
    },
  },
];

export async function callMaterialTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_material_info":
      assertPathOrGuid(args);
      return client.get("/api/materials/info", args);
    default:
      return null;
  }
}
