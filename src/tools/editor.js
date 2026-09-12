export const editorTools = [
  { name: "get_editor_state", description: "Read Editor play/compile state and recent compile diagnostics.", inputSchema: { type: "object", properties: {}, additionalProperties: false } },
  { name: "refresh_editor", description: "Request AssetDatabase refresh in the current Editor; poll get_editor_state for compile completion.", inputSchema: { type: "object", properties: {}, additionalProperties: false } },
  { name: "set_editor_play_mode", description: "Enter, exit, pause, or resume the currently loaded scene. Does not save or replace scenes.", inputSchema: { type: "object", properties: { action: { type: "string", enum: ["enter", "exit", "pause", "resume"] } }, required: ["action"], additionalProperties: false } },
  { name: "run_lobby_aa_validation", description: "Run one explicitly installed Lobby AA validation entry; no arbitrary C# or menu execution.", inputSchema: { type: "object", properties: { entry: { type: "string", enum: ["camera", "history-clear", "preview", "preview-restart", "diagnostic-preview", "dynamic", "reference", "configure", "lifecycle", "regression", "repair-check", "repair-restore"] }, output: { type: "string" }, statePath: { type: "string" } }, required: ["entry", "output"], additionalProperties: false } }
];
export async function callEditorTool(name, client, args = {}) {
  switch (name) {
    case "get_editor_state": return client.get("/api/editor/state");
    case "refresh_editor": return client.post("/api/editor/refresh");
    case "set_editor_play_mode":
      if (!["enter", "exit", "pause", "resume"].includes(args.action)) throw new Error("Invalid play action");
      return client.post("/api/editor/play-mode", { action: args.action });
    case "run_lobby_aa_validation":
      if (!["camera", "history-clear", "preview", "preview-restart", "diagnostic-preview", "dynamic", "reference", "configure", "lifecycle", "regression", "repair-check", "repair-restore"].includes(args.entry) || typeof args.output !== "string") throw new Error("Invalid validation entry/output");
      return client.post("/api/editor/validation", { entry: args.entry, output: args.output, statePath: args.statePath });
    default: return null;
  }
}
