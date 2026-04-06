import { assertPathOrGuid } from "../lib/tool-utils.js";

export const animationTools = [
  {
    name: "get_animation_info",
    description: "Read AnimatorController structure or AnimationClip bindings/events.",
    inputSchema: {
      type: "object",
      properties: { path: { type: "string" }, guid: { type: "string" } },
      additionalProperties: false,
    },
  },
];

export async function callAnimationTool(toolName, client, args = {}) {
  switch (toolName) {
    case "get_animation_info":
      assertPathOrGuid(args);
      return client.get("/api/animations/info", args);
    default:
      return null;
  }
}
