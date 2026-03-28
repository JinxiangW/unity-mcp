import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

const DEFAULT_PORT = process.env.UNITY_MCP_PORT || process.env.UNITY_READONLY_MCP_PORT || "51234";
const DEFAULT_BASE_URL = process.env.UNITY_MCP_BASE_URL || `http://127.0.0.1:${DEFAULT_PORT}`;
const DEFAULT_TIMEOUT_MS = Number.parseInt(process.env.UNITY_MCP_TIMEOUT_MS || "15000", 10);

class UnityBridgeClient {
  constructor(baseUrl, timeoutMs) {
    this.baseUrl = baseUrl.replace(/\/$/, "");
    this.timeoutMs = timeoutMs;
  }

  async get(path, params = {}) {
    const url = new URL(`${this.baseUrl}${path}`);

    for (const [key, value] of Object.entries(params)) {
      if (value === undefined || value === null || value === "") {
        continue;
      }

      if (Array.isArray(value)) {
        url.searchParams.set(key, value.join(","));
        continue;
      }

      url.searchParams.set(key, String(value));
    }

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const response = await fetch(url, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
        signal: controller.signal,
      });

      const text = await response.text();
      let parsed;

      try {
        parsed = text ? JSON.parse(text) : {};
      } catch (error) {
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
      if (error.name === "AbortError") {
        throw new Error(`Unity bridge request timed out after ${this.timeoutMs}ms`);
      }

      throw error;
    } finally {
      clearTimeout(timeout);
    }
  }
}

const tools = [
  {
    name: "get_asset_info",
    description: "Read basic Unity asset info by path or GUID.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string", description: "Unity asset path, for example Assets/Materials/M_Wall.mat" },
        guid: { type: "string", description: "Unity asset GUID" },
        includeDependencies: { type: "boolean", description: "Include direct dependencies", default: false },
        includeReferencedBy: { type: "boolean", description: "Include assets that directly reference this asset", default: false },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_asset_dependencies",
    description: "Read asset dependency information by path or GUID.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
        recursive: { type: "boolean", default: true },
        includeReferencedBy: { type: "boolean", default: false },
      },
      additionalProperties: false,
    },
  },
  {
    name: "find_assets",
    description: "Find Unity assets by type and search filter.",
    inputSchema: {
      type: "object",
      properties: {
        type: { type: "string", description: "Unity type filter, for example Material, Shader, Texture2D" },
        filter: { type: "string", description: "AssetDatabase filter text" },
        limit: { type: "integer", minimum: 1, maximum: 1000, default: 200 },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_material_info",
    description: "Read material info, shader linkage, and property values.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_shader_info",
    description: "Read shader metadata, properties, keywords, passes, and usage.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
        includeUsage: { type: "boolean", default: true },
      },
      additionalProperties: false,
    },
  },
  {
    name: "find_materials_using_shader",
    description: "Find materials, and optionally loaded-scene renderers, using a shader.",
    inputSchema: {
      type: "object",
      properties: {
        shaderName: { type: "string", description: "Shader.name" },
        guid: { type: "string", description: "Shader asset GUID" },
        includeRenderers: { type: "boolean", default: true },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_shadergraph_info",
    description: "Read Shader Graph structure, properties, keywords, nodes, edges, and targets.",
    inputSchema: {
      type: "object",
      properties: {
        path: { type: "string" },
        guid: { type: "string" },
      },
      additionalProperties: false,
    },
  },
  {
    name: "get_scene_info",
    description: "Read currently loaded Unity scene information.",
    inputSchema: {
      type: "object",
      properties: {},
      additionalProperties: false,
    },
  },
  {
    name: "get_scene_renderers",
    description: "Read renderer to material bindings for loaded scenes or one loaded scene path.",
    inputSchema: {
      type: "object",
      properties: {
        scenePath: { type: "string", description: "Optional loaded scene path" },
      },
      additionalProperties: false,
    },
  },
];

const client = new UnityBridgeClient(DEFAULT_BASE_URL, DEFAULT_TIMEOUT_MS);

function assertPathOrGuid(args) {
  if (!args.path && !args.guid) {
    throw new Error("Either 'path' or 'guid' is required.");
  }
}

async function callUnity(toolName, args = {}) {
  switch (toolName) {
    case "get_asset_info":
      assertPathOrGuid(args);
      return client.get("/api/assets/info", args);
    case "get_asset_dependencies":
      assertPathOrGuid(args);
      return client.get("/api/assets/dependencies", args);
    case "find_assets":
      return client.get("/api/assets/find", args);
    case "get_material_info":
      assertPathOrGuid(args);
      return client.get("/api/materials/info", args);
    case "get_shader_info":
      assertPathOrGuid(args);
      return client.get("/api/shaders/info", args);
    case "find_materials_using_shader":
      if (!args.shaderName && !args.guid) {
        throw new Error("Either 'shaderName' or 'guid' is required.");
      }
      return client.get("/api/shaders/materials", args);
    case "get_shadergraph_info":
      assertPathOrGuid(args);
      return client.get("/api/shadergraphs/info", args);
    case "get_scene_info":
      return client.get("/api/scenes/info", args);
    case "get_scene_renderers":
      return client.get("/api/scenes/renderers", args);
    default:
      throw new Error(`Unknown tool: ${toolName}`);
  }
}

const server = new Server(
  {
    name: "readonly-unity-mcp",
    version: "0.1.0",
  },
  {
    capabilities: {
      tools: {},
    },
  },
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools }));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const toolName = request.params.name;
  const args = request.params.arguments || {};

  try {
    const result = await callUnity(toolName, args);

    return {
      content: [
        {
          type: "text",
          text: JSON.stringify(result, null, 2),
        },
      ],
      structuredContent: result,
    };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);

    return {
      isError: true,
      content: [
        {
          type: "text",
          text: message,
        },
      ],
    };
  }
});

server.onerror = (error) => {
  process.stderr.write(`[readonly-unity-mcp] ${error instanceof Error ? error.stack || error.message : String(error)}\n`);
};

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((error) => {
  process.stderr.write(`[readonly-unity-mcp] Failed to start: ${error instanceof Error ? error.stack || error.message : String(error)}\n`);
  process.exit(1);
});
