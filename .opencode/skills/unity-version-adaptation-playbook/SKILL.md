---
name: unity-version-adaptation-playbook
description: Adapt this Unity MCP to a new Unity version while keeping MCP tools, routes, and JSON contracts as stable as possible.
license: MIT
compatibility: opencode
metadata:
  repo: unity-mcp
  audience: coding-agents
  focus: multi-version-adaptation
---

## What this skill is for

Use this skill when the repo needs to work on a different Unity version than the current primary target.

The goal is not just to "make it compile". The goal is to make it compile with the smallest possible blast radius:

- keep MCP tool names stable
- keep HTTP route names stable
- keep JSON response shapes stable
- isolate version-specific fixes inside the Unity package, preferably inside `Compat/`

## First read in a new session

1. `README.md`
2. `docs/multi-version-compatibility.md`
3. `docs/architecture.md`
4. `docs/maintenance-strategy.md`
5. `.opencode/skills/unity-mcp-playbook/SKILL.md`

## Core rule

Prefer fixing new-version issues in this order:

1. `unity-package/com.ta.unity-mcp/Editor/Compat/`
2. `unity-package/com.ta.unity-mcp/Editor/UnityMcpQueries.cs`
3. `unity-package/com.ta.unity-mcp/Editor/UnityMcpServer.cs`
4. `src/index.js`

If a problem can be solved in `Compat/`, do not change the MCP contract.

## Mental model

- `Contracts/` = stable output shapes
- `Compat/` = version-specific implementation details
- `Queries.cs` = business questions and orchestration
- `Server.cs` = HTTP transport
- `src/index.js` = MCP transport and small result normalization

When adapting to a new Unity version, most work should happen in `Compat/`.

## Standard workflow for a new Unity version

### 1. Identify the target environment

Capture these before changing code:

- Unity version from `ProjectSettings/ProjectVersion.txt`
- whether URP/HDRP/Shader Graph packages are present
- whether the project already compiles before adding this package

### 2. Sync the package into a real Unity project

Do not rely only on static reasoning. Use a real project on the target version.

### 3. Compile first, then inspect logs

After sync:

- wait for script compilation
- inspect `Editor.log` or Console errors
- separate current errors from historical log noise

## Error and warning triage loop

Use this exact order every time:

1. compile or wait for Unity compile to finish
2. read the latest `Editor.log` tail or current Console output
3. separate `error` from `warning`
4. fix all current compile errors first
5. recompile and confirm errors are gone
6. then fix warnings that come from this package, especially obsolete API warnings
7. recompile again and confirm the latest log section is clean or only has accepted external noise

Important:

- do not chase old log entries without checking the latest compile section
- do not treat historical errors as current blockers
- prefer fixing warnings in `Compat/` so version cleanup stays isolated
- if a warning comes from another package or the host project, note it but do not change unrelated code

### 4. Classify the failure

Use one of these buckets:

- missing API or renamed API
- obsolete API warning
- namespace/type moved
- Shader Graph text schema drift
- runtime route failure after compile succeeds
- output-shape issue or noisy data issue

### 5. Fix in the smallest correct layer

#### Missing or changed Unity API

Usually fix in:

- `Compat/UnityVersionContext.cs`
- `Compat/CapabilityProbe.cs`
- `Compat/Shader/`
- `Compat/Scene/` when that layer exists

Prefer:

- reflection or capability detection
- alternate implementation classes
- best-effort fallback

Avoid scattering version checks through `Queries.cs`.

#### Obsolete warnings

Prefer:

- reflection wrappers in `Compat/`
- modern APIs where available
- leaving the legacy implementation callable without direct obsolete references if possible

#### Current compile errors from this package

Prioritize by blast radius:

1. namespace/type resolution errors
2. missing or renamed API errors
3. contract/DTO mismatch errors
4. route/runtime behavior errors after compile succeeds

Most of these should be fixed in `Compat/` or in thin wrappers used by `Compat/`.

#### Shader Graph drift

Fix in:

- `Compat/ShaderGraph/GraphSchemaAdapter.cs`
- `Compat/ShaderGraph/GraphNormalizer.cs`

Do not rush to change outward JSON structure. First normalize the new schema into the existing DTOs.

#### Runtime data mismatch or noisy results

Fix in:

- `Compat/` if the wrong raw data is being selected
- `src/index.js` only if the issue is presentation/noise filtering and the Unity-side response is still useful internally

## Version-adaptation checklist

For each new Unity version, do this in order:

1. sync package into the target project
2. compile and collect errors
3. fix compile blockers in `Compat/`
4. recompile and confirm the errors are actually gone
5. remove version-specific warnings where practical
6. recompile and confirm the latest compile section is clean
7. test `/health`
8. test `/api/scenes/info`
9. test one material route
10. test one shader route
11. test one shadergraph route
12. update docs with version status and known limitations

## What to keep stable

Try not to change these unless truly necessary:

- MCP tool names
- Unity route names
- top-level JSON field names
- asset/material/shader reference object shapes

If a field is unavailable in a version, prefer returning `null` or an empty array over deleting the field.

## DTO rule

When a new Unity version returns data differently, adapt the data into the existing DTOs first.

Do not let version-specific shapes leak outward unless there is a deliberate contract change.

## Fast verification commands

When Unity is open, verify in this order:

1. `curl http://127.0.0.1:51234/health`
2. `curl http://127.0.0.1:51234/api/scenes/info`
3. one material query
4. one shader query
5. one shadergraph query

If `/health` is down, check compile state before changing runtime code.

## How to record compatibility status

When support for a new version is improved, update `docs/multi-version-compatibility.md` with:

- Unity version
- support status
- what was changed
- known limitations

Use a compact matrix like:

```text
Unity 6000.3   supported       primary dev target
Unity 6000.4   expected        smoke-tested
Unity 2022 LTS partial         shadergraph parsing needs extra validation
```

## Definition of done for a new version

- target Unity project compiles cleanly
- `/health` responds
- material, shader, and shadergraph routes all return JSON
- fixes are mostly isolated to `Compat/`
- docs mention the version status
- read-only boundary still holds

## Escalation rule

Only change `Contracts/`, route names, or MCP tool shapes if the new Unity version makes the old contract impossible or misleading.

That is the exception, not the default.
