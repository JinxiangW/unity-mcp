import test from "node:test";
import assert from "node:assert/strict";
import {callUnityTool,tools} from "../src/tools/index.js";
const client={get:async(path,args)=>({path,args}),post:async(path,args)=>({path,args})};
test("bounded Editor routes",async()=>{
 assert.equal((await callUnityTool("get_editor_state",client)).path,"/api/editor/state");
 assert.equal((await callUnityTool("refresh_editor",client)).path,"/api/editor/refresh");
 for(const action of ["enter","exit","pause","resume"])assert.deepEqual((await callUnityTool("set_editor_play_mode",client,{action})).args,{action});
 for(const entry of ["camera","history-clear","reference","preview","preview-restart","diagnostic-preview","dynamic","configure","lifecycle","regression"])assert.equal((await callUnityTool("run_lobby_aa_validation",client,{entry,output:"fresh"})).args.entry,entry);
});
test("reject arbitrary execution and invalid control",async()=>{
 await assert.rejects(callUnityTool("set_editor_play_mode",client,{action:"save"}));
 await assert.rejects(callUnityTool("run_lobby_aa_validation",client,{entry:"System.IO.File.Delete",output:"x"}));
 await assert.rejects(callUnityTool("run_lobby_aa_validation",client,{entry:"camera",output:17}));
 const schema=tools.find(x=>x.name==="run_lobby_aa_validation").inputSchema;
 assert.equal(schema.additionalProperties,false);assert.equal(schema.properties.entry.enum.length,10);
});
