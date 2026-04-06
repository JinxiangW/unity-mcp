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
  {
    name: "get_material_export_spec",
    description: "Build a read-only material export spec for downstream transfer workflows.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
        exportProfile: { type: "string", description: "Transfer profile, for example ue-pbr", default: "ue-pbr" },
        includeShaderGraph: { type: "boolean", default: true },
        recursiveShaderGraphs: { type: "boolean", default: true },
        includeRawProperties: { type: "boolean", default: true },
      },
      additionalProperties: false,
    },
  },
];

export async function callMaterialTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_material_info":
      assertPathOrGuid(args);
      return client.get("/api/materials/info", args);
    case "get_material_export_spec":
      assertPathOrGuid(args);
      return client.get("/api/materials/export-spec", args);
    default:
      return null;
  }
}
