import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const DEFAULT_PORT = process.env.UNITY_MCP_PORT || "51234";
const DEFAULT_BASE_URL = process.env.UNITY_MCP_BASE_URL || `http://127.0.0.1:${DEFAULT_PORT}`;
const DEFAULT_TIMEOUT_MS = Number.parseInt(process.env.UNITY_MCP_TIMEOUT_MS || "30000", 10);

function printUsage() {
  process.stdout.write(`Usage:\n  node scripts/export-material-package.js --path <UnityAssetPath> [options]\n  node scripts/export-material-package.js --guid <UnityGuid> [options]\n\nOptions:\n  --out <directory>                 Output root directory. Default: ./exports\n  --base-url <url>                  Unity MCP base URL. Default: ${DEFAULT_BASE_URL}\n  --export-profile <name>           Export profile. Default: ue-pbr\n  --include-shadergraph <bool>      Include shader graph bundle. Default: true\n  --recursive-shadergraphs <bool>   Recursively include subgraphs. Default: true\n  --include-raw-properties <bool>   Include raw Unity properties. Default: true\n  --help                            Show this help\n`);
}

function parseBoolean(value, defaultValue) {
  if (value === undefined) {
    return defaultValue;
  }

  const normalized = String(value).trim().toLowerCase();
  if (["1", "true", "yes", "on"].includes(normalized)) {
    return true;
  }

  if (["0", "false", "no", "off"].includes(normalized)) {
    return false;
  }

  throw new Error(`Invalid boolean value '${value}'. Use true/false.`);
}

function parseArgs(argv) {
  const args = {
    out: "exports",
    baseUrl: DEFAULT_BASE_URL,
    exportProfile: "ue-pbr",
    includeShaderGraph: true,
    recursiveShaderGraphs: true,
    includeRawProperties: true,
  };

  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];

    if (token === "--help") {
      args.help = true;
      continue;
    }

    if (!token.startsWith("--")) {
      throw new Error(`Unexpected argument '${token}'.`);
    }

    const name = token.slice(2);
    const value = argv[index + 1];
    if (value === undefined || value.startsWith("--")) {
      throw new Error(`Missing value for '${token}'.`);
    }

    index += 1;

    switch (name) {
      case "path":
        args.path = value;
        break;
      case "guid":
        args.guid = value;
        break;
      case "out":
        args.out = value;
        break;
      case "base-url":
        args.baseUrl = value;
        break;
      case "export-profile":
        args.exportProfile = value;
        break;
      case "include-shadergraph":
        args.includeShaderGraph = parseBoolean(value, true);
        break;
      case "recursive-shadergraphs":
        args.recursiveShaderGraphs = parseBoolean(value, true);
        break;
      case "include-raw-properties":
        args.includeRawProperties = parseBoolean(value, true);
        break;
      default:
        throw new Error(`Unknown option '${token}'.`);
    }
  }

  if (!args.help && !args.path && !args.guid) {
    throw new Error("Either --path or --guid is required.");
  }

  return args;
}

async function fetchJson(url, timeoutMs) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, {
      method: "GET",
      headers: { Accept: "application/json" },
      signal: controller.signal,
    });

    const text = await response.text();
    let parsed;

    try {
      parsed = text ? JSON.parse(text) : {};
    } catch {
      throw new Error(`Unity bridge returned non-JSON response (${response.status}): ${text}`);
    }

    if (!response.ok) {
      throw new Error(parsed?.error || `Unity bridge request failed with HTTP ${response.status}`);
    }

    if (parsed?.ok === false) {
      throw new Error(parsed?.error || "Unity bridge returned an error response");
    }

    return parsed;
  } catch (error) {
    if (error?.name === "AbortError") {
      throw new Error(`Unity bridge request timed out after ${timeoutMs}ms`);
    }

    throw error;
  } finally {
    clearTimeout(timeout);
  }
}

function buildExportSpecUrl(args) {
  const url = new URL(`${args.baseUrl.replace(/\/$/, "")}/api/materials/export-spec`);
  const params = {
    path: args.path,
    guid: args.guid,
    exportProfile: args.exportProfile,
    includeShaderGraph: args.includeShaderGraph,
    recursiveShaderGraphs: args.recursiveShaderGraphs,
    includeRawProperties: args.includeRawProperties,
  };

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "") {
      continue;
    }

    url.searchParams.set(key, String(value));
  }

  return url;
}

function sanitizeDirectoryName(name) {
  return String(name || "MaterialExport")
    .replace(/[<>:"/\\|?*]+/g, "_")
    .replace(/\s+/g, " ")
    .trim();
}

async function ensureDirectory(filePath) {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
}

async function writeJson(filePath, value) {
  await ensureDirectory(filePath);
  await fs.writeFile(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

async function copyFileIfPresent(sourcePath, destinationPath) {
  try {
    await ensureDirectory(destinationPath);
    await fs.copyFile(sourcePath, destinationPath);
    return true;
  } catch (error) {
    if (error && (error.code === "ENOENT" || error.code === "ENOTDIR")) {
      return false;
    }

    throw error;
  }
}

function normalizeAssetPath(assetPath) {
  return String(assetPath || "").replace(/\\/g, "/");
}

function stripBundlePayloadFromManifest(spec) {
  const manifest = JSON.parse(JSON.stringify(spec));

  if (manifest?.shaderGraph?.bundle) {
    delete manifest.shaderGraph.bundle.mainGraph;
    delete manifest.shaderGraph.bundle.subgraphs;
    delete manifest.shaderGraph.bundle.index;
  }

  return manifest;
}

function parseShaderGraphObjects(text) {
  const objects = [];
  let depth = 0;
  let startIndex = -1;
  let inString = false;
  let escaping = false;

  for (let index = 0; index < text.length; index += 1) {
    const character = text[index];

    if (escaping) {
      escaping = false;
      continue;
    }

    if (inString && character === "\\") {
      escaping = true;
      continue;
    }

    if (character === "\"") {
      inString = !inString;
      continue;
    }

    if (inString) {
      continue;
    }

    if (character === "{") {
      if (depth === 0) {
        startIndex = index;
      }
      depth += 1;
      continue;
    }

    if (character === "}") {
      depth -= 1;
      if (depth === 0 && startIndex >= 0) {
        const slice = text.slice(startIndex, index + 1);
        try {
          objects.push(JSON.parse(slice));
        } catch {
          // Ignore malformed fragments. The export should stay best-effort.
        }
        startIndex = -1;
      }
    }
  }

  return objects;
}

function indexObjectsById(objects) {
  const map = new Map();
  for (const object of objects || []) {
    if (object?.m_ObjectId) {
      map.set(object.m_ObjectId, object);
    }
  }
  return map;
}

function resolveObjectList(token, objectMap) {
  const results = [];
  if (!Array.isArray(token)) {
    return results;
  }

  for (const entry of token) {
    if (entry && typeof entry === "object" && entry.m_Id) {
      const resolved = objectMap.get(entry.m_Id);
      if (resolved) {
        results.push(resolved);
      }
    } else if (entry && typeof entry === "object") {
      results.push(entry);
    }
  }

  return results;
}

function resolveNodeSlots(rawNode, objectMap) {
  return resolveObjectList(rawNode?.m_Slots || rawNode?.m_SerializableSlots, objectMap);
}

function getSlotDirection(rawSlot) {
  switch (rawSlot?.m_SlotType) {
    case 0:
      return "input";
    case 1:
      return "output";
    default:
      return "unknown";
  }
}

function buildSlotExport(rawSlot) {
  return {
    objectId: rawSlot?.m_ObjectId ?? null,
    slotId: rawSlot?.m_Id ?? null,
    displayName: rawSlot?.m_DisplayName ?? rawSlot?.m_Name ?? rawSlot?.m_ShaderOutputName ?? null,
    direction: getSlotDirection(rawSlot),
    valueType: rawSlot?.m_Type ?? null,
    slotType: rawSlot?.m_SlotType ?? null,
    shaderOutputName: rawSlot?.m_ShaderOutputName ?? null,
    stageCapability: rawSlot?.m_StageCapability ?? null,
    hidden: rawSlot?.m_Hidden ?? null,
  };
}

function findNodeSlotName(rawNode, slotId, objectMap) {
  if (slotId == null || !rawNode) {
    return null;
  }

  const slot = resolveNodeSlots(rawNode, objectMap).find((candidate) => candidate?.m_Id === slotId);
  return slot?.m_DisplayName ?? slot?.m_Name ?? slot?.m_ShaderOutputName ?? null;
}

function enrichShaderGraphGraph(graph, rawText) {
  if (!graph || !rawText) {
    return graph;
  }

  const rawObjects = parseShaderGraphObjects(rawText);
  const objectMap = indexObjectsById(rawObjects);
  const nodesById = new Map((graph.nodes || []).map((node) => [node.objectId, node]).filter(([objectId]) => objectId));

  for (const node of graph.nodes || []) {
    const rawNode = objectMap.get(node.objectId);
    if (!rawNode) {
      continue;
    }

    const slots = resolveNodeSlots(rawNode, objectMap).map(buildSlotExport);
    node.slots = slots.length;
    node.inputSlots = slots.filter((slot) => slot.direction === "input");
    node.outputSlots = slots.filter((slot) => slot.direction === "output");
  }

  for (const edge of graph.edges || []) {
    const outputNodeId = edge.outputNodeId ?? null;
    const inputNodeId = edge.inputNodeId ?? null;
    const outputNode = nodesById.get(outputNodeId);
    const inputNode = nodesById.get(inputNodeId);
    const outputRawNode = outputNode ? objectMap.get(outputNode.objectId) : null;
    const inputRawNode = inputNode ? objectMap.get(inputNode.objectId) : null;

    edge.outputSlotName = edge.outputSlotName ?? findNodeSlotName(outputRawNode, edge.outputSlotId, objectMap);
    edge.inputSlotName = edge.inputSlotName ?? findNodeSlotName(inputRawNode, edge.inputSlotId, objectMap);
  }

  return graph;
}

async function loadRawShaderGraphText(projectPath, assetPath) {
  if (!projectPath || !assetPath) {
    return null;
  }

  const absolutePath = path.join(projectPath, assetPath);
  try {
    return await fs.readFile(absolutePath, "utf8");
  } catch {
    return null;
  }
}

async function walkMetaFiles(rootDirectory, results = []) {
  let entries;
  try {
    entries = await fs.readdir(rootDirectory, { withFileTypes: true });
  } catch {
    return results;
  }

  for (const entry of entries) {
    const absolutePath = path.join(rootDirectory, entry.name);
    if (entry.isDirectory()) {
      await walkMetaFiles(absolutePath, results);
      continue;
    }

    if (entry.isFile() && entry.name.endsWith(".meta")) {
      results.push(absolutePath);
    }
  }

  return results;
}

async function buildProjectGuidMap(projectPath) {
  const guidMap = new Map();
  for (const rootName of ["Assets", "Packages"]) {
    const rootDirectory = path.join(projectPath, rootName);
    const metaFiles = await walkMetaFiles(rootDirectory);
    for (const metaFile of metaFiles) {
      let text;
      try {
        text = await fs.readFile(metaFile, "utf8");
      } catch {
        continue;
      }

      const match = text.match(/^guid:\s*([0-9a-f]{32})\s*$/im);
      if (!match) {
        continue;
      }

      const relativeAssetPath = path
        .relative(projectPath, metaFile.slice(0, -5))
        .split(path.sep)
        .join("/");
      guidMap.set(match[1], relativeAssetPath);
    }
  }

  return guidMap;
}

async function enrichShaderGraphBundle(spec) {
  const projectPath = spec?.unityContext?.projectPath;
  const bundle = spec?.shaderGraph?.bundle;
  if (!bundle) {
    return;
  }

  const items = [];
  if (bundle.mainGraph) {
    items.push(bundle.mainGraph);
  }
  for (const subgraph of bundle.subgraphs || []) {
    items.push(subgraph);
  }

  for (const item of items) {
    if (!item?.graph || item.graph.inputSlots || item.graph.outputSlots) {
      continue;
    }

    const rawText = await loadRawShaderGraphText(projectPath, item.asset?.path);
    if (!rawText) {
      continue;
    }

    enrichShaderGraphGraph(item.graph, rawText);
  }
}

function enumerateShaderGraphBundleItems(shaderGraphSection) {
  const bundle = shaderGraphSection?.bundle;
  if (!bundle) {
    return [];
  }

  const items = [];
  if (bundle.mainGraph) {
    items.push(bundle.mainGraph);
  }
  for (const subgraph of bundle.subgraphs || []) {
    items.push(subgraph);
  }
  return items;
}

function parseLocalIncludes(sourceText) {
  const includes = [];
  const regex = /^\s*#include\s+"([^"]+)"/gm;
  let match;
  while ((match = regex.exec(sourceText)) !== null) {
    includes.push(match[1]);
  }
  return includes;
}

function resolveIncludedAssetPath(fromAssetPath, includePath) {
  if (!includePath) {
    return null;
  }

  const normalizedInclude = normalizeAssetPath(includePath);
  if (normalizedInclude.startsWith("Assets/") || normalizedInclude.startsWith("Packages/")) {
    return normalizedInclude;
  }

  const parentDirectory = path.posix.dirname(normalizeAssetPath(fromAssetPath));
  return path.posix.normalize(path.posix.join(parentDirectory, normalizedInclude));
}

function makeShaderSourceExportPath(assetPath) {
  return path.posix.join("shader_sources", normalizeAssetPath(assetPath));
}

async function copyShaderSourceRecursive(projectPath, rootDirectory, assetPath, copiedAssets, copyResults) {
  const normalizedAssetPath = normalizeAssetPath(assetPath);
  if (!normalizedAssetPath || copiedAssets.has(normalizedAssetPath)) {
    return;
  }

  copiedAssets.add(normalizedAssetPath);
  const absolutePath = path.join(projectPath, normalizedAssetPath);
  const exportRelativePath = makeShaderSourceExportPath(normalizedAssetPath);
  const destinationPath = path.join(rootDirectory, exportRelativePath);
  const exists = await copyFileIfPresent(absolutePath, destinationPath);
  if (!exists) {
    copyResults.missing.push({ assetPath: normalizedAssetPath, sourcePath: absolutePath });
    return;
  }

  copyResults.copied.push({
    assetPath: normalizedAssetPath,
    exportFile: exportRelativePath,
  });

  let sourceText = null;
  try {
    sourceText = await fs.readFile(absolutePath, "utf8");
  } catch {
    sourceText = null;
  }

  if (!sourceText) {
    return;
  }

  for (const includePath of parseLocalIncludes(sourceText)) {
    const includeAssetPath = resolveIncludedAssetPath(normalizedAssetPath, includePath);
    if (!includeAssetPath) {
      continue;
    }

    await copyShaderSourceRecursive(projectPath, rootDirectory, includeAssetPath, copiedAssets, copyResults);
  }
}

async function copyCustomFunctionSources(rootDirectory, spec) {
  const projectPath = spec?.unityContext?.projectPath;
  if (!projectPath) {
    return { copied: [], missing: [] };
  }

  const projectGuidMap = await buildProjectGuidMap(projectPath);
  const copyResults = { copied: [], missing: [] };
  const copiedAssets = new Set();
  for (const item of enumerateShaderGraphBundleItems(spec.shaderGraph)) {
    const graph = item?.graph;
    if (!graph?.nodes) {
      continue;
    }

    for (const node of graph.nodes) {
      const customFunction = node?.customFunction;
      const sourcePath = customFunction?.functionSourcePath
        || projectGuidMap.get(customFunction?.functionSourceGuid || "");
      if (!sourcePath) {
        continue;
      }

      customFunction.functionSourcePath = sourcePath;

      await copyShaderSourceRecursive(projectPath, rootDirectory, sourcePath, copiedAssets, copyResults);
      customFunction.sourceExportFile = makeShaderSourceExportPath(sourcePath);

      const includeExportFiles = copyResults.copied
        .filter((entry) => entry.assetPath !== normalizeAssetPath(sourcePath))
        .map((entry) => entry.exportFile);
      if (includeExportFiles.length > 0) {
        customFunction.availableSourceFiles = Array.from(new Set([customFunction.sourceExportFile, ...includeExportFiles]));
      } else {
        customFunction.availableSourceFiles = [customFunction.sourceExportFile];
      }
    }
  }

  return copyResults;
}

async function writeShaderGraphBundle(rootDirectory, shaderGraphSection) {
  if (!shaderGraphSection?.bundle) {
    return { wroteMainGraph: false, wroteSubgraphs: 0 };
  }

  const { bundle } = shaderGraphSection;
  let wroteSubgraphs = 0;

  if (bundle.mainGraphFile && bundle.mainGraph) {
    await writeJson(path.join(rootDirectory, bundle.mainGraphFile), bundle.mainGraph);
  }

  if (bundle.indexFile && bundle.index) {
    await writeJson(path.join(rootDirectory, bundle.indexFile), {
      schemaVersion: "unity-shadergraph-export-index/1.0",
      baseDirectory: bundle.subgraphsDirectory || "subgraphs",
      entries: bundle.index,
    });
  }

  for (const subgraph of bundle.subgraphs || []) {
    if (!subgraph?.exportFile) {
      continue;
    }

    await writeJson(path.join(rootDirectory, subgraph.exportFile), subgraph);
    wroteSubgraphs += 1;
  }

  return {
    wroteMainGraph: Boolean(bundle.mainGraphFile && bundle.mainGraph),
    wroteSubgraphs,
  };
}

async function copyTextures(rootDirectory, spec) {
  const copied = [];
  const missing = [];

  for (const texture of spec.textures || []) {
    const sourcePath = texture?.sourceFile?.absolutePath;
    const relativePath = texture?.exportFile?.relativePath;
    if (!sourcePath || !relativePath) {
      continue;
    }

    const destinationPath = path.join(rootDirectory, relativePath);
    const exists = await copyFileIfPresent(sourcePath, destinationPath);
    if (exists) {
      copied.push(relativePath);
    } else {
      missing.push({ sourcePath, relativePath });
    }
  }

  return { copied, missing };
}

async function main() {
  const args = parseArgs(process.argv.slice(2));

  if (args.help) {
    printUsage();
    return;
  }

  const response = await fetchJson(buildExportSpecUrl(args), DEFAULT_TIMEOUT_MS);
  const spec = response?.data;
  if (!spec || !spec.material?.name) {
    throw new Error("Unity bridge did not return a valid material export spec.");
  }

  await enrichShaderGraphBundle(spec);

  const outputRoot = path.resolve(args.out, sanitizeDirectoryName(spec.material.name));
  await fs.mkdir(outputRoot, { recursive: true });

  const manifest = stripBundlePayloadFromManifest(spec);
  await writeJson(path.join(outputRoot, "manifest.json"), manifest);

  const customFunctionSources = await copyCustomFunctionSources(outputRoot, spec);
  const shaderGraphResult = await writeShaderGraphBundle(outputRoot, spec.shaderGraph);
  const textureResult = await copyTextures(outputRoot, spec);

  const summary = {
    material: spec.material.name,
    outputRoot,
    manifest: "manifest.json",
    wroteShaderGraph: shaderGraphResult.wroteMainGraph,
    wroteSubgraphs: shaderGraphResult.wroteSubgraphs,
    copiedShaderSources: customFunctionSources.copied,
    missingShaderSources: customFunctionSources.missing,
    copiedTextures: textureResult.copied,
    missingTextures: textureResult.missing,
  };

  process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.stack || error.message : String(error)}\n`);
  process.exit(1);
});
