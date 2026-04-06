export const projectTools = [
  {
    name: "get_project_settings",
    description: "Read Player Settings, current build target, and key Graphics Settings.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  },
  {
    name: "get_project_packages",
    description: "Read package dependencies from the Unity project's manifest.json.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  },
];

export async function callProjectTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_project_settings":
      return client.get("/api/project/settings", args);
    case "get_project_packages":
      return client.get("/api/project/packages", args);
    default:
      return null;
  }
}
