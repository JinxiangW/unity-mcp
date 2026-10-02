import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";

function printUsage() {
  process.stdout.write(`Usage:
  node scripts/summarize-shadergraph-io.js --root <material-export-directory> [--out <filename>]

Options:
  --root <directory>   Material export directory containing manifest.json and shadergraph.json
  --out <filename>     Output filename relative to --root. Default: graph-io-summary.json
  --help               Show this help
`);
}

function parseArgs(argv) {
  const args = {
    out: "graph-io-summary.json",
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
      case "root":
        args.root = value;
        break;
      case "out":
        args.out = value;
        break;
      default:
        throw new Error(`Unknown option '--${name}'.`);
    }
  }

  return args;
}

async function readJson(filePath) {
  return JSON.parse(await fs.readFile(filePath, "utf8"));
}

function indexByObjectId(items) {
  const map = new Map();
  for (const item of items || []) {
    if (item?.objectId) {
      map.set(item.objectId, item);
    }
  }
  return map;
}

function buildNodeSlotIndex(graph) {
  const slotsByNodeId = new Map();

  for (const node of graph.nodes || []) {
    const slotsById = new Map();

    for (const [slotGroup, direction] of [
      ["inputSlots", "input"],
      ["outputSlots", "output"],
    ]) {
      for (const slot of Array.isArray(node[slotGroup]) ? node[slotGroup] : []) {
        if (slot?.slotId == null || slotsById.has(slot.slotId)) {
          continue;
        }

        slotsById.set(slot.slotId, {
          ...slot,
          direction: slot.direction ?? direction,
        });
      }
    }

    if (node.objectId) {
      slotsByNodeId.set(node.objectId, slotsById);
    }
  }

  return slotsByNodeId;
}

function summarizeSlot(nodeId, slotId, direction, preferredName, slotsByNodeId) {
  const slot = slotId != null ? slotsByNodeId.get(nodeId)?.get(slotId) : null;
  if (!slot && slotId == null && !preferredName) {
    return null;
  }

  return {
    slotId: slot?.slotId ?? slotId ?? null,
    displayName: preferredName ?? slot?.displayName ?? slot?.shaderOutputName ?? null,
    direction: slot?.direction ?? direction ?? null,
    valueType: slot?.valueType ?? null,
    shaderOutputName: slot?.shaderOutputName ?? null,
    stageCapability: slot?.stageCapability ?? null,
  };
}

function summarizeBlockConnections(graph, blockList, nodesById, slotsByNodeId) {
  const edges = Array.isArray(graph.edges) ? graph.edges : [];

  return (blockList || []).map((block) => {
    const incoming = edges
      .filter((edge) => edge.inputNodeId === block.objectId)
      .map((edge) => {
        const source = nodesById.get(edge.outputNodeId);
        return {
          fromNodeId: edge.outputNodeId,
          fromNode: source?.displayName ?? edge.outputNodeId,
          fromType: source?.type ?? null,
          fromSlot: summarizeSlot(edge.outputNodeId, edge.outputSlotId, "output", edge.outputSlotName, slotsByNodeId),
          toSlot: summarizeSlot(edge.inputNodeId, edge.inputSlotId, "input", edge.inputSlotName, slotsByNodeId),
        };
      });

    return {
      blockObjectId: block.objectId,
      blockDescriptor: block.descriptor ?? block.displayName ?? null,
      incoming,
    };
  });
}

function summarizeSubgraphLinks(graph, nodesById, slotsByNodeId) {
  const subgraphIds = new Set((graph.subGraphReferences || []).map((item) => item.objectId));
  const edges = Array.isArray(graph.edges) ? graph.edges : [];

  return edges
    .filter((edge) => subgraphIds.has(edge.inputNodeId) || subgraphIds.has(edge.outputNodeId))
    .map((edge) => {
      const source = nodesById.get(edge.outputNodeId);
      const target = nodesById.get(edge.inputNodeId);
      return {
        fromNodeId: edge.outputNodeId,
        fromNode: source?.displayName ?? edge.outputNodeId,
        fromType: source?.type ?? null,
        fromSlot: summarizeSlot(edge.outputNodeId, edge.outputSlotId, "output", edge.outputSlotName, slotsByNodeId),
        toNodeId: edge.inputNodeId,
        toNode: target?.displayName ?? edge.inputNodeId,
        toType: target?.type ?? null,
        toSlot: summarizeSlot(edge.inputNodeId, edge.inputSlotId, "input", edge.inputSlotName, slotsByNodeId),
      };
    });
}

function summarizeSubgraphOutputs(graph, nodesById, slotsByNodeId) {
  const outputNode = (graph.nodes || []).find((node) => node.type === "UnityEditor.ShaderGraph.SubGraphOutputNode");
  if (!outputNode) {
    return [];
  }

  return (graph.edges || [])
    .filter((edge) => edge.inputNodeId === outputNode.objectId)
    .map((edge) => {
      const source = nodesById.get(edge.outputNodeId);
      const outputSlot = summarizeSlot(edge.inputNodeId, edge.inputSlotId, "input", edge.inputSlotName, slotsByNodeId);
      return {
        outputSlotId: edge.inputSlotId ?? null,
        outputSlotName: outputSlot?.displayName ?? edge.inputSlotName ?? null,
        fromNodeId: edge.outputNodeId,
        fromNode: source?.displayName ?? edge.outputNodeId,
        fromType: source?.type ?? null,
        fromSlot: summarizeSlot(edge.outputNodeId, edge.outputSlotId, "output", edge.outputSlotName, slotsByNodeId),
      };
    });
}

function summarizeSubgraphInputs(graph, nodesById, slotsByNodeId) {
  return (graph.nodes || [])
    .filter((node) => node.type === "UnityEditor.ShaderGraph.PropertyNode")
    .map((node) => {
      const outgoing = (graph.edges || [])
        .filter((edge) => edge.outputNodeId === node.objectId)
        .map((edge) => {
          const target = nodesById.get(edge.inputNodeId);
          return {
            toNodeId: edge.inputNodeId,
            toNode: target?.displayName ?? edge.inputNodeId,
            toType: target?.type ?? null,
            toSlot: summarizeSlot(edge.inputNodeId, edge.inputSlotId, "input", edge.inputSlotName, slotsByNodeId),
            fromSlot: summarizeSlot(edge.outputNodeId, edge.outputSlotId, "output", edge.outputSlotName, slotsByNodeId),
          };
        });

      return {
        propertyNodeId: node.objectId,
        displayName: node.displayName ?? "Property",
        outgoing,
      };
    });
}

async function collectSubgraphSummaries(rootDirectory, indexEntries) {
  const summaries = [];

  for (const entry of indexEntries || []) {
    const exportFile = entry.exportFile ? path.join(rootDirectory, entry.exportFile) : null;
    if (!exportFile) {
      continue;
    }

    const payload = await readJson(exportFile);
    const graph = payload.graph || {};
    const nodesById = indexByObjectId(graph.nodes);
    const slotsByNodeId = buildNodeSlotIndex(graph);

    summaries.push({
      name: entry.name ?? payload.asset?.name ?? null,
      assetPath: entry.path ?? payload.asset?.path ?? null,
      exportFile: entry.exportFile,
      summary: graph.summary ?? null,
      exposedProperties: (graph.properties || []).map((property) => ({
        displayName: property.displayName ?? null,
        referenceName: property.referenceName ?? null,
        type: property.type ?? null,
      })),
      nestedSubgraphReferences: (graph.subGraphReferences || []).map((item) => ({
        displayName: item.displayName ?? null,
        assetPath: item.assetPath ?? null,
        exportFile: item.exportFile ?? null,
      })),
      outputs: summarizeSubgraphOutputs(graph, nodesById, slotsByNodeId),
      propertyNodeConnections: summarizeSubgraphInputs(graph, nodesById, slotsByNodeId),
    });
  }

  return summaries;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help || !args.root) {
    printUsage();
    process.exit(args.help ? 0 : 1);
  }

  const rootDirectory = path.resolve(args.root);
  const manifestPath = path.join(rootDirectory, "manifest.json");
  const shadergraphPath = path.join(rootDirectory, "shadergraph.json");
  const subgraphIndexPath = path.join(rootDirectory, "subgraphs", "index.json");

  const manifest = await readJson(manifestPath);
  const shadergraph = await readJson(shadergraphPath);
  const subgraphIndex = await readJson(subgraphIndexPath);
  const graph = shadergraph.graph || {};
  const nodesById = indexByObjectId(graph.nodes);
  const slotsByNodeId = buildNodeSlotIndex(graph);

  const summary = {
    schemaVersion: "unity-shadergraph-io-summary/0.2",
    sourcePackage: {
      rootDirectory,
      manifest: "manifest.json",
      shadergraph: "shadergraph.json",
      subgraphIndex: path.relative(rootDirectory, subgraphIndexPath).replaceAll("\\", "/"),
    },
    material: manifest.material ?? null,
    shader: manifest.shader ?? null,
    surface: manifest.surface ?? null,
    semantics: {
      keys: Object.keys(manifest.semantics || {}),
      custom: manifest.semantics?.custom ?? null,
    },
    mainGraph: {
      summary: graph.summary ?? null,
      subgraphReferences: (graph.subGraphReferences || []).map((item) => ({
        displayName: item.displayName ?? null,
        objectId: item.objectId ?? null,
        subGraphGuid: item.subGraphGuid ?? null,
        assetPath: item.assetPath ?? null,
        exportFile: item.exportFile ?? null,
      })),
      fragmentOutputs: summarizeBlockConnections(graph, graph.output?.fragmentBlocks, nodesById, slotsByNodeId),
      vertexOutputs: summarizeBlockConnections(graph, graph.output?.vertexBlocks, nodesById, slotsByNodeId),
      subgraphLinks: summarizeSubgraphLinks(graph, nodesById, slotsByNodeId),
    },
    subgraphs: await collectSubgraphSummaries(rootDirectory, subgraphIndex.entries),
  };

  const outputPath = path.join(rootDirectory, args.out);
  await fs.writeFile(outputPath, `${JSON.stringify(summary, null, 2)}\n`, "utf8");

  process.stdout.write(`${JSON.stringify({
    output: outputPath,
    subgraphs: summary.subgraphs.length,
    mainSubgraphReferences: summary.mainGraph.subgraphReferences.length,
  }, null, 2)}\n`);
}

main().catch((error) => {
  process.stderr.write(`${error?.stack || error}\n`);
  process.exit(1);
});
