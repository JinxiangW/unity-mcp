import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { fileURLToPath } from "node:url";
const [name, json = "{}"] = process.argv.slice(2);
const allowed=["get_editor_state","refresh_editor","set_editor_play_mode","run_lobby_aa_validation","get_scene_info","get_shader_info"];
if(!allowed.includes(name))throw new Error("Unknown bounded tool: "+name);
const client=new Client({name:"unity-editor-validation-client",version:"1.0.0"},{capabilities:{}});
try {
  await client.connect(new StdioClientTransport({command:process.execPath,args:[fileURLToPath(new URL("../src/index.js",import.meta.url))]}));
  const result=await client.callTool({name,arguments:JSON.parse(json)});
  process.stdout.write(JSON.stringify({protocol:"MCP tools/call",name,result},null,2)+"\n");
  if(result.isError)process.exitCode=1;
} finally { await client.close(); }
