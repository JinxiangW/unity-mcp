// Sends one refresh request, then observes that same persisted request through reload.
import {Client} from "@modelcontextprotocol/sdk/client/index.js";
import {StdioClientTransport} from "@modelcontextprotocol/sdk/client/stdio.js";
import {fileURLToPath} from "node:url";
const client=new Client({name:"unity-refresh-completion-client",version:"1.0.0"},{capabilities:{}});
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
try {
 await client.connect(new StdioClientTransport({command:process.execPath,args:[fileURLToPath(new URL("../src/index.js",import.meta.url))]}));
 const request=await client.callTool({name:"refresh_editor",arguments:{}});
 if(request.isError)throw new Error(JSON.stringify(request));
 const requestId=request.structuredContent?.data?.requestId;
 if(!requestId)throw new Error("Bridge lacks persistent refresh request identity");
 const deadline=Date.now()+120000;let lastResult;
 while(Date.now()<deadline){
  await pause(1000);
  // Unity's HTTP listener can disappear briefly during assembly reload. Never resend refresh.
  const state=await client.callTool({name:"get_editor_state",arguments:{}});lastResult=state;
  if(state.isError)continue;
  const data=state.structuredContent?.data,refresh=data?.refresh;
  if(refresh?.requestId!==requestId)continue; // An in-flight pre-reload read may still describe the prior request; never accept it as this request.
  if(refresh.finished){
   process.stdout.write(JSON.stringify({protocol:"MCP tools/call",requestId,request,result:state},null,2)+"\n");
   if(!refresh.passed||data.compilerFailed||data.isCompiling||data.isUpdating)process.exitCode=1;
   break;
  }
 }
 if(Date.now()>=deadline)throw new Error("Refresh completion deadline exceeded; do not retry mutation: "+requestId+" "+JSON.stringify(lastResult));
} finally {await client.close();}
