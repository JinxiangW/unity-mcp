import { assetTools, callAssetTool } from "./assets.js";
import { materialTools, callMaterialTool } from "./materials.js";
import { shaderTools, callShaderTool } from "./shaders.js";
import { sceneTools, callSceneTool } from "./scenes.js";
import { prefabTools, callPrefabTool } from "./prefabs.js";
import { textureTools, callTextureTool } from "./textures.js";
import { animationTools, callAnimationTool } from "./animations.js";
import { projectTools, callProjectTool } from "./project.js";

export const tools = [
  ...assetTools,
  ...materialTools,
  ...shaderTools,
  ...sceneTools,
  ...prefabTools,
  ...textureTools,
  ...animationTools,
  ...projectTools,
];

const callers = [
  callAssetTool,
  callMaterialTool,
  callShaderTool,
  callSceneTool,
  callPrefabTool,
  callTextureTool,
  callAnimationTool,
  callProjectTool,
];

export async function callUnityTool(toolName, client, args = {}) {
  for (const caller of callers) {
    const result = await caller(toolName, client, args);
    if (result !== null) {
      return result;
    }
  }

  throw new Error(`Unknown tool: ${toolName}`);
}
