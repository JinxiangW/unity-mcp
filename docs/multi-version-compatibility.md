# Multi-version Compatibility Plan

## Goal

Support multiple Unity versions while changing as few components as possible.

The target design is:

- stable MCP tool names
- stable HTTP route names
- stable JSON response shapes
- version-specific behavior isolated inside the Unity package

In practice, this means future Unity-version work should mostly replace or extend small compatibility components, not rewrite the whole query stack.

## Design principle

Use a stable protocol with swappable implementations.

- `src/index.js` should stay mostly version-agnostic
- `UnityReadOnlyMcpServer.cs` should stay mostly version-agnostic
- `UnityReadOnlyMcpQueries.cs` should express business questions, not version branches
- version differences should be absorbed by a dedicated `Compat` layer

## Proposed Unity package structure

```text
unity-package/com.ta.readonly-unity-mcp/Editor/
  Contracts/
    AssetRefDto.cs
    AssetInfoDto.cs
    MaterialInfoDto.cs
    ShaderInfoDto.cs
    ShaderGraphInfoDto.cs
    SceneInfoDto.cs
    SceneRendererBindingDto.cs

  Compat/
    UnityVersionContext.cs
    CapabilityProbe.cs
    CompatServices.cs

    Asset/
      IAssetDependencyReader.cs
      DefaultAssetDependencyReader.cs

    Scene/
      ISceneRenderReader.cs
      DefaultSceneRenderReader.cs
      ReflectionVolumeReader.cs

    Shader/
      IShaderIntrospector.cs
      ModernShaderIntrospector.cs
      LegacyShaderIntrospector.cs
      ShaderSourceMetadataReader.cs

    ShaderGraph/
      IShaderGraphReader.cs
      TextShaderGraphReader.cs
      GraphEnvelopeReader.cs
      GraphSchemaAdapter.cs
      GraphNormalizer.cs

  UnityReadOnlyMcpBootstrap.cs
  UnityReadOnlyMcpServer.cs
  UnityReadOnlyMcpQueries.cs
  UnityShaderGraphTextParser.cs
```

The exact filenames can evolve, but the separation should stay the same.

## Responsibilities by layer

### Contracts

Purpose: define stable shapes for data returned by the Unity package.

- replace ad hoc anonymous objects over time
- make cross-version adaptation explicit
- preserve stable HTTP and MCP responses even if Unity internals differ

Examples:

- `AssetRefDto`: `name`, `path`, `guid`, `type`
- `ShaderPropertyDto`: `name`, `description`, `type`, `flags`, `rangeMin`, `rangeMax`, `attributes`
- `ShaderGraphInfoDto`: `properties`, `keywords`, `nodes`, `edges`, `subGraphs`, `targets`, `warnings`

### UnityVersionContext

Purpose: detect the current runtime environment once and expose a compact capability summary.

It should gather:

- `Application.unityVersion`
- installed package versions where useful
- whether key APIs or types exist

Suggested output shape:

```csharp
public sealed class UnityVersionContext
{
    public string UnityVersion { get; }
    public string ShaderGraphPackageVersion { get; }
    public string RenderPipelinePackageVersion { get; }
    public bool HasShaderGetPropertyApi { get; }
    public bool HasVolumeType { get; }
    public bool HasShaderGraphAssembly { get; }
}
```

### CapabilityProbe

Purpose: centralize reflection-based feature detection.

It should answer questions like:

- does `Shader.GetPropertyCount()` exist?
- does `Shader.GetPropertyAttributes()` exist?
- is `UnityEngine.Rendering.Volume` available?
- is a specific Shader Graph assembly present?

This avoids scattering reflection and version checks across query code.

### CompatServices

Purpose: choose the correct implementation once and expose stable service instances.

Suggested shape:

```csharp
internal static class CompatServices
{
    public static UnityVersionContext Context { get; }
    public static IShaderIntrospector Shader { get; }
    public static IShaderGraphReader ShaderGraph { get; }
    public static ISceneRenderReader Scene { get; }
    public static IAssetDependencyReader Asset { get; }
}
```

`UnityReadOnlyMcpQueries.cs` should depend on this facade instead of directly branching on Unity version.

## Interface design

### IShaderIntrospector

This is the most important compatibility seam.

Suggested responsibilities:

- read shader property metadata
- read shader keywords
- read pass names when available
- read fallback and custom editor metadata
- build `ShaderInfoDto`

Suggested methods:

```csharp
public interface IShaderIntrospector
{
    ShaderInfoDto ReadShaderInfo(Shader shader, string assetPath, bool includeUsage);
    IReadOnlyList<ShaderPropertyDto> ReadProperties(Shader shader);
    IReadOnlyList<ShaderKeywordDto> ReadKeywords(Shader shader);
}
```

Implementations:

- `ModernShaderIntrospector`: prefer modern `Shader` instance APIs
- `LegacyShaderIntrospector`: fall back to older or reflected APIs

### IShaderGraphReader

This is the second major compatibility seam.

Suggested responsibilities:

- open and parse `.shadergraph` / `.shadersubgraph`
- resolve root graph object
- normalize field-name drift between package versions
- produce stable graph DTOs plus warnings

Suggested methods:

```csharp
public interface IShaderGraphReader
{
    ShaderGraphInfoDto Read(string assetPath);
}
```

Internal split:

- `GraphEnvelopeReader`: parse raw text into object fragments
- `GraphSchemaAdapter`: resolve version-specific field aliases
- `GraphNormalizer`: emit stable DTOs

### ISceneRenderReader

Suggested responsibilities:

- list loaded scenes
- build renderer -> material bindings
- summarize lights, reflection probes, and volumes

Suggested methods:

```csharp
public interface ISceneRenderReader
{
    SceneInfoDto ReadLoadedScenes();
    SceneRendererCollectionDto ReadSceneRenderers(string scenePath);
}
```

Use a sub-helper like `ReflectionVolumeReader` for volume-specific reflection access instead of hardwiring `Volume` assumptions into the scene reader.

### IAssetDependencyReader

Suggested responsibilities:

- read direct and recursive dependencies
- read reverse references when needed
- normalize asset references into stable DTOs

This seam is lower risk than shader and shader graph, but isolating it keeps query code cleaner.

## Query-layer target state

`UnityReadOnlyMcpQueries.cs` should become a thin orchestration layer.

Instead of this style:

- call Unity APIs directly
- branch on API availability inline
- build large anonymous objects inline

Move toward this style:

```csharp
public static object GetShaderInfo(string path, string guid, bool includeUsage)
{
    var assetPath = ResolveAssetPath(path, guid);
    var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
    if (shader == null)
    {
        throw new InvalidOperationException(...);
    }

    return CompatServices.Shader.ReadShaderInfo(shader, assetPath, includeUsage);
}
```

That keeps business intent readable and version behavior localized.

## Rules for version handling

### Prefer capability checks over version checks

Prefer:

- `if api exists, use it`
- `if type exists, read it`

Only use version-number branching when capability probing cannot express the difference clearly.

### Allow best-effort fields

If a field is not reliable across versions:

- keep the JSON field
- allow `null`
- optionally add `warnings`
- do not fail the whole route unless the whole query is impossible

### Keep protocol stable

When a version-specific fix is needed:

- first try to fix the compat implementation
- only change route/tool/output shape if the user-facing contract truly needs to evolve

## Migration plan

### Phase 1. Introduce contracts and version context

Low-risk foundation work.

- add DTO classes
- add `UnityVersionContext`
- add `CapabilityProbe`
- keep current queries working

### Phase 2. Extract shader compatibility seam

Highest value first.

- add `IShaderIntrospector`
- move shader property/keyword/pass access behind it
- keep `GetShaderInfo()` output unchanged

### Phase 3. Extract shader graph compatibility seam

Highest instability second.

- add `IShaderGraphReader`
- split current parser into reader/adapter/normalizer pieces
- add parse warnings instead of brittle hard failures

### Phase 4. Extract scene compatibility seam

- add `ISceneRenderReader`
- move volume/probe/light logic behind scene helpers
- isolate reflection-based volume access

### Phase 5. Extract asset dependency seam

- add `IAssetDependencyReader`
- normalize asset refs and dependency traversal

## Testing strategy for compatibility work

For each compatibility refactor:

1. Unity compile succeeds in the target project
2. `curl http://127.0.0.1:51234/health`
3. one asset route
4. one scene route
5. one shader route
6. one shadergraph route

If testing across multiple Unity versions, keep a small compatibility matrix in docs with:

- version
- status
- known limitations

## Recommended compatibility matrix format

```text
Unity 6000.3   supported       primary dev target
Unity 6000.2   expected        smoke-tested only
Unity 6000.4   expected        smoke-tested only
Unity 2022 LTS partial         shadergraph and shader APIs need extra validation
```

## Current compatibility status

```text
Unity 6000.3.7f1   supported   primary dev target, full smoke test passed
Unity 2019.4.41f2 partial     URP 7.7.1 smoke test passed, legacy Shader Graph uses best-effort parsing
```

### Unity 2019.4.41f2 notes

Validated against:

- Unity `2019.4.41f2`
- URP `7.7.1`
- Shader Graph `7.7.1`
- project: `D:/UnityProjects/URPSample`

What passed:

- package resolved as an embedded package
- `TA.ReadOnlyUnityMcp.Editor.dll` compiled cleanly
- `/health`
- `/api/scenes/info`
- `/api/scenes/renderers`
- `/api/materials/info`
- `/api/shaders/info`
- `/api/shadergraphs/info`

What needed adaptation:

- package metadata had to allow Unity `2019.4`
- the target project needed an explicit embedded-package dependency entry in `Packages/manifest.json`
- legacy Shader Graph files required a schema adapter for:
  - `m_SerializedProperties`
  - `m_SerializableNodes`
  - `m_SerializableEdges`
  - legacy master-node output inference

Known limitations on 2019.4:

- legacy Shader Graph parsing is structure-focused and best-effort
- old-format graphs may not provide modern `targets` or `keywords`
- shader pass names may be `null`
- warning strings are expected for legacy graph normalization

Compatibility rule reinforced by this test:

- `Compat/ShaderGraph/` is the right place to absorb old Shader Graph schema drift
- outward MCP tools and route names did not need to change
- DTO shapes remained stable while the legacy reader normalized older graph data

## Definition of success

This design is working when:

- adding support for a new Unity version mostly touches `Compat/`
- MCP tool names do not need to change
- route names do not need to change
- query methods stay readable and short
- output shape drift is minimal and intentional
