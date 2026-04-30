import { assertPathOrGuid } from "../lib/tool-utils.js";

function assertAssetsOutputFolder(value) {
  if (value === undefined || value === null || value === "") {
    return;
  }

  const normalized = String(value).replace(/\\/g, "/").replace(/\/+$/, "");
  if (
    (normalized !== "Assets" && !normalized.startsWith("Assets/"))
    || normalized.split("/").includes("..")
  ) {
    throw new Error("'outputFolder' must be a Unity asset path under Assets/.");
  }
}

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
    description: "Build a material export spec for downstream transfer workflows.",
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
  {
    name: "create_material_transfer_package",
    description: "Create Unity project migration artifacts for a material without modifying the source material.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
        outputFolder: { type: "string", default: "Assets/MCPExports" },
        packageName: { type: "string" },
        exportProfile: { type: "string", description: "Transfer profile, for example ue-pbr", default: "ue-pbr" },
        includeShaderGraph: { type: "boolean", default: true },
        recursiveShaderGraphs: { type: "boolean", default: true },
        includeRawProperties: { type: "boolean", default: true },
        dryRun: { type: "boolean", default: false },
        overwrite: { type: "boolean", default: false },
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
    case "create_material_transfer_package":
      assertPathOrGuid(args);
      assertAssetsOutputFolder(args.outputFolder);
      return client.post("/api/materials/transfer-package", args);
    default:
      return null;
  }
}
