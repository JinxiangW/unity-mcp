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

test("tool registry includes new additive endpoints", () => {
  const names = tools.map((tool) => tool.name);
  for (const requiredName of [
    "get_pipeline_info",
    "get_scene_lights",
    "get_scene_volumes",
    "get_prefab_info",
    "get_texture_info",
    "get_animation_info",
    "get_project_settings",
    "get_project_packages",
    "create_material_transfer_package",
  ]) {
    assert.ok(names.includes(requiredName), `${requiredName} missing`);
  }
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

test("callUnityTool posts material transfer package requests", async () => {
  const client = makeClient();
  const result = await callUnityTool("create_material_transfer_package", client, {
    path: "Assets/Test.mat",
    outputFolder: "Assets/MCPExports",
    dryRun: true,
  });

  assert.equal(result.method, "POST");
  assert.equal(result.path, "/api/materials/transfer-package");
  assert.deepEqual(client.calls[0], {
    method: "POST",
    path: "/api/materials/transfer-package",
    args: {
      path: "Assets/Test.mat",
      outputFolder: "Assets/MCPExports",
      dryRun: true,
    },
  });
});

test("create_material_transfer_package validates material locator and output folder", async () => {
  const client = makeClient();
  await assert.rejects(
    () => callUnityTool("create_material_transfer_package", client, { outputFolder: "Assets/MCPExports" }),
    /Either 'path' or 'guid' is required/,
  );

  await assert.rejects(
    () => callUnityTool("create_material_transfer_package", client, {
      path: "Assets/Test.mat",
      outputFolder: "Packages/Exports",
    }),
    /outputFolder/,
  );
});

test("create_material_transfer_package forwards overwrite flag", async () => {
  const client = makeClient();
  await callUnityTool("create_material_transfer_package", client, {
    guid: "abc123",
    outputFolder: "Assets/MCPExports",
    overwrite: true,
  });

  assert.deepEqual(client.calls[0], {
    method: "POST",
    path: "/api/materials/transfer-package",
    args: {
      guid: "abc123",
      outputFolder: "Assets/MCPExports",
      overwrite: true,
    },
  });
});
