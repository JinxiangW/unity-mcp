using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace TA.UnityMcp
{
    [InitializeOnLoad]
    internal static class UnityMcpEditorControl
    {
        static readonly List<object> diagnostics=new List<object>();
        static string lastRequest,lastError;
        static bool pending;
        static Action queuedRequest;
        static void PumpRequest()
        {
            // delayCall depends on an Inspector update and may stall in a background Editor.
            // HTTP main-thread dispatch already uses EditorApplication.update, so use the same
            // guaranteed pump, once on the following editor update, without any UI interaction.
            var action=queuedRequest;if(action==null)return;queuedRequest=null;action();
        }
        const string JobKey="TA.UnityMcp.EditorJob",DiagnosticsKey="TA.UnityMcp.CompileDiagnostics";
        static JObject job,refresh;
        static bool backgroundOwned,previousRunInBackground;
        static int backgroundQueueRequests;
        const string BackgroundKey="TA.UnityMcp.BackgroundOwned",BackgroundPreviousKey="TA.UnityMcp.BackgroundPrevious";
        static void BeginBackgroundValidation()
        {
            if(backgroundOwned)throw new InvalidOperationException("Background validation already owned");
            previousRunInBackground=Application.runInBackground;backgroundOwned=true;backgroundQueueRequests=0;
            SessionState.SetBool(BackgroundPreviousKey,previousRunInBackground);SessionState.SetBool(BackgroundKey,true);
            Application.runInBackground=true;
            job["backgroundDriver"]=new JObject{["kind"]="runInBackground+QueuePlayerLoopUpdate",["previousRunInBackground"]=previousRunInBackground,["startedFrame"]=Time.frameCount};SaveJob();
        }
        static void PumpBackgroundValidation()
        {
            if(!backgroundOwned||!EditorApplication.isPlaying||EditorApplication.isPaused)return;
            EditorApplication.QueuePlayerLoopUpdate();backgroundQueueRequests++;
        }
        static void EndBackgroundValidation()
        {
            if(!backgroundOwned)return;
            Application.runInBackground=previousRunInBackground;backgroundOwned=false;SessionState.SetBool(BackgroundKey,false);
            if(job?["backgroundDriver"] is JObject driver){driver["finishedFrame"]=Time.frameCount;driver["queueRequests"]=backgroundQueueRequests;driver["restoredRunInBackground"]=Application.runInBackground;}
        }
        const string RefreshKey="TA.UnityMcp.RefreshJob";
        const string BridgeRevision="editor-update-pump-v4";
        const string RefreshAcceptanceMarker="LobbyAA.BackgroundRefresh.20260912.01";
        static double refreshQuietAfter;
        static string AssemblyId {get{return typeof(UnityMcpEditorControl).Assembly.ManifestModule.ModuleVersionId.ToString();}}
        static void SaveRefresh(){SessionState.SetString(RefreshKey,refresh==null?"":refresh.ToString(Formatting.None));}
        static void PollRefresh()
        {
            if(refresh==null||(bool?)refresh["finished"]==true||pending)return;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating){refreshQuietAfter=EditorApplication.timeSinceStartup+.5;return;}
            if(EditorApplication.timeSinceStartup<refreshQuietAfter)return;
            refresh["finished"]=true;refresh["passed"]=!EditorUtility.scriptCompilationFailed;refresh["phase"]="finished";
            refresh["completedUtc"]=DateTime.UtcNow.ToString("O");refresh["completedAssemblyId"]=AssemblyId;
            refresh["completedWhileFocused"]=UnityEditorInternal.InternalEditorUtility.isApplicationActive;SaveRefresh();
        }
        static double nextPoll;
        static bool diagnosticsObserved;
        static void SaveJob()
        {
            SessionState.SetString(JobKey,job==null?"":job.ToString(Formatting.None));
            if(job!=null&&Directory.Exists((string)job["output"]))File.WriteAllText(Path.Combine((string)job["output"],"mcp-job.json"),job.ToString());
        }
        static void FinishJob(bool passed,string error=null,JObject result=null)
        {
            if(job==null)return;
            EndBackgroundValidation();
            job["running"]=false;job["finished"]=true;job["passed"]=passed;job["error"]=error;
            job["result"]=result;job["finishedUtc"]=DateTime.UtcNow.ToString("O");
            if((bool?)job["cancelRequested"]==true)job["cancelled"]=result!=null&&(bool?)result["finished"]==true&&(bool?)result["restored"]==true;
            if(error!=null)lastError=error;
            Directory.CreateDirectory((string)job["output"]);SaveJob();
        }
        static void PollJob()
        {
            if(job==null||(bool?)job["running"]!=true||EditorApplication.timeSinceStartup<nextPoll)return;
            nextPoll=EditorApplication.timeSinceStartup+.25;
            string path=(string)job["reportPath"],entry=(string)job["entry"];
            if(entry.StartsWith("urp-",StringComparison.Ordinal)&&(DateTimeOffset.UtcNow-job["startedUtc"].ToObject<DateTimeOffset>()).TotalSeconds>360){FinishJob(false,"URP validation exceeded 360s without finished report; inspect owned fixture cleanup before retry");return;}
            if(entry=="urp-material-workflow"){string saved=SessionState.GetString("Endfield.UrpMaterialValidation","");if(!string.IsNullOrEmpty(saved))job["progress"]=JObject.Parse(saved);}
            if(entry=="urp-prefab-boundary"){var type=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="Assembly-CSharp-Editor").Select(a=>a.GetType("Endfield.Diagnostics.Editor.CharacterUrpPrefabBoundaryValidation",false)).FirstOrDefault(t=>t!=null);if(type!=null)job["progress"]=(string)type.GetMethod("Status",Type.EmptyTypes).Invoke(null,null);}
            if(entry=="urp-scene-lifecycle"){var type=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="Assembly-CSharp-Editor").Select(a=>a.GetType("Endfield.Diagnostics.Editor.CharacterUrpSceneValidation",false)).FirstOrDefault(t=>t!=null);if(type!=null)job["progress"]=(string)type.GetMethod("Status",Type.EmptyTypes).Invoke(null,null);}
            if(!entry.StartsWith("urp-",StringComparison.Ordinal)&&entry!="preview"&&entry!="preview-restart"&&entry!="diagnostic-preview"&&entry!="dynamic"&&entry!="lifecycle"&&entry!="regression"&&entry!="repair-check"&&entry!="repair-restore")return;
            if(File.Exists(path))try {
                var report=JObject.Parse(File.ReadAllText(path));
                if((bool?)report["finished"]==true){FinishJob((bool?)report["passed"]==true,(string)report["error"],report);return;}
            } catch(IOException){} catch(JsonException){}
            if(!EditorApplication.isPlaying||EditorApplication.isPaused)FinishJob(false,"Validation interrupted: Play ended or paused before its finished report");
        }
        static void InterruptJob(string reason)
        {
            PollJob();if((string)job?["entry"]=="urp-material-workflow"){string saved=SessionState.GetString("Endfield.UrpMaterialValidation","");if(!string.IsNullOrEmpty(saved)){string phase=(string)JObject.Parse(saved)["phase"];if(((phase=="reload"||phase=="exit")&&reason.Contains("assembly reload"))||(phase=="exit"&&reason.Contains("Play exit"))){SaveJob();return;}}}if(job!=null&&(bool?)job["running"]==true)FinishJob(false,reason);
        }
        static object ConsoleCompileErrors()
        {
            // Bounded read-only console fallback for first installation: event history may be absent.
            var errors=new List<string>();
            try {
                var assembly=typeof(EditorApplication).Assembly;var entries=assembly.GetType("UnityEditor.LogEntries");var entryType=assembly.GetType("UnityEditor.LogEntry");
                var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
                var countMethod=entries?.GetMethod("GetCount",flags);var get=entries?.GetMethod("GetEntryInternal",flags);
                var text=entryType?.GetField("condition",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)??entryType?.GetField("message",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                if(countMethod==null||get==null||text==null)return new {available=false,errors};
                var start=entries.GetMethod("StartGettingEntries",flags);var end=entries.GetMethod("EndGettingEntries",flags);
                try {
                    start?.Invoke(null,null);
                    int count=(int)countMethod.Invoke(null,null);var value=Activator.CreateInstance(entryType);
                    for(int i=Math.Max(0,count-2000);i<count&&errors.Count<200;i++) {
                        get.Invoke(null,new object[]{i,value});string message=text.GetValue(value) as string;
                        if(message!=null&&(message.Contains("error CS")||message.Contains("Shader error")))errors.Add(message);
                    }
                } finally {end?.Invoke(null,null);}
                return new {available=true,errors};
            } catch(Exception e){return new {available=false,errors,error=e.Message};}
        }
        static object ConsoleErrors()
        {
            const int returnLimit=20,scanLimit=2000;
            // Unity2021 ConsoleWindow.Mode error classes; raw mode is also returned.
            const int errorMask=1|2|(1<<4)|(1<<6)|(1<<8)|(1<<11)|(1<<13)|(1<<17)|(1<<20)|(1<<21)|(1<<22);
            var result=new List<object>();int scanned=0,matching=0,totalRows=0;var counters=new object[]{0,0,0};
            try
            {
                var assembly=typeof(EditorApplication).Assembly;var entries=assembly.GetType("UnityEditor.LogEntries");var entryType=assembly.GetType("UnityEditor.LogEntry");
                var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;var fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
                var start=entries?.GetMethod("StartGettingEntries",flags);var end=entries?.GetMethod("EndGettingEntries",flags);var get=entries?.GetMethod("GetEntryInternal",flags);var count=entries?.GetMethod("GetCount",flags);var counts=entries?.GetMethod("GetCountsByType",flags);
                var messageField=entryType?.GetField("message",fields)??entryType?.GetField("condition",fields);var modeField=entryType?.GetField("mode",fields);var stackField=entryType?.GetField("callstackTextStartUTF16",fields);
                if(start==null||end==null||get==null||count==null||counts==null||messageField==null||modeField==null)return new {available=false,entries=result,reason="Required Console read API unavailable"};
                var repeat=entries.GetMethod("GetEntryCount",flags);var getFilter=entries.GetMethod("GetFilteringText",flags);var consoleFlags=entries.GetProperty("consoleFlags",flags);
                string filterBefore=getFilter?.Invoke(null,null) as string;int flagsBefore=consoleFlags==null?0:(int)consoleFlags.GetValue(null);
                try
                {
                    start.Invoke(null,null);totalRows=(int)count.Invoke(null,null);counts.Invoke(null,counters);var value=Activator.CreateInstance(entryType);
                    for(int row=totalRows-1;row>=Math.Max(0,totalRows-scanLimit);row--)
                    {
                        scanned++;if(!(bool)get.Invoke(null,new object[]{row,value}))continue;
                        int mode=(int)modeField.GetValue(value);if((mode&errorMask)==0)continue;matching++;
                        if(result.Count>=returnLimit)continue;
                        string full=messageField.GetValue(value) as string??"";int split=stackField==null?-1:(int)stackField.GetValue(value);bool splitKnown=split>=0&&split<=full.Length;
                        object Field(string name){return entryType.GetField(name,fields)?.GetValue(value);}
                        result.Add(new {row,kind=(mode&(1<<17))!=0?"Exception":(mode&(2|(1<<21)))!=0?"Assert":"Error",mode,
                            isCompileError=(mode&((1<<11)|(1<<20)))!=0,fullMessage=full,message=splitKnown?full.Substring(0,split):full,stackTrace=splitKnown?full.Substring(split):null,stackSplitKnown=splitKnown,
                            file=Field("file"),line=Field("line"),column=Field("column"),instanceId=Field("instanceID"),globalLineIndex=Field("globalLineIndex"),repeatCount=repeat==null?(int?)null:(int)repeat.Invoke(null,new object[]{row})});
                    }
                }
                finally{end.Invoke(null,null);}
                bool filterActive=!string.IsNullOrEmpty(filterBefore);bool filtersUnchanged=filterBefore==(getFilter?.Invoke(null,null) as string)&&(consoleFlags==null||flagsBefore==(int)consoleFlags.GetValue(null));
                return new {available=true,totalRows,countsByType=new {errors=(int)counters[0],warnings=(int)counters[1],logs=(int)counters[2]},scannedRows=scanned,matchingRowsInScannedRange=matching,returnedEntries=result.Count,returnLimit,scanLimit,
                    truncated=totalRows>scanned||matching>result.Count,filterActive,collapseEnabled=(flagsBefore&1)!=0,errorLevelVisible=(flagsBefore&(1<<9))!=0,filtersUnchanged,entries=result,
                    scope="Newest first, at most20 Error/Exception/Assert-class rows from the newest2000 accessible Console rows. Native type counters are separate from row/collapse counts. Existing filter/collapse state is preserved; hidden rows are not fetched by changing it. Full returned messages/stacks are not character-truncated. Empty rows with filters enabled do not prove no errors."};
            }
            catch(Exception e){return new {available=false,totalRows,scannedRows=scanned,entries=result,error=e.Message};}
        }
        static UnityMcpEditorControl()
        {
            // Domain reload can interrupt a validation; restore the exact runtime-only setting first.
            if(SessionState.GetBool(BackgroundKey,false)){Application.runInBackground=SessionState.GetBool(BackgroundPreviousKey,false);SessionState.SetBool(BackgroundKey,false);}
            string previousRefresh=SessionState.GetString(RefreshKey,"");
            if(!string.IsNullOrEmpty(previousRefresh)) {
                refresh=JObject.Parse(previousRefresh);
                if((bool?)refresh["finished"]!=true){refresh["reloadObserved"]=true;refresh["phase"]="reloaded";refreshQuietAfter=EditorApplication.timeSinceStartup+.5;SaveRefresh();}
            }
            string persisted=SessionState.GetString(JobKey,"");
            if(!string.IsNullOrEmpty(persisted)){job=JObject.Parse(persisted);if((bool?)job["running"]==true){string savedWorkflow=SessionState.GetString("Endfield.UrpMaterialValidation","");bool declaredReload=(string)job["entry"]=="urp-material-workflow"&&!string.IsNullOrEmpty(savedWorkflow)&&new[]{"reload","exit"}.Contains((string)JObject.Parse(savedWorkflow)["phase"])&&string.Equals(Path.GetFullPath((string)JObject.Parse(savedWorkflow)["directory"]),Path.GetFullPath((string)job["output"]),StringComparison.OrdinalIgnoreCase);if(!declaredReload)FinishJob(false,"Validation interrupted by domain reload");}}
            string previousDiagnostics=SessionState.GetString(DiagnosticsKey,"");
            if(!string.IsNullOrEmpty(previousDiagnostics)){diagnostics.AddRange(JArray.Parse(previousDiagnostics).Select(x=>(object)x));diagnosticsObserved=true;}
            EditorApplication.update+=PollJob;
            EditorApplication.update+=PumpRequest;
            EditorApplication.update+=PollRefresh;
            EditorApplication.update+=PumpBackgroundValidation;
            AssemblyReloadEvents.beforeAssemblyReload+=()=>{InterruptJob("Validation interrupted by assembly reload");if(refresh!=null&&(bool?)refresh["finished"]!=true){refresh["phase"]="reloading";SaveRefresh();}};
            EditorApplication.quitting+=()=>InterruptJob("Validation interrupted by Editor quit");
            EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.ExitingPlayMode)InterruptJob("Validation interrupted by Play exit");};
            CompilationPipeline.compilationStarted += _ => {if(refresh!=null&&(bool?)refresh["finished"]!=true){refresh["compilationObserved"]=true;refresh["phase"]="compiling";SaveRefresh();}diagnostics.Clear();diagnosticsObserved=true;SessionState.SetString(DiagnosticsKey,"[]");};
            CompilationPipeline.assemblyCompilationFinished += (assembly,messages) => {
                foreach(var m in messages)if(diagnostics.Count<200)diagnostics.Add(new { assembly, message=m.message, type=m.type.ToString(), file=m.file, line=m.line });
                diagnosticsObserved=true;SessionState.SetString(DiagnosticsKey,JsonConvert.SerializeObject(diagnostics));
            };
        }
        static object ReadMember(object value,string name)
        {
            if(value==null)return null;var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            return value.GetType().GetProperty(name,flags)?.GetValue(value)??value.GetType().GetField(name,flags)?.GetValue(value);
        }
        static float[] MatrixValues(object value)
        {
            if(!(value is Matrix4x4 matrix))return null;var result=new float[16];for(int i=0;i<16;i++)result[i]=matrix[i];return result;
        }
        static float[][] FourRows(Vector4[] rows,int start)
        {
            if(rows==null||rows.Length<start+4)return null;return Enumerable.Range(start,4).Select(i=>new[]{rows[i].x,rows[i].y,rows[i].z,rows[i].w}).ToArray();
        }
        static object CapturedObjectBindings(Camera view)
        {
            var result=new List<object>();
            foreach(var item in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(x=>x!=null&&x.GetType().FullName=="HG.Rendering.Validation.HGLobbyCapturedObject"&&x.gameObject.scene.IsValid()&&!EditorUtility.IsPersistent(x)).Take(8))
            {
                if(ReadMember(item,"view") as Camera!=view)continue;
                var material=ReadMember(item,"material") as Material;var bindings=ReadMember(item,"bindings") as Array;var cameras=new List<object>();
                if(bindings!=null)foreach(var binding in bindings)
                {
                    if((string)ReadMember(binding,"kind")!="Camera")continue;
                    string uniform=(string)ReadMember(binding,"uniform");var rows=material==null?null:material.GetVectorArray(uniform);
                    cameras.Add(new {uniform,row24=FourRows(rows,24),row32=FourRows(rows,32),source="Material.GetVectorArray (last submitted parameter state; no GPU readback)"});
                }
                result.Add(new {sourceEid=ReadMember(item,"sourceEid"),instanceId=item.GetInstanceID(),active=item.isActiveAndEnabled,sourceOrigin=ReadMember(item,"sourceOrigin") is Vector3 origin?new[]{origin.x,origin.y,origin.z}:null,cameraBindings=cameras});
            }
            return result;
        }
        static object RuntimeState()
        {
            var sample=Resources.FindObjectsOfTypeAll<MonoBehaviour>().FirstOrDefault(x=>x!=null&&x.GetType().FullName=="HG.Rendering.Validation.HGCharacterSample"&&x.gameObject.scene.IsValid()&&!EditorUtility.IsPersistent(x));
            object Read(string name){if(sample==null)return null;var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;return sample.GetType().GetProperty(name,flags)?.GetValue(sample)??sample.GetType().GetField(name,flags)?.GetValue(sample);}
            var camera=Read("sampleCamera") as Camera;var target=camera==null?null:camera.targetTexture;
            var lobby=Resources.FindObjectsOfTypeAll<MonoBehaviour>().FirstOrDefault(x=>x!=null&&x.GetType().FullName=="HG.Rendering.Validation.HGLobbySample"&&x.gameObject.scene.IsValid()&&!EditorUtility.IsPersistent(x)&&(ReadMember(x,"character") as MonoBehaviour)==sample);
            bool? desiredAA=lobby==null?(bool?)null:ReadMember(lobby,"antialiasing") as bool?;
            var temporal=Read("temporal");var displayed=Read("displayOverride") as Texture;int bufferView=Read("bufferView") is int v?v:0;
            var shown=displayed!=null?displayed:(bufferView==1?Read("receiverClassification"):bufferView==2?Read("shadowScreen"):Read("output")) as Texture;
            var position=camera==null?Vector3.zero:camera.transform.position;var rotation=camera==null?Quaternion.identity:camera.transform.rotation;
            var cameraMode=camera==null?null:typeof(Camera).GetProperty("projectionMatrixMode",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(camera);
            var before=Read("beforeRenderFrame") as Delegate;var prepare=Read("prepareRenderFrame") as Delegate;var after=Read("afterRenderFrame") as Delegate;
            return new {frameCount=Time.frameCount,time=Time.time,unscaledTime=Time.unscaledTime,deltaTime=Time.deltaTime,realtime=Time.realtimeSinceStartup,runInBackground=Application.runInBackground,
                sample=sample==null?null:new {instanceId=sample.GetInstanceID(),width=Read("width"),height=Read("height"),outputCreated=(Read("output") as RenderTexture)?.IsCreated(),antialiasing=desiredAA,desiredAA,temporalEnabled=temporal!=null,lobbyFailure=ReadMember(lobby,"failure"),bufferView,displayOverride=displayed==null?null:displayed.name,shownTexture=shown==null?null:shown.name,lastJitteredViewProjection=MatrixValues(ReadMember(temporal,"jitteredViewProjection")),lastNonJitteredViewProjection=MatrixValues(ReadMember(temporal,"nonJitteredViewProjection")),enabled=sample.enabled,active=sample.isActiveAndEnabled,renderedFrames=Read("renderedFrames"),failure=Read("failure"),initialized=Read("context")!=null,framePrepared=Read("framePrepared"),temporalFrame=Read("temporalFrame"),
                    beforeSubscribers=before?.GetInvocationList().Length??0,prepareSubscribers=prepare?.GetInvocationList().Length??0,afterSubscribers=after?.GetInvocationList().Length??0},
                capturedObjects=CapturedObjectBindings(camera),camera=camera==null?null:new {name=camera.name,instanceId=camera.GetInstanceID(),position=new[]{position.x,position.y,position.z},rotation=new[]{rotation.x,rotation.y,rotation.z,rotation.w},fieldOfView=camera.fieldOfView,near=camera.nearClipPlane,far=camera.farClipPlane,aspect=camera.aspect,orthographic=camera.orthographic,orthographicSize=camera.orthographicSize,projectionMode=cameraMode==null?(int?)null:Convert.ToInt32(cameraMode),projectionModeName=cameraMode?.ToString(),projectionMatrix=MatrixValues(camera.projectionMatrix),enabled=camera.enabled,active=camera.gameObject.activeInHierarchy,target=target==null?null:target.name,width=target==null?0:target.width,height=target==null?0:target.height,targetCreated=target!=null&&target.IsCreated()},
                preCullSubscribers=Camera.onPreCull?.GetInvocationList().Length??0,postRenderSubscribers=Camera.onPostRender?.GetInvocationList().Length??0};
        }
        static object SceneWindowInventory()
        {
            var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            return SceneView.sceneViews.Cast<SceneView>().Where(v=>v!=null).Select(v=>
            {
                object gizmo=typeof(SceneView).GetField("m_OrientationGizmo",flags)?.GetValue(v);
                var gc=gizmo==null?null:gizmo.GetType().GetField("m_Camera",flags)?.GetValue(gizmo) as Camera;
                var rt=gizmo==null?null:gizmo.GetType().GetField("m_RenderTexture",flags)?.GetValue(gizmo) as RenderTexture;
                return new {viewId=v.GetInstanceID(),title=v.titleContent.text,cameraId=v.camera==null?0:v.camera.GetInstanceID(),position=new[]{v.position.x,v.position.y,v.position.width,v.position.height},gizmoCameraId=gc==null?0:gc.GetInstanceID(),gizmoRtId=rt==null?0:rt.GetInstanceID(),gizmoRtCreated=rt!=null&&rt.IsCreated()};
            }).ToArray();
        }
        static object CameraInventory(){return Resources.FindObjectsOfTypeAll<Camera>().Where(c=>c!=null).Select(c=>new{id=c.GetInstanceID(),name=c.name,type=c.cameraType.ToString(),enabled=c.enabled,persistent=EditorUtility.IsPersistent(c),sceneValid=c.gameObject.scene.IsValid(),scene=c.gameObject.scene.path,hideFlags=c.hideFlags.ToString(),objectHideFlags=c.gameObject.hideFlags.ToString(),targetId=c.targetTexture==null?0:c.targetTexture.GetInstanceID()}).ToArray();}
        public static object State()
        {
            PollJob();PollRefresh();var scene=SceneManager.GetActiveScene();
            return new { processId=System.Diagnostics.Process.GetCurrentProcess().Id, unityVersion=Application.unityVersion,
                isPlaying=EditorApplication.isPlaying,isPaused=EditorApplication.isPaused,isCompiling=EditorApplication.isCompiling,isUpdating=EditorApplication.isUpdating,
                sceneViewWindows=SceneWindowInventory(),cameraInventory=CameraInventory(),prefabStagePath=UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()?.assetPath,
                backgroundValidation=new {owned=backgroundOwned,previousRunInBackground,queueRequests=backgroundQueueRequests},runtime=RuntimeState(),applicationFocused=UnityEditorInternal.InternalEditorUtility.isApplicationActive,bridgeRevision=BridgeRevision,refreshAcceptanceMarker=RefreshAcceptanceMarker,loadedAssemblyId=AssemblyId,refresh,
                scene=scene.path,isDirty=scene.isDirty,pending,lastRequest,lastError,diagnostics,
                compilerFailed=EditorUtility.scriptCompilationFailed,diagnosticsObserved,consoleCompileErrors=ConsoleCompileErrors(),consoleErrors=ConsoleErrors(),validation=job,
                diagnosticScope="Compilation events persist across domain reload. Empty event/console lists do not prove compilation success; require compilerFailed=false and isCompiling=false, plus requested runtime reports." };
        }
        public static object Request(string route,JObject body)
        {
            if(route=="/api/editor/validation-cancel")
            {
                PollJob();if(job==null||(bool?)job["running"]!=true||(string)job["jobId"]!=(string)body["jobId"])throw new InvalidOperationException("No matching active validation job");
                if(!new[]{"urp-bound-response","urp-scene-lifecycle","urp-pantyhose","urp-prefab-boundary","urp-panty-ingestion","urp-panty-aligned","urp-mpb-timing-a","urp-mpb-timing-b","urp-fullchain-aligned","urp-taa-start"}.Contains((string)job["entry"]))throw new InvalidOperationException("This suite has no installed safe cancellation contract");
                if(pending||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Editor is busy; cancellation not dispatched");
                var target=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="Assembly-CSharp-Editor").Select(a=>a.GetType((string)job["entry"]=="urp-bound-response"?"Endfield.Diagnostics.Editor.CharacterUrpBoundResponseValidation":(string)job["entry"]=="urp-pantyhose"?"Endfield.Diagnostics.Editor.CharacterUrpPantyhoseValidation":(string)job["entry"]=="urp-prefab-boundary"?"Endfield.Diagnostics.Editor.CharacterUrpPrefabBoundaryValidation":((string)job["entry"]=="urp-panty-ingestion"||(string)job["entry"]=="urp-panty-aligned"||(string)job["entry"]=="urp-mpb-timing-a")?"Endfield.Diagnostics.Editor.CharacterUrpPantyIngestionValidation":(string)job["entry"]=="urp-mpb-timing-b"?"Endfield.Diagnostics.Editor.EndfieldMpbOverrideTimingValidation":(string)job["entry"]=="urp-fullchain-aligned"?"Endfield.Diagnostics.Editor.EndfieldFullChainValidation":(string)job["entry"]=="urp-taa-start"?"Endfield.Diagnostics.Editor.EndfieldTaaStartValidation":"Endfield.Diagnostics.Editor.CharacterUrpSceneValidation",false)).First(t=>t!=null);
                job["cancelRequested"]=true;job["cancelRequestedUtc"]=DateTime.UtcNow.ToString("O");SaveJob();
                target.GetMethod("Cancel",Type.EmptyTypes).Invoke(null,null);nextPoll=0;PollJob();
                return new {accepted=true,cancelRequested=true,cancelled=(bool?)job["cancelled"]==true,jobId=(string)job["jobId"],finished=(bool?)job["finished"]==true,passed=false};
            }
            PollJob();if(job!=null&&(bool?)job["running"]==true)throw new InvalidOperationException("Validation is running; refresh, Play changes and additional validation requests are refused until its finished report");
            PollRefresh();if(refresh!=null&&(bool?)refresh["finished"]!=true)throw new InvalidOperationException("Refresh is still importing/compiling; await its finished state");
            if(pending)throw new InvalidOperationException("An Editor request is pending");
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("Editor is compiling or updating");
            string action=(string)body["action"],entry=(string)body["entry"],output=(string)body["output"];
            if(route=="/api/editor/play-mode" && !new[]{"enter","exit","pause","resume"}.Contains(action))throw new ArgumentException("Invalid play action");
            if(route=="/api/editor/validation")
            {
                if(!new[]{"urp-animation-prepare","urp-animation-run","urp-animation-restore","urp-baseline-hop-return","urp-baseline-hop-check","urp-baseline-hop-exit","urp-taa-start","urp-fullchain-aligned","urp-mpb-timing-a","urp-mpb-timing-b","urp-panty-aligned","urp-panty-ingestion","urp-prefab-boundary","urp-recover-scene","urp-integer-transport","urp-pantyhose","urp-world-shadow","urp-scene-shadow-reload","urp-scene-lifecycle","urp-tone-witness","urp-scene-shadow-import","urp-irradiance-import","urp-shadow-flags","urp-bound-response","urp-cloth-inputs","urp-scene","urp-game","urp-baseline-scene","urp-setup-fault","urp-post-fault","urp-tone","urp-overlay-reload","urp-material-workflow","camera","history-clear","preview","preview-restart","diagnostic-preview","dynamic","reference","configure","lifecycle","regression","repair-check","repair-restore"}.Contains(entry))throw new ArgumentException("Unknown validation");
                string root=Path.GetFullPath("D:/Endfield/Delivery/evidence/Validation/LobbyAA")+Path.DirectorySeparatorChar;
                output=Path.GetFullPath(output??"");
                if(!output.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Directory.Exists(output)||File.Exists(output))throw new ArgumentException("Validation requires a fresh output within LobbyAA evidence");
            }
            if(route=="/api/editor/validation") {
                string report=entry=="camera"?"camera-report.json":entry=="configure"?"mcp-job.json":"report.json";
                job=new JObject { ["jobId"]=Guid.NewGuid().ToString("N"),["entry"]=entry,["output"]=output,["reportPath"]=Path.Combine(output,report),["running"]=true,["finished"]=false,["passed"]=false,["startedUtc"]=DateTime.UtcNow.ToString("O") };SaveJob();
            }
            if(route=="/api/editor/refresh") {
                refresh=new JObject { ["requestId"]=Guid.NewGuid().ToString("N"),["phase"]="queued",["finished"]=false,["passed"]=false,["acceptedUtc"]=DateTime.UtcNow.ToString("O"),["requestedAssemblyId"]=AssemblyId,["requestedWhileFocused"]=UnityEditorInternal.InternalEditorUtility.isApplicationActive };SaveRefresh();
            }
            pending=true;lastRequest=route+":"+(entry??action??"refresh");lastError=null;
            queuedRequest = () => {
                try {
                    if(route=="/api/editor/refresh") {
                        refresh["phase"]="executing";refresh["executedUtc"]=DateTime.UtcNow.ToString("O");refresh["executedWhileFocused"]=UnityEditorInternal.InternalEditorUtility.isApplicationActive;SaveRefresh();
                        AssetDatabase.Refresh();refresh["refreshReturned"]=true;refresh["phase"]="awaiting-idle";refreshQuietAfter=EditorApplication.timeSinceStartup+.5;SaveRefresh();
                    }
                    else if(route=="/api/editor/play-mode") {
                        if(action=="enter")EditorApplication.isPlaying=true;
                        else if(action=="exit")EditorApplication.isPlaying=false;
                        else EditorApplication.isPaused=action=="pause";
                    } else {
                        if(entry=="history-clear") {
                            var ht=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("HG.Rendering.Validation.Editor.HGLobbyAAHistoryClearValidation",false)).FirstOrDefault(x=>x!=null);
                            if(ht==null)throw new InvalidOperationException("History clear validator not installed");
                            ht.GetMethod("Run",new[]{typeof(string)}).Invoke(null,new object[]{output});var hr=JObject.Parse(File.ReadAllText(Path.Combine(output,"report.json")));FinishJob((bool?)hr["passed"]==true,null,hr);return;
                        }
                        if(entry=="configure") {
                            var ct=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("HG.Rendering.Validation.Editor.HGLobbyAAIntegration",false)).FirstOrDefault(x=>x!=null);
                            if(ct==null)throw new InvalidOperationException("Lobby integration not installed");
                            ct.GetMethod("Configure",Type.EmptyTypes).Invoke(null,null);FinishJob(true,null,new JObject{["configured"]=true});return;
                        }
                        if(entry=="reference") {
                            var rt=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("HG.Rendering.Validation.Editor.HGLobbyAAReferenceValidation",false)).FirstOrDefault(x=>x!=null);
                            if(rt==null)throw new InvalidOperationException("Reference validator not installed");
                            rt.GetMethod("Run",new[]{typeof(string),typeof(string),typeof(string)}).Invoke(null,new object[]{"D:/Endfield/Delivery/evidence/Validation/LobbyAA/Source9057_02","D:/Endfield/Delivery/evidence/Validation/LobbyAA/Native9057_01",output});
                            var referenceReport=JObject.Parse(File.ReadAllText(Path.Combine(output,"report.json")));FinishJob((bool?)referenceReport["accepted"]==true,null,referenceReport);return;
                        }
                        if(entry=="repair-check"||entry=="repair-restore") {
                            BeginBackgroundValidation();
                            var repair=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("HG.Rendering.Validation.Editor.HGLobbyRuntimeRepairValidation",false)).FirstOrDefault(x=>x!=null);
                            if(repair==null)throw new InvalidOperationException("Repair validator not installed");
                            if(entry=="repair-check")repair.GetMethod("Run",new[]{typeof(string)}).Invoke(null,new object[]{output});
                            else repair.GetMethod("Restore",new[]{typeof(string),typeof(string)}).Invoke(null,new object[]{(string)body["statePath"],output});
                            SaveJob();return;
                        }
                        if(entry=="preview"||entry=="preview-restart"||entry=="diagnostic-preview"||entry=="dynamic"||entry=="lifecycle"||entry=="regression")BeginBackgroundValidation();
                        string type=entry=="camera"?"HG.Rendering.Validation.Editor.HGLobbyAACameraValidation":(entry=="lifecycle"||entry=="regression")?"HG.Rendering.Validation.Editor.HGLobbyAAAcceptanceValidation":"HG.Rendering.Validation.Editor.HGLobbyAAIntegration";
                        string method=entry=="camera"?"Run":entry=="preview"?"RunPreview":entry=="preview-restart"?"RunPreviewWithRestart":entry=="diagnostic-preview"?"RunDiagnosticPreview":entry=="lifecycle"?"RunLifecycle":entry=="regression"?"RunRegression":"RunDynamic";
                        var t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(type,false)).FirstOrDefault(x=>x!=null);
                        var m=t==null?null:t.GetMethod(method,new[]{typeof(string)});
                        if(m==null)throw new InvalidOperationException("Explicit validation entry is not installed: "+type+"."+method);
                        m.Invoke(null,new object[]{output});
                        if(entry=="camera") {var result=JObject.Parse(File.ReadAllText(Path.Combine(output,"camera-report.json")));FinishJob((bool?)result["passed"]==true,null,result);}
                        else SaveJob();
                    }
                } catch(Exception e){lastError=e.ToString();if(route=="/api/editor/validation")FinishJob(false,lastError);if(route=="/api/editor/refresh"){refresh["finished"]=true;refresh["passed"]=false;refresh["error"]=lastError;SaveRefresh();}Debug.LogError(lastError);} finally {pending=false;}
            };
            return new { accepted=true, request=lastRequest,jobId=route=="/api/editor/validation"?(string)job["jobId"]:null,requestId=route=="/api/editor/refresh"?(string)refresh["requestId"]:null };
        }
    }
}
