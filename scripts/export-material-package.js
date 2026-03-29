import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const DEFAULT_PORT = process.env.UNITY_MCP_PORT || process.env.UNITY_READONLY_MCP_PORT || "51234";
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

function stripBundlePayloadFromManifest(spec) {
  const manifest = JSON.parse(JSON.stringify(spec));

  if (manifest?.shaderGraph?.bundle) {
    delete manifest.shaderGraph.bundle.mainGraph;
    delete manifest.shaderGraph.bundle.subgraphs;
    delete manifest.shaderGraph.bundle.index;
  }

  return manifest;
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

  const outputRoot = path.resolve(args.out, sanitizeDirectoryName(spec.material.name));
  await fs.mkdir(outputRoot, { recursive: true });

  const manifest = stripBundlePayloadFromManifest(spec);
  await writeJson(path.join(outputRoot, "manifest.json"), manifest);

  const shaderGraphResult = await writeShaderGraphBundle(outputRoot, spec.shaderGraph);
  const textureResult = await copyTextures(outputRoot, spec);

  const summary = {
    material: spec.material.name,
    outputRoot,
    manifest: "manifest.json",
    wroteShaderGraph: shaderGraphResult.wroteMainGraph,
    wroteSubgraphs: shaderGraphResult.wroteSubgraphs,
    copiedTextures: textureResult.copied,
    missingTextures: textureResult.missing,
  };

  process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.stack || error.message : String(error)}\n`);
  process.exit(1);
});
