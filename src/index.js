import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { UnityBridgeClient } from "./lib/unity-bridge-client.js";
import { callUnityTool, tools } from "./tools/index.js";

const DEFAULT_PORT = process.env.UNITY_MCP_PORT || "51234";
const DEFAULT_BASE_URL = process.env.UNITY_MCP_BASE_URL || `http://127.0.0.1:${DEFAULT_PORT}`;
const DEFAULT_TIMEOUT_MS = Number.parseInt(process.env.UNITY_MCP_TIMEOUT_MS || "15000", 10);

const client = new UnityBridgeClient(DEFAULT_BASE_URL, DEFAULT_TIMEOUT_MS);

const server = new Server(
  {
    name: "unity-mcp",
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
    const result = await callUnityTool(toolName, client, args);
    return {
      content: [{ type: "text", text: JSON.stringify(result, null, 2) }],
      structuredContent: result,
    };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return {
      isError: true,
      content: [{ type: "text", text: message }],
    };
  }
});

server.onerror = (error) => {
  process.stderr.write(`[unity-mcp] ${error instanceof Error ? error.stack || error.message : String(error)}\n`);
};

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((error) => {
  process.stderr.write(`[unity-mcp] Failed to start: ${error instanceof Error ? error.stack || error.message : String(error)}\n`);
  process.exit(1);
});
