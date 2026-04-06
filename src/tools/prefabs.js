import { assertPathOrGuid } from "../lib/tool-utils.js";

export const prefabTools = [
  {
    name: "get_prefab_info",
    description: "Read prefab hierarchy, component structure, and material references.",
    inputSchema: {
      type: "object",
      properties: { path: { type: "string" }, guid: { type: "string" } },
      additionalProperties: false,
    },
  },
];

export async function callPrefabTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_prefab_info":
      assertPathOrGuid(args);
      return client.get("/api/prefabs/info", args);
    default:
      return null;
  }
}
