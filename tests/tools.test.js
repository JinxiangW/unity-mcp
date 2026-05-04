import test from "node:test";
import assert from "node:assert/strict";
import { callUnityTool, tools } from "../src/tools/index.js";

function makeClient() {
  return {
    calls: [],
    async get(path, args) {
      this.calls.push({ method: "GET", path, args });
      return { ok: true, method: "GET", path, args };
    },
    async post(path, args) {
      this.calls.push({ method: "POST", path, args });
      return { ok: true, method: "POST", path, args };
    },
  };
}

test("tool registry includes all read-only query endpoints", () => {
  const names = tools.map((tool) => tool.name);
  for (const requiredName of [
    "get_asset_info",
    "get_asset_dependencies",
    "find_assets",
    "get_material_info",
    "get_shader_info",
    "find_materials_using_shader",
    "get_shadergraph_info",
    "get_scene_info",
    "get_scene_renderers",
    "get_pipeline_info",
    "get_scene_lights",
    "get_scene_volumes",
    "get_prefab_info",
    "get_texture_info",
    "get_animation_info",
    "get_project_settings",
    "get_project_packages",
  ]) {
    assert.ok(names.includes(requiredName), `${requiredName} missing`);
  }

  assert.equal(names.length, 17, "MCP should expose exactly 17 read-only tools");
});

test("callUnityTool dispatches to prefab route", async () => {
  const client = makeClient();
  const result = await callUnityTool("get_prefab_info", client, { path: "Assets/Test.prefab" });
  assert.equal(result.path, "/api/prefabs/info");
  assert.deepEqual(client.calls[0], {
    method: "GET",
    path: "/api/prefabs/info",
    args: { path: "Assets/Test.prefab" },
  });
});

test("callUnityTool forwards scene filters as query params", async () => {
  const client = makeClient();
  const result = await callUnityTool("get_scene_lights", client, {
    scenePath: "Assets/Scenes/Sample.unity",
    layers: ["Default", "UI"],
    tag: "Gameplay",
  });

  assert.equal(result.path, "/api/scenes/lights");
  assert.deepEqual(client.calls[0], {
    method: "GET",
    path: "/api/scenes/lights",
    args: {
      scenePath: "Assets/Scenes/Sample.unity",
      layers: ["Default", "UI"],
      tag: "Gameplay",
    },
  });
});
