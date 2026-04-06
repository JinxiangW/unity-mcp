import test from "node:test";
import assert from "node:assert/strict";
import {
  assertPathOrGuid,
  normalizeFindAssetsResult,
  normalizeMaterialSearchResult,
} from "../src/lib/tool-utils.js";

test("assertPathOrGuid rejects missing path and guid", () => {
  assert.throws(() => assertPathOrGuid({}), /Either 'path' or 'guid' is required/);
});

test("normalizeMaterialSearchResult keeps only .mat entries", () => {
  const result = normalizeMaterialSearchResult({
    data: {
      materials: [
        { path: "Assets/A.mat" },
        { path: "Assets/B.shadergraph" },
      ],
    },
  });

  assert.equal(result.data.materialCount, 1);
  assert.deepEqual(result.data.materials, [{ path: "Assets/A.mat" }]);
});

test("normalizeFindAssetsResult only filters material searches", () => {
  const unchanged = normalizeFindAssetsResult({ data: { assets: [{ path: "Assets/T.png" }] } }, { type: "Texture2D" });
  assert.equal(unchanged.data.assets.length, 1);

  const filtered = normalizeFindAssetsResult({
    data: {
      assets: [
        { path: "Assets/A.mat" },
        { path: "Assets/B.prefab" },
      ],
    },
  }, { type: "Material" });

  assert.equal(filtered.data.returned, 1);
  assert.deepEqual(filtered.data.assets, [{ path: "Assets/A.mat" }]);
});
