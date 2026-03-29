using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TA.ReadOnlyUnityMcp.Compat;
using TA.ReadOnlyUnityMcp.Compat.Shader;
using TA.ReadOnlyUnityMcp.Compat.ShaderGraph;
using TA.ReadOnlyUnityMcp.Contracts;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TA.ReadOnlyUnityMcp
{
    internal static class UnityReadOnlyMcpQueries
    {
        public static object GetAssetInfo(string path, string guid, bool includeDependencies, bool includeReferencedBy)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var importer = AssetImporter.GetAtPath(assetPath);

            return new
            {
                asset = DescribeAssetReference(assetPath),
                labels = mainAsset != null ? AssetDatabase.GetLabels(mainAsset) : Array.Empty<string>(),
                importer = importer != null ? new
                {
                    type = importer.GetType().FullName,
                    assetTimeStamp = AssetDatabase.GetAssetDependencyHash(assetPath).ToString()
                } : null,
                dependencies = includeDependencies ? GetDependencyReferences(assetPath, false) : null,
                referencedBy = includeReferencedBy ? FindAssetsReferencing(assetPath) : null,
                importSettings = GetImportSettings(assetPath)
            };
        }

        public static object GetAssetDependencies(string path, string guid, bool recursive, bool includeReferencedBy)
        {
            var assetPath = ResolveAssetPath(path, guid);

            return new
            {
                asset = DescribeAssetReference(assetPath),
                recursive,
                dependencies = GetDependencyReferences(assetPath, recursive),
                referencedBy = includeReferencedBy ? FindAssetsReferencing(assetPath) : null
            };
        }

        public static object FindAssets(string type, string filter, int limit)
        {
            limit = Mathf.Clamp(limit, 1, 1000);

            var searchTerms = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                searchTerms.Add(filter.Trim());
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                searchTerms.Add($"t:{type.Trim()}");
            }

            var search = string.Join(" ", searchTerms);
            var guids = AssetDatabase.FindAssets(search);
            var assets = guids.Take(limit)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(assetPath => !string.IsNullOrWhiteSpace(assetPath))
                .Select(DescribeAssetReference)
                .ToList();

            return new
            {
                search,
                totalMatches = guids.Length,
                returned = assets.Count,
                assets
            };
        }

        public static object GetMaterialInfo(string path, string guid)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                throw new InvalidOperationException($"Asset at '{assetPath}' is not a Material.");
            }

            return new
            {
                asset = DescribeAssetReference(assetPath),
                shader = material.shader != null ? DescribeShaderReference(material.shader) : null,
                renderQueue = material.renderQueue,
                enableInstancing = material.enableInstancing,
                doubleSidedGI = material.doubleSidedGI,
                globalIlluminationFlags = material.globalIlluminationFlags.ToString(),
                shaderKeywords = material.shaderKeywords ?? Array.Empty<string>(),
                properties = CompatServices.Shader.ReadMaterialProperties(material)
            };
        }

        public static object GetMaterialExportSpec(string path, string guid, string exportProfile, bool includeShaderGraph, bool recursiveShaderGraphs, bool includeRawProperties)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                throw new InvalidOperationException($"Asset at '{assetPath}' is not a Material.");
            }

            exportProfile = string.IsNullOrWhiteSpace(exportProfile) ? "ue-pbr" : exportProfile.Trim();

            var properties = CompatServices.Shader.ReadMaterialProperties(material);
            var propertyMap = properties.ToDictionary(property => property.name, StringComparer.Ordinal);
            var shader = material.shader;
            var shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
            var isShaderGraph = IsShaderGraphAssetPath(shaderPath);
            var warnings = new JArray();

            if (shader == null)
            {
                warnings.Add(BuildWarning("MATERIAL_WITHOUT_SHADER", "Material does not reference a shader.", "error"));
            }

            var baseColorProperty = FindProperty(propertyMap, "_BaseColor");
            var baseMapProperty = FindProperty(propertyMap, "_BaseMap");
            var normalMapProperty = FindProperty(propertyMap, "_BumpMap");
            var metallicProperty = FindProperty(propertyMap, "_Metallic");
            var metallicMapProperty = FindProperty(propertyMap, "_MetallicGlossMap");
            var smoothnessProperty = FindProperty(propertyMap, "_Smoothness");
            var occlusionMapProperty = FindProperty(propertyMap, "_OcclusionMap");
            var occlusionStrengthProperty = FindProperty(propertyMap, "_OcclusionStrength");
            var emissionToggleProperty = FindProperty(propertyMap, "_Use_Emission");
            var emissionMapProperty = FindProperty(propertyMap, "_EmissionMap");
            var emissionColorProperty = FindProperty(propertyMap, "_EmissionColor");
            var guideTextureProperty = FindProperty(propertyMap, "_GuideTexture");
            var guideTilingProperty = FindProperty(propertyMap, "_GuideTiling");
            var guideStrengthProperty = FindProperty(propertyMap, "_GuideStrength");
            var tilingProperty = FindProperty(propertyMap, "_Tiling");
            var offsetProperty = FindProperty(propertyMap, "_Offset");
            var cutoffProperty = FindProperty(propertyMap, "_Cutoff");
            var metallicTextureToggleProperty = FindProperty(propertyMap, "_Use_Metallic_Texture");

            var smoothness = GetFloatValue(smoothnessProperty);
            var roughness = smoothness.HasValue ? 1f - smoothness.Value : (float?)null;
            var alphaClip = HasKeyword(material.shaderKeywords, "_BUILTIN_AlphaClip")
                            || HasKeyword(material.shaderKeywords, "_BUILTIN_ALPHATEST_ON")
                            || (GetFloatValue(cutoffProperty) ?? 0f) > 0f;
            var surfaceType = material.renderQueue >= 3000 ? "Transparent" : "Opaque";
            var blendMode = alphaClip ? "Masked" : (material.renderQueue >= 3000 ? "Translucent" : "Opaque");
            var twoSided = HasKeyword(material.shaderKeywords, "_DOUBLESIDED_ON") || material.doubleSidedGI;
            var pipeline = InferPipeline(shader);
            var transferMode = isShaderGraph ? "custom_graph_needed" : "material_instance";
            var confidence = isShaderGraph ? 0.45 : 0.9;

            if (isShaderGraph)
            {
                warnings.Add(BuildWarning(
                    "CUSTOM_SHADERGRAPH_MATERIAL",
                    $"{material.name} uses the custom Shader Graph '{shader.name}'; only the transferable PBR subset is normalized for export.",
                    "warning"));
            }

            if (metallicTextureToggleProperty != null && (GetFloatValue(metallicTextureToggleProperty) ?? 0f) > 0f && GetTextureReference(metallicMapProperty) == null)
            {
                warnings.Add(BuildWarning(
                    "METALLIC_TEXTURE_MISSING",
                    "The shader exposes a metallic texture toggle, but no metallic texture is assigned on this material instance.",
                    "warning"));
            }

            if (roughness.HasValue)
            {
                warnings.Add(BuildWarning(
                    "ROUGHNESS_DERIVED_FROM_SMOOTHNESS",
                    "Roughness is derived as 1 - Unity smoothness because no dedicated roughness texture was assigned.",
                    "info"));
            }

            if (alphaClip && GetTextureReference(baseMapProperty) != null)
            {
                warnings.Add(BuildWarning(
                    "ALPHA_FROM_BASEMAP_INFERENCE",
                    "Opacity/alpha clip is inferred from the base map alpha because no dedicated opacity texture slot exists.",
                    "info"));
            }

            var textures = new JArray();
            AddTextureExport(textures, material.name, "baseColor", "_BaseMap", baseMapProperty, BuildChannelPacking("baseColor.r", "baseColor.g", "baseColor.b", "opacity"));
            AddTextureExport(textures, material.name, "normal", "_BumpMap", normalMapProperty, new JObject { ["rgb"] = "normal" });
            AddTextureExport(textures, material.name, "metallicRoughnessMask", "_MetallicGlossMap", metallicMapProperty, new JObject { ["r"] = "metallic", ["a"] = "smoothness" });
            AddTextureExport(textures, material.name, "occlusion", "_OcclusionMap", occlusionMapProperty, new JObject { ["g"] = "occlusion" });
            AddTextureExport(textures, material.name, "emission", "_EmissionMap", emissionMapProperty, new JObject { ["rgb"] = "emission" });
            AddTextureExport(textures, material.name, "custom.guideTexture", "_GuideTexture", guideTextureProperty, new JObject { ["rgba"] = "custom.guideTexture" });

            var customSemanticGroups = new JArray();
            AddCustomSemanticGroup(customSemanticGroups, "custom.guideTexture", guideTextureProperty, new[] { "_GuideTexture", "_GuideTiling", "_GuideStrength" }, new JObject
            {
                ["guideTiling"] = ToJToken(GetFloatValue(guideTilingProperty)),
                ["guideStrength"] = ToJToken(GetFloatValue(guideStrengthProperty))
            });
            AddCustomSemanticGroup(customSemanticGroups, "dissolve", null, new[]
            {
                "_Invert",
                "_UseBackColor",
                "_BackColor",
                "_UseDithering",
                "_EdgeColor",
                "_EdgeWidth",
                "_EdgeSmoothness",
                "_AffectAlbedo",
                "_GlareColor",
                "_GlareGuideStrength",
                "_GlareWidth",
                "_GlareSmoothness",
                "_GlareOffset"
            }, null);
            AddCustomSemanticGroup(customSemanticGroups, "displacement", null, new[]
            {
                "_DisplacementPerVertex",
                "_DisplacementSmoothness",
                "_DisplacementOffset",
                "_RotationAxis",
                "_RotationMin",
                "_RotationMax",
                "_RandomPositionOffset",
                "_PositionOffset",
                "_Scale",
                "_NormalOffset"
            }, null);

            var spec = new JObject
            {
                ["schemaVersion"] = "unity-material-export-spec/1.0",
                ["exportProfile"] = exportProfile,
                ["exportedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["unityContext"] = new JObject
                {
                    ["unityVersion"] = Application.unityVersion,
                    ["projectPath"] = Directory.GetCurrentDirectory().Replace('\\', '/'),
                    ["renderPipelinePackageVersion"] = ToJToken(CompatServices.Context.renderPipelinePackageVersion),
                    ["shaderGraphPackageVersion"] = ToJToken(CompatServices.Context.shaderGraphPackageVersion)
                },
                ["material"] = JObject.FromObject(new
                {
                    name = material.name,
                    path = assetPath,
                    guid = AssetDatabase.AssetPathToGUID(assetPath),
                    assetType = material.GetType().FullName
                }),
                ["shader"] = shader != null ? JObject.FromObject(new
                {
                    name = shader.name,
                    path = shaderPath,
                    guid = string.IsNullOrWhiteSpace(shaderPath) ? null : AssetDatabase.AssetPathToGUID(shaderPath),
                    isShaderGraph,
                    shaderFamily = isShaderGraph ? "CustomLit" : InferShaderFamily(shader),
                    pipeline
                }) : JValue.CreateNull(),
                ["classification"] = new JObject
                {
                    ["supported"] = true,
                    ["transferMode"] = transferMode,
                    ["confidence"] = confidence,
                    ["targetModel"] = exportProfile,
                    ["notes"] = new JArray(BuildClassificationNotes(isShaderGraph, includeShaderGraph, recursiveShaderGraphs))
                },
                ["surface"] = new JObject
                {
                    ["surfaceType"] = surfaceType,
                    ["blendMode"] = blendMode,
                    ["alphaClip"] = alphaClip,
                    ["alphaCutoff"] = ToJToken(GetFloatValue(cutoffProperty)),
                    ["twoSided"] = twoSided,
                    ["cullMode"] = JValue.CreateNull(),
                    ["renderQueue"] = material.renderQueue,
                    ["enableInstancing"] = material.enableInstancing,
                    ["doubleSidedGI"] = material.doubleSidedGI,
                    ["globalIlluminationFlags"] = material.globalIlluminationFlags.ToString()
                },
                ["semantics"] = new JObject
                {
                    ["baseColor"] = BuildTextureSemantic(
                        baseMapProperty,
                        "baseColor",
                        new[] { "_BaseColor", "_BaseMap" },
                        baseColorProperty != null ? ToJToken(baseColorProperty.value) : JValue.CreateNull(),
                        null,
                        BuildUvTransform(baseMapProperty)),
                    ["normal"] = BuildTextureSemantic(
                        normalMapProperty,
                        "normal",
                        new[] { "_BumpMap" },
                        JValue.CreateNull(),
                        null,
                        BuildUvTransform(normalMapProperty),
                        new JObject { ["scale"] = 1.0 }),
                    ["metallic"] = new JObject
                    {
                        ["value"] = ToJToken(GetFloatValue(metallicProperty)),
                        ["textureId"] = GetTextureReference(metallicMapProperty) != null ? "metallicRoughnessMask" : null,
                        ["channel"] = GetTextureReference(metallicMapProperty) != null ? "r" : null,
                        ["rawPropertyNames"] = new JArray("_Metallic", "_MetallicGlossMap", "_Use_Metallic_Texture")
                    },
                    ["roughness"] = new JObject
                    {
                        ["value"] = ToJToken(roughness),
                        ["source"] = roughness.HasValue ? "derived_from_smoothness" : null,
                        ["textureId"] = GetTextureReference(metallicMapProperty) != null ? "metallicRoughnessMask" : null,
                        ["channel"] = GetTextureReference(metallicMapProperty) != null ? "a" : null,
                        ["rawPropertyNames"] = new JArray("_Smoothness", "_MetallicGlossMap"),
                        ["conversion"] = roughness.HasValue ? new JObject
                        {
                            ["kind"] = "one_minus_smoothness",
                            ["sourceValue"] = ToJToken(smoothness)
                        } : JValue.CreateNull()
                    },
                    ["emission"] = new JObject
                    {
                        ["enabled"] = (GetFloatValue(emissionToggleProperty) ?? 0f) > 0f,
                        ["color"] = emissionColorProperty != null ? ToJToken(emissionColorProperty.value) : JValue.CreateNull(),
                        ["textureId"] = GetTextureReference(emissionMapProperty) != null ? "emission" : null,
                        ["rawPropertyNames"] = new JArray("_Use_Emission", "_EmissionColor", "_EmissionMap")
                    },
                    ["opacity"] = new JObject
                    {
                        ["value"] = surfaceType == "Transparent" ? 1.0 : 1.0,
                        ["textureId"] = GetTextureReference(baseMapProperty) != null ? "baseColor" : null,
                        ["channel"] = GetTextureReference(baseMapProperty) != null ? "a" : null,
                        ["rawPropertyNames"] = new JArray("_BaseMap", "_Cutoff")
                    },
                    ["occlusion"] = new JObject
                    {
                        ["value"] = ToJToken(GetFloatValue(occlusionStrengthProperty)),
                        ["textureId"] = GetTextureReference(occlusionMapProperty) != null ? "occlusion" : null,
                        ["channel"] = GetTextureReference(occlusionMapProperty) != null ? "g" : null,
                        ["rawPropertyNames"] = new JArray("_OcclusionMap", "_OcclusionStrength")
                    },
                    ["uvTransform"] = new JObject
                    {
                        ["value"] = new JObject
                        {
                            ["tiling"] = tilingProperty != null ? ToJToken(tilingProperty.value) : JValue.CreateNull(),
                            ["offset"] = offsetProperty != null ? ToJToken(offsetProperty.value) : JValue.CreateNull()
                        },
                        ["rawPropertyNames"] = new JArray("_Tiling", "_Offset")
                    },
                    ["custom"] = customSemanticGroups
                },
                ["textures"] = textures,
                ["keywords"] = new JObject
                {
                    ["activeMaterialKeywords"] = new JArray(material.shaderKeywords ?? Array.Empty<string>()),
                    ["declaredShaderKeywords"] = shader != null ? new JArray(CompatServices.Shader.ReadKeywords(shader).Select(keyword => keyword.name)) : new JArray()
                },
                ["unityRawProperties"] = includeRawProperties ? BuildRawPropertyExports(properties) : new JArray(),
                ["warnings"] = warnings
            };

            if (includeShaderGraph && isShaderGraph && !string.IsNullOrWhiteSpace(shaderPath))
            {
                spec["shaderGraph"] = BuildShaderGraphSection(shaderPath, recursiveShaderGraphs);
            }

            return spec;
        }

        public static object GetShaderInfo(string path, string guid, bool includeUsage)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (shader == null)
            {
                throw new InvalidOperationException($"Asset at '{assetPath}' is not a Shader.");
            }

            var passes = CompatServices.Shader.ReadPasses(shader);
            var keywords = CompatServices.Shader.ReadKeywords(shader);
            var properties = CompatServices.Shader.ReadProperties(shader);
            var sourceInfo = ShaderSourceMetadataReader.Read(ToAbsoluteProjectPath(assetPath));
            var usageMaterials = includeUsage ? FindMaterialsForShader(shader.name) : null;

            return new ShaderInfoDto
            {
                asset = DescribeAssetReference(assetPath),
                name = shader.name,
                isSupported = shader.isSupported,
                maximumLOD = shader.maximumLOD,
                passCount = passes.Count,
                passes = passes,
                keywords = keywords,
                properties = properties,
                fallback = sourceInfo.fallback,
                customEditor = sourceInfo.customEditor,
                usage = usageMaterials != null ? new ShaderUsageDto
                {
                    materialCount = usageMaterials.Count,
                    materials = usageMaterials
                } : null
            };
        }

        public static object FindMaterialsUsingShader(string shaderName, string guid, bool includeRenderers)
        {
            Shader shader = null;
            string assetPath = null;

            if (!string.IsNullOrWhiteSpace(guid))
            {
                assetPath = ResolveAssetPath(null, guid);
                shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader == null)
                {
                    throw new InvalidOperationException($"Asset at '{assetPath}' is not a Shader.");
                }

                shaderName = shader.name;
            }

            if (string.IsNullOrWhiteSpace(shaderName))
            {
                throw new InvalidOperationException("Shader name or GUID is required.");
            }

            var materials = FindMaterialsForShader(shaderName);
            var renderers = includeRenderers ? FindLoadedRenderersUsingShader(shaderName) : new List<object>();

            return new
            {
                shader = shader != null ? DescribeShaderReference(shader) : new ShaderReferenceDto
                {
                    name = shaderName,
                    path = assetPath,
                    guid = guid
                },
                materials,
                materialCount = materials.Count,
                renderers,
                rendererCount = renderers.Count
            };
        }

        public static object GetShaderGraphInfo(string path, string guid)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var extension = Path.GetExtension(assetPath)?.ToLowerInvariant();
            if (extension != ".shadergraph" && extension != ".shadersubgraph")
            {
                throw new InvalidOperationException($"Asset at '{assetPath}' is not a Shader Graph asset.");
            }

            var parsed = CompatServices.ShaderGraph.Read(assetPath);
            var dependencyPaths = AssetDatabase.GetDependencies(assetPath, false)
                .Where(candidate => candidate != assetPath)
                .Where(candidate => string.Equals(Path.GetExtension(candidate), ".shadersubgraph", StringComparison.OrdinalIgnoreCase))
                .Select(DescribeAssetReference)
                .ToList();

            return new
            {
                asset = DescribeAssetReference(assetPath),
                sourceLength = parsed.sourceLength,
                subGraphDependencies = dependencyPaths,
                graph = parsed.graph
            };
        }

        public static object GetSceneInfo()
        {
            var scenes = GetLoadedScenes();
            var activeScene = SceneManager.GetActiveScene();

            return new
            {
                activeScene = new
                {
                    name = activeScene.name,
                    path = activeScene.path,
                    isLoaded = activeScene.isLoaded,
                    buildIndex = activeScene.buildIndex
                },
                loadedSceneCount = scenes.Count,
                scenes = scenes.Select(DescribeSceneSummary).ToList()
            };
        }

        public static object GetSceneRenderers(string scenePath)
        {
            var scenes = GetLoadedScenes();
            if (!string.IsNullOrWhiteSpace(scenePath))
            {
                scenes = scenes.Where(scene => string.Equals(scene.path, NormalizeAssetPath(scenePath), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (scenes.Count == 0)
            {
                throw new InvalidOperationException("No loaded scenes matched the requested scene path.");
            }

            return new
            {
                sceneCount = scenes.Count,
                scenes = scenes.Select(scene => new
                {
                    name = scene.name,
                    path = scene.path,
                    renderers = GetSceneRendererEntries(scene)
                }).ToList()
            };
        }

        private static List<Scene> GetLoadedScenes()
        {
            var scenes = new List<Scene>();
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (scene.IsValid() && scene.isLoaded)
                {
                    scenes.Add(scene);
                }
            }

            return scenes;
        }

        private static object DescribeSceneSummary(Scene scene)
        {
            var rootObjects = scene.GetRootGameObjects();
            var renderers = rootObjects.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToList();
            var lights = rootObjects.SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToList();
            var reflectionProbes = rootObjects.SelectMany(root => root.GetComponentsInChildren<ReflectionProbe>(true)).ToList();
            var volumes = rootObjects.SelectMany(GetVolumeComponents).ToList();

            return new
            {
                name = scene.name,
                path = scene.path,
                buildIndex = scene.buildIndex,
                isDirty = scene.isDirty,
                rootCount = scene.rootCount,
                rendererCount = renderers.Count,
                lightCount = lights.Count,
                reflectionProbeCount = reflectionProbes.Count,
                volumeCount = volumes.Count,
                lights = lights.Select(DescribeLight).ToList(),
                reflectionProbes = reflectionProbes.Select(DescribeReflectionProbe).ToList(),
                volumes = volumes.Select(DescribeVolume).ToList()
            };
        }

        private static List<object> GetSceneRendererEntries(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Select(renderer => new
                {
                    name = renderer.name,
                    gameObjectPath = GetTransformPath(renderer.transform),
                    rendererType = renderer.GetType().FullName,
                    enabled = renderer.enabled,
                    gameObjectActive = renderer.gameObject.activeInHierarchy,
                    sortingLayer = renderer.sortingLayerName,
                    sortingOrder = renderer.sortingOrder,
                    materials = renderer.sharedMaterials
                        .Select((material, index) => new
                        {
                            slot = index,
                            material = material != null ? DescribeMaterialReference(material) : null
                        })
                        .ToList()
                })
                .Cast<object>()
                .ToList();
        }

        private static List<object> FindLoadedRenderersUsingShader(string shaderName)
        {
            return GetLoadedScenes()
                .SelectMany(scene => scene.GetRootGameObjects())
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer.sharedMaterials.Any(material => material != null && material.shader != null && material.shader.name == shaderName))
                .Select(renderer => new
                {
                    scene = renderer.gameObject.scene.path,
                    renderer = renderer.name,
                    gameObjectPath = GetTransformPath(renderer.transform),
                    rendererType = renderer.GetType().FullName,
                    materials = renderer.sharedMaterials
                        .Where(material => material != null && material.shader != null && material.shader.name == shaderName)
                        .Select(DescribeMaterialReference)
                        .ToList()
                })
                .Cast<object>()
                .ToList();
        }

        private static List<MaterialReferenceDto> FindMaterialsForShader(string shaderName)
        {
            var materials = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                               || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                .Where(path => string.Equals(Path.GetExtension(path), ".mat", StringComparison.OrdinalIgnoreCase))
                .Select(path => new
                {
                    path,
                    material = AssetDatabase.LoadAssetAtPath<Material>(path)
                })
                .Where(entry => entry.material != null)
                .Where(entry => entry.material.shader != null && entry.material.shader.name == shaderName)
                .Select(entry => new MaterialReferenceDto
                {
                    name = entry.material.name,
                    path = entry.path,
                    guid = AssetDatabase.AssetPathToGUID(entry.path),
                    shader = DescribeShaderReference(entry.material.shader)
                })
                .ToList();

            return materials;
        }

        private static object GetImportSettings(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                return null;
            }

            if (importer is TextureImporter textureImporter)
            {
                return new
                {
                    importerType = importer.GetType().FullName,
                    textureType = textureImporter.textureType.ToString(),
                    alphaSource = textureImporter.alphaSource.ToString(),
                    mipmapEnabled = textureImporter.mipmapEnabled,
                    sRGBTexture = textureImporter.sRGBTexture,
                    npotScale = textureImporter.npotScale.ToString(),
                    wrapMode = textureImporter.wrapMode.ToString(),
                    filterMode = textureImporter.filterMode.ToString(),
                    anisoLevel = textureImporter.anisoLevel,
                    maxTextureSize = textureImporter.maxTextureSize
                };
            }

            return new
            {
                importerType = importer.GetType().FullName
            };
        }

        private static JArray BuildRawPropertyExports(IEnumerable<MaterialPropertyDto> properties)
        {
            var exports = new JArray();

            foreach (var property in properties)
            {
                exports.Add(new JObject
                {
                    ["name"] = property.name,
                    ["type"] = property.type,
                    ["mappedSemantic"] = ToJToken(GetMappedSemantic(property.name)),
                    ["value"] = ToJToken(property.value)
                });
            }
            return exports;
        }

        private static JObject BuildShaderGraphSection(string assetPath, bool recursive)
        {
            var bundle = BuildShaderGraphBundle(assetPath, recursive);
            var mainGraph = bundle["mainGraph"] as JObject;
            var summary = mainGraph?["graph"]?["summary"] != null ? (JObject)mainGraph["graph"]["summary"].DeepClone() : new JObject();

            return new JObject
            {
                ["summary"] = summary,
                ["bundle"] = bundle
            };
        }

        private static JObject BuildShaderGraphBundle(string assetPath, bool recursive)
        {
            var guidMap = AssetDatabase.GetAllAssetPaths()
                .Where(path => IsShaderGraphAssetPath(path) || IsSubGraphAssetPath(path))
                .ToDictionary(path => AssetDatabase.AssetPathToGUID(path), NormalizeAssetPath, StringComparer.OrdinalIgnoreCase);

            var exportedGraphs = new Dictionary<string, GraphExportRecord>(StringComparer.OrdinalIgnoreCase);
            var mainGraph = ExportShaderGraph(assetPath, true, recursive, guidMap, exportedGraphs);
            var subgraphs = new JArray(exportedGraphs.Values
                .Where(record => !string.Equals(record.assetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.exportFile, StringComparer.OrdinalIgnoreCase)
                .Select(record => record.payload));
            var index = new JArray(exportedGraphs.Values
                .Where(record => !string.Equals(record.assetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.exportFile, StringComparer.OrdinalIgnoreCase)
                .Select(record => new JObject
                {
                    ["name"] = record.name,
                    ["path"] = record.assetPath,
                    ["guid"] = record.guid,
                    ["exportFile"] = record.exportFile
                }));

            return new JObject
            {
                ["mainGraphFile"] = mainGraph.exportFile,
                ["subgraphsDirectory"] = "subgraphs",
                ["indexFile"] = "subgraphs/index.json",
                ["recursive"] = recursive,
                ["mainGraph"] = mainGraph.payload,
                ["subgraphs"] = subgraphs,
                ["index"] = index
            };
        }

        private static GraphExportRecord ExportShaderGraph(string assetPath, bool isMainGraph, bool recursive, IReadOnlyDictionary<string, string> guidMap, IDictionary<string, GraphExportRecord> exportedGraphs)
        {
            assetPath = NormalizeAssetPath(assetPath);
            if (exportedGraphs.TryGetValue(assetPath, out var existing))
            {
                return existing;
            }

            var adapter = new GraphSchemaAdapter();
            var text = File.ReadAllText(ToAbsoluteProjectPath(assetPath));
            var envelope = adapter.BuildEnvelope(GraphEnvelopeReader.ParseObjects(text));
            var root = envelope.root;
            var objectMap = envelope.objectMap;
            if (root == null)
            {
                throw new InvalidOperationException($"Shader Graph file at '{assetPath}' did not contain a parseable graph root.");
            }

            var properties = adapter.ResolveObjectList(root["m_Properties"], objectMap);
            var keywords = adapter.ResolveObjectList(root["m_Keywords"], objectMap);
            var categories = adapter.ResolveObjectList(root["m_CategoryData"], objectMap);
            var groups = adapter.ResolveObjectList(root["m_GroupDatas"], objectMap);
            var stickyNotes = adapter.ResolveObjectList(root["m_StickyNoteDatas"], objectMap);
            var nodes = adapter.ResolveObjectList(root["m_Nodes"], objectMap);
            var edges = adapter.ResolveObjectList(root["m_Edges"], objectMap);
            var propertyIds = properties.Select(property => property.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var keywordIds = keywords.Select(keyword => keyword.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var nodeIds = nodes.Select(node => node.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var categoryMembership = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var categoryExports = new JArray();

            foreach (var category in categories)
            {
                var childObjects = adapter.ResolveObjectList(category["m_ChildObjectList"], objectMap);
                var childObjectIds = new JArray(childObjects.Select(child => child.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)));
                foreach (var childObjectId in childObjectIds.Values<string>())
                {
                    if (!categoryMembership.TryGetValue(childObjectId, out var memberships))
                    {
                        memberships = new List<string>();
                        categoryMembership[childObjectId] = memberships;
                    }

                    memberships.Add(category.Value<string>("m_ObjectId"));
                }

                categoryExports.Add(new JObject
                {
                    ["objectId"] = category.Value<string>("m_ObjectId"),
                    ["name"] = ToJToken(adapter.FirstString(category, "m_Name", "m_DisplayName")),
                    ["childCount"] = childObjects.Count,
                    ["childObjectIds"] = childObjectIds,
                    ["childObjects"] = new JArray(childObjects.Select(child => SimplifyGraphObjectReference(child, propertyIds, keywordIds, nodeIds, adapter)))
                });
            }

            var nodeExports = new JArray(nodes.Select(node => new JObject
            {
                ["objectId"] = node.Value<string>("m_ObjectId"),
                ["type"] = node.Value<string>("m_Type"),
                ["displayName"] = ToJToken(adapter.FirstString(node, "m_Name", "m_DisplayName")),
                ["position"] = BuildPositionObject(node["m_DrawState"]?["m_Position"] as JObject),
                ["groupId"] = ToJToken(node["m_Group"]?["m_Id"]?.Value<string>()),
                ["categoryIds"] = categoryMembership.TryGetValue(node.Value<string>("m_ObjectId"), out var memberships) ? new JArray(memberships) : new JArray(),
                ["subGraphGuid"] = ToJToken(adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"])),
                ["subGraphPath"] = ToJToken(ResolveSubGraphPath(adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"]), guidMap)),
                ["slots"] = node["m_Slots"] is JArray slots ? slots.Count : node["m_SerializableSlots"] is JArray legacySlots ? legacySlots.Count : 0
            }));

            var edgeExports = new JArray(edges.Select(edge => new JObject
            {
                ["objectId"] = ToJToken(edge.Value<string>("m_ObjectId")),
                ["outputNodeId"] = ToJToken(edge["m_OutputSlot"]?["m_Node"]?["m_Id"]?.Value<string>() ?? edge["m_OutputSlot"]?.Value<string>("m_NodeGUIDSerialized")),
                ["outputSlotId"] = ToJToken(edge["m_OutputSlot"]?.Value<int?>("m_SlotId")),
                ["inputNodeId"] = ToJToken(edge["m_InputSlot"]?["m_Node"]?["m_Id"]?.Value<string>() ?? edge["m_InputSlot"]?.Value<string>("m_NodeGUIDSerialized")),
                ["inputSlotId"] = ToJToken(edge["m_InputSlot"]?.Value<int?>("m_SlotId"))
            }));

            var groupExports = new JArray(groups.Select(group => new JObject
            {
                ["objectId"] = group.Value<string>("m_ObjectId"),
                ["title"] = ToJToken(adapter.FirstString(group, "m_Title", "m_Name", "m_DisplayName")),
                ["position"] = BuildPositionObject(group["m_DrawState"]?["m_Position"] as JObject ?? group["m_Position"] as JObject),
                ["containedObjectIds"] = new JArray(adapter.ResolveObjectList(group["m_ContainedNodes"], objectMap)
                    .Concat(adapter.ResolveObjectList(group["m_Items"], objectMap))
                    .Concat(adapter.ResolveObjectList(group["m_GroupItems"], objectMap))
                    .Select(item => item.Value<string>("m_ObjectId"))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
            }));

            var stickyExports = new JArray(stickyNotes.Select(note => new JObject
            {
                ["objectId"] = note.Value<string>("m_ObjectId"),
                ["title"] = ToJToken(note.Value<string>("m_Title")),
                ["content"] = ToJToken(note.Value<string>("m_Content")),
                ["theme"] = ToJToken(note.Value<string>("m_Theme") ?? note.Value<int?>("m_Theme")?.ToString()),
                ["textSize"] = ToJToken(note.Value<int?>("m_TextSize")),
                ["position"] = BuildPositionObject(note["m_Position"] as JObject)
            }));

            var targetExports = new JArray(adapter.ResolveObjectList(root["m_ActiveTargets"], objectMap).Select(target => new JObject
            {
                ["objectId"] = target.Value<string>("m_ObjectId"),
                ["type"] = target.Value<string>("m_Type"),
                ["displayName"] = ToJToken(adapter.FirstString(target, "m_DisplayName", "m_Name", "m_TargetId")),
                ["activeSubTarget"] = ToJToken(target["m_ActiveSubTarget"]?.ToString(Newtonsoft.Json.Formatting.None))
            }));

            var subGraphReferences = new JArray();
            var record = new GraphExportRecord
            {
                assetPath = assetPath,
                guid = AssetDatabase.AssetPathToGUID(assetPath),
                name = Path.GetFileNameWithoutExtension(assetPath),
                exportFile = BuildShaderGraphExportPath(assetPath, isMainGraph),
                payload = new JObject()
            };
            exportedGraphs[assetPath] = record;

            foreach (var node in nodes)
            {
                var subGraphGuid = adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"]);
                if (string.IsNullOrWhiteSpace(subGraphGuid))
                {
                    continue;
                }

                var subGraphPath = ResolveSubGraphPath(subGraphGuid, guidMap);
                var subgraphExportFile = !string.IsNullOrWhiteSpace(subGraphPath) ? BuildShaderGraphExportPath(subGraphPath, false) : null;

                if (recursive && !string.IsNullOrWhiteSpace(subGraphPath))
                {
                    subgraphExportFile = ExportShaderGraph(subGraphPath, false, true, guidMap, exportedGraphs).exportFile;
                }

                subGraphReferences.Add(new JObject
                {
                    ["objectId"] = node.Value<string>("m_ObjectId"),
                    ["displayName"] = ToJToken(adapter.FirstString(node, "m_Name", "m_DisplayName")),
                    ["subGraphGuid"] = subGraphGuid,
                    ["assetPath"] = ToJToken(subGraphPath),
                    ["exportFile"] = ToJToken(subgraphExportFile)
                });
            }

            record.payload = new JObject
            {
                ["schemaVersion"] = "unity-shadergraph-export/1.0",
                ["exportFile"] = record.exportFile,
                ["asset"] = JObject.FromObject(new
                {
                    name = record.name,
                    path = assetPath,
                    guid = record.guid,
                    type = IsSubGraphAssetPath(assetPath) ? "UnityEditor.ShaderGraph.SubGraphAsset" : "UnityEngine.Shader"
                }),
                ["graph"] = new JObject
                {
                    ["format"] = envelope.format,
                    ["rootType"] = ToJToken(root.Value<string>("m_Type")),
                    ["summary"] = new JObject
                    {
                        ["propertyCount"] = properties.Count,
                        ["keywordCount"] = keywords.Count,
                        ["categoryCount"] = categories.Count,
                        ["groupCount"] = groups.Count,
                        ["stickyNoteCount"] = stickyNotes.Count,
                        ["nodeCount"] = nodes.Count,
                        ["edgeCount"] = edges.Count,
                        ["subGraphReferenceCount"] = subGraphReferences.Count,
                        ["targetCount"] = targetExports.Count
                    },
                    ["properties"] = new JArray(properties.Select(property => new JObject
                    {
                        ["objectId"] = property.Value<string>("m_ObjectId"),
                        ["type"] = property.Value<string>("m_Type"),
                        ["displayName"] = ToJToken(adapter.FirstString(property, "m_DisplayName", "m_Name")),
                        ["referenceName"] = ToJToken(property.Value<string>("m_OverrideReferenceName")
                            ?? adapter.FirstString(property, "m_RefNameGeneratedByDisplayName", "m_ReferenceName", "m_DefaultReferenceName", "m_Name")),
                        ["valueType"] = ToJToken(adapter.FirstString(property, "m_ValueType", "m_Type")),
                        ["groupId"] = ToJToken(property["m_Group"]?["m_Id"]?.Value<string>())
                    })),
                    ["keywords"] = new JArray(keywords.Select(keyword => new JObject
                    {
                        ["objectId"] = keyword.Value<string>("m_ObjectId"),
                        ["type"] = keyword.Value<string>("m_Type"),
                        ["displayName"] = ToJToken(adapter.FirstString(keyword, "m_DisplayName", "m_Name")),
                        ["referenceName"] = ToJToken(adapter.FirstString(keyword, "m_ReferenceName", "m_RefNameGeneratedByDisplayName", "m_Name")),
                        ["definition"] = ToJToken(adapter.FirstString(keyword, "m_KeywordDefinition")),
                        ["scope"] = ToJToken(adapter.FirstString(keyword, "m_Scope")),
                        ["groupId"] = ToJToken(keyword["m_Group"]?["m_Id"]?.Value<string>())
                    })),
                    ["categories"] = categoryExports,
                    ["groups"] = groupExports,
                    ["stickyNotes"] = stickyExports,
                    ["nodes"] = nodeExports,
                    ["edges"] = edgeExports,
                    ["targets"] = targetExports,
                    ["output"] = new JObject
                    {
                        ["outputNode"] = BuildOutputNode(root["m_OutputNode"] as JObject, objectMap, adapter),
                        ["vertexBlocks"] = BuildGraphBlocks(root["m_VertexContext"]?["m_Blocks"], objectMap, adapter),
                        ["fragmentBlocks"] = BuildGraphBlocks(root["m_FragmentContext"]?["m_Blocks"], objectMap, adapter)
                    },
                    ["subGraphReferences"] = subGraphReferences,
                    ["warnings"] = new JArray(envelope.warnings ?? new List<string>())
                }
            };

            return record;
        }

        private static JObject BuildTextureSemantic(MaterialPropertyDto textureProperty, string textureId, IEnumerable<string> rawPropertyNames, JToken constantValue, string channel, JObject uv, JObject extraFields = null)
        {
            var result = new JObject
            {
                ["value"] = constantValue ?? JValue.CreateNull(),
                ["textureId"] = GetTextureReference(textureProperty) != null ? textureId : null,
                ["channel"] = ToJToken(channel),
                ["rawPropertyNames"] = new JArray(rawPropertyNames),
                ["uv"] = uv ?? JValue.CreateNull()
            };

            if (extraFields != null)
            {
                foreach (var property in extraFields.Properties())
                {
                    result[property.Name] = property.Value;
                }
            }

            return result;
        }

        private static JObject BuildUvTransform(MaterialPropertyDto textureProperty)
        {
            return new JObject
            {
                ["set"] = 0,
                ["scale"] = ToJToken(GetTextureTransform(textureProperty, "scale")),
                ["offset"] = ToJToken(GetTextureTransform(textureProperty, "offset"))
            };
        }

        private static void AddTextureExport(JArray textures, string materialName, string semantic, string propertyName, MaterialPropertyDto property, JObject channelPacking)
        {
            var textureReference = GetTextureReference(property);
            if (textureReference == null)
            {
                return;
            }

            var absolutePath = ToAbsoluteProjectPath(textureReference.path);
            var importSettings = GetImportSettings(textureReference.path);
            var textureType = ReadOptionalMember(importSettings, "textureType")?.ToString();
            var isNormalMap = string.Equals(textureType, "NormalMap", StringComparison.OrdinalIgnoreCase);
            var sRgb = ReadOptionalMember(importSettings, "sRGBTexture") as bool?;
            var extension = Path.GetExtension(textureReference.path);

            textures.Add(new JObject
            {
                ["id"] = GetTextureExportId(semantic),
                ["semantic"] = semantic,
                ["unityPropertyName"] = propertyName,
                ["asset"] = JObject.FromObject(textureReference),
                ["sourceFile"] = new JObject
                {
                    ["absolutePath"] = absolutePath.Replace('\\', '/'),
                    ["extension"] = extension,
                    ["exists"] = File.Exists(absolutePath)
                },
                ["exportFile"] = new JObject
                {
                    ["fileName"] = $"{materialName}__{GetSuggestedTextureExportName(semantic)}{extension}",
                    ["relativePath"] = $"{materialName}__{GetSuggestedTextureExportName(semantic)}{extension}"
                },
                ["usage"] = new JObject
                {
                    ["colorSpace"] = isNormalMap || sRgb == false ? "Linear" : "sRGB",
                    ["isNormalMap"] = isNormalMap,
                    ["uvSet"] = 0,
                    ["channelPacking"] = channelPacking
                },
                ["importSettings"] = ToJToken(importSettings)
            });
        }

        private static void AddCustomSemanticGroup(JArray customSemanticGroups, string semantic, MaterialPropertyDto textureProperty, IEnumerable<string> rawPropertyNames, JObject parameters)
        {
            customSemanticGroups.Add(new JObject
            {
                ["semantic"] = semantic,
                ["textureId"] = GetTextureReference(textureProperty) != null ? GetTextureExportId(semantic) : JValue.CreateNull(),
                ["rawPropertyNames"] = new JArray(rawPropertyNames),
                ["parameters"] = parameters ?? JValue.CreateNull()
            });
        }

        private static MaterialPropertyDto FindProperty(IReadOnlyDictionary<string, MaterialPropertyDto> propertyMap, string name)
        {
            return propertyMap.TryGetValue(name, out var property) ? property : null;
        }

        private static float? GetFloatValue(MaterialPropertyDto property)
        {
            if (property == null)
            {
                return null;
            }

            if (property.value is float floatValue)
            {
                return floatValue;
            }

            if (property.value is double doubleValue)
            {
                return (float)doubleValue;
            }

            if (property.value is int intValue)
            {
                return intValue;
            }

            if (property.value is long longValue)
            {
                return longValue;
            }

            return null;
        }

        private static AssetReferenceDto GetTextureReference(MaterialPropertyDto property)
        {
            return ReadOptionalMember(property?.value, "texture") as AssetReferenceDto;
        }

        private static object GetTextureTransform(MaterialPropertyDto property, string memberName)
        {
            return ReadOptionalMember(property?.value, memberName);
        }

        private static string GetMappedSemantic(string propertyName)
        {
            switch (propertyName)
            {
                case "_BaseMap":
                    return "baseColor.texture";
                case "_BaseColor":
                    return "baseColor.value";
                case "_Cutoff":
                    return "surface.alphaCutoff";
                case "_MetallicGlossMap":
                    return "metallic.texture";
                case "_Smoothness":
                    return "roughness.value";
                case "_Metallic":
                    return "metallic.value";
                case "_BumpMap":
                    return "normal.texture";
                case "_OcclusionMap":
                    return "occlusion.texture";
                case "_OcclusionStrength":
                    return "occlusion.value";
                case "_Use_Emission":
                    return "emission.enabled";
                case "_EmissionMap":
                    return "emission.texture";
                case "_EmissionColor":
                    return "emission.color";
                case "_Tiling":
                    return "uvTransform.tiling";
                case "_Offset":
                    return "uvTransform.offset";
                case "_GuideTexture":
                    return "custom.guideTexture.texture";
                case "_GuideTiling":
                    return "custom.guideTexture.guideTiling";
                case "_GuideStrength":
                    return "custom.guideTexture.guideStrength";
                default:
                    return propertyName != null && propertyName.StartsWith("unity_", StringComparison.Ordinal) ? null : $"custom.{propertyName}";
            }
        }

        private static JToken ToJToken(object value)
        {
            return value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }

        private static bool HasKeyword(IEnumerable<string> shaderKeywords, string keyword)
        {
            return shaderKeywords != null && shaderKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);
        }

        private static string InferPipeline(Shader shader)
        {
            if (shader == null)
            {
                return null;
            }

            if (shader.name.IndexOf("Universal Render Pipeline", StringComparison.OrdinalIgnoreCase) >= 0
                || shader.name.IndexOf("Shader Graphs/", StringComparison.OrdinalIgnoreCase) >= 0
                || !string.IsNullOrWhiteSpace(CompatServices.Context.renderPipelinePackageVersion))
            {
                return "URP";
            }

            return "BuiltInOrUnknown";
        }

        private static string InferShaderFamily(Shader shader)
        {
            if (shader == null)
            {
                return null;
            }

            if (shader.name.IndexOf("Lit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Lit";
            }

            return "Custom";
        }

        private static IEnumerable<string> BuildClassificationNotes(bool isShaderGraph, bool includeShaderGraph, bool recursiveShaderGraphs)
        {
            yield return "Core PBR channels are exported as transferable semantics.";

            if (isShaderGraph)
            {
                yield return "This material is driven by a Shader Graph and may require custom graph reconstruction in downstream tools.";
            }

            if (includeShaderGraph)
            {
                yield return recursiveShaderGraphs
                    ? "Shader Graph bundle includes recursively referenced subgraphs."
                    : "Shader Graph bundle only includes the main graph; subgraph references are preserved but not expanded.";
            }
        }

        private static JObject BuildWarning(string code, string message, string severity)
        {
            return new JObject
            {
                ["code"] = code,
                ["message"] = message,
                ["severity"] = severity
            };
        }

        private static JObject BuildChannelPacking(string r, string g, string b, string a)
        {
            return new JObject
            {
                ["r"] = r,
                ["g"] = g,
                ["b"] = b,
                ["a"] = a
            };
        }

        private static string GetTextureExportId(string semantic)
        {
            switch (semantic)
            {
                case "baseColor":
                    return "baseColor";
                case "normal":
                    return "normal";
                case "metallicRoughnessMask":
                    return "metallicRoughnessMask";
                case "occlusion":
                    return "occlusion";
                case "emission":
                    return "emission";
                default:
                    return semantic.Replace('.', '_');
            }
        }

        private static string GetSuggestedTextureExportName(string semantic)
        {
            switch (semantic)
            {
                case "baseColor":
                    return "BaseColor";
                case "normal":
                    return "Normal";
                case "metallicRoughnessMask":
                    return "MetallicRoughnessMask";
                case "occlusion":
                    return "Occlusion";
                case "emission":
                    return "Emission";
                case "custom.guideTexture":
                    return "GuideTexture";
                default:
                    return semantic.Replace('.', '_');
            }
        }

        private static string BuildShaderGraphExportPath(string assetPath, bool isMainGraph)
        {
            if (isMainGraph)
            {
                return "shadergraph.json";
            }

            var normalizedPath = NormalizeAssetPath(assetPath);
            const string shadersRoot = "Assets/Shaders/";
            if (normalizedPath.StartsWith(shadersRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "subgraphs/" + normalizedPath.Substring(shadersRoot.Length)
                    .Replace(".shadergraph", ".json")
                    .Replace(".shadersubgraph", ".json");
            }

            return "subgraphs/" + Path.GetFileNameWithoutExtension(normalizedPath) + ".json";
        }

        private static string ResolveSubGraphPath(string guid, IReadOnlyDictionary<string, string> guidMap)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            return guidMap.TryGetValue(guid, out var assetPath) ? assetPath : null;
        }

        private static bool IsShaderGraphAssetPath(string assetPath)
        {
            return string.Equals(Path.GetExtension(assetPath), ".shadergraph", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubGraphAssetPath(string assetPath)
        {
            return string.Equals(Path.GetExtension(assetPath), ".shadersubgraph", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject BuildPositionObject(JObject position)
        {
            if (position == null)
            {
                return null;
            }

            return new JObject
            {
                ["x"] = ToJToken(position.Value<float?>("x")),
                ["y"] = ToJToken(position.Value<float?>("y")),
                ["width"] = ToJToken(position.Value<float?>("width")),
                ["height"] = ToJToken(position.Value<float?>("height"))
            };
        }

        private static JObject SimplifyGraphObjectReference(JObject source, ISet<string> propertyIds, ISet<string> keywordIds, ISet<string> nodeIds, GraphSchemaAdapter adapter)
        {
            var objectId = source.Value<string>("m_ObjectId");
            return new JObject
            {
                ["objectId"] = objectId,
                ["type"] = source.Value<string>("m_Type"),
                ["displayName"] = ToJToken(adapter.FirstString(source, "m_Name", "m_DisplayName")),
                ["kind"] = propertyIds.Contains(objectId) ? "property" : keywordIds.Contains(objectId) ? "keyword" : nodeIds.Contains(objectId) ? "node" : "object"
            };
        }

        private static JObject BuildOutputNode(JObject token, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter)
        {
            var objectId = token?["m_Id"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(objectId) || !objectMap.TryGetValue(objectId, out var resolved))
            {
                return null;
            }

            return new JObject
            {
                ["objectId"] = objectId,
                ["type"] = resolved.Value<string>("m_Type"),
                ["displayName"] = ToJToken(adapter.FirstString(resolved, "m_Name", "m_DisplayName"))
            };
        }

        private static JArray BuildGraphBlocks(JToken blocksToken, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter)
        {
            return new JArray(adapter.ResolveObjectList(blocksToken, objectMap).Select(block => new JObject
            {
                ["objectId"] = block.Value<string>("m_ObjectId"),
                ["type"] = block.Value<string>("m_Type"),
                ["descriptor"] = ToJToken(adapter.FirstString(block, "m_SerializedDescriptor", "m_Name"))
            }));
        }

        private static List<object> GetDependencyReferences(string assetPath, bool recursive)
        {
            return AssetDatabase.GetDependencies(assetPath, recursive)
                .Where(candidate => candidate != assetPath)
                .Select(DescribeAssetReference)
                .Cast<object>()
                .ToList();
        }

        private static List<object> FindAssetsReferencing(string assetPath)
        {
            var results = new List<object>();
            foreach (var candidatePath in AssetDatabase.GetAllAssetPaths())
            {
                if (candidatePath == assetPath)
                {
                    continue;
                }

                if (!candidatePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    && !candidatePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dependencies = AssetDatabase.GetDependencies(candidatePath, false);
                if (dependencies.Contains(assetPath))
                {
                    results.Add(DescribeAssetReference(candidatePath));
                }
            }

            return results;
        }

        internal static AssetReferenceDto DescribeAssetReference(string assetPath)
        {
            var normalizedPath = NormalizeAssetPath(assetPath);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(normalizedPath);
            var assetType = AssetDatabase.GetMainAssetTypeAtPath(normalizedPath);

            return new AssetReferenceDto
            {
                name = mainAsset != null ? mainAsset.name : Path.GetFileNameWithoutExtension(normalizedPath),
                path = normalizedPath,
                guid = AssetDatabase.AssetPathToGUID(normalizedPath),
                type = assetType != null ? assetType.FullName : null
            };
        }

        internal static ShaderReferenceDto DescribeShaderReference(Shader shader)
        {
            var path = AssetDatabase.GetAssetPath(shader);
            return new ShaderReferenceDto
            {
                name = shader.name,
                path = path,
                guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path)
            };
        }

        internal static MaterialReferenceDto DescribeMaterialReference(Material material)
        {
            var path = AssetDatabase.GetAssetPath(material);
            return new MaterialReferenceDto
            {
                name = material.name,
                path = path,
                guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path),
                shader = material.shader != null ? DescribeShaderReference(material.shader) : null
            };
        }

        internal static AssetReferenceDto DescribeTextureReference(Texture texture)
        {
            var path = AssetDatabase.GetAssetPath(texture);
            return new AssetReferenceDto
            {
                name = texture.name,
                path = path,
                guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path),
                type = texture.GetType().FullName
            };
        }

        private static object DescribeLight(Light light)
        {
            return new
            {
                name = light.name,
                gameObjectPath = GetTransformPath(light.transform),
                type = light.type.ToString(),
                intensity = light.intensity,
                range = light.range,
                color = ToColorObject(light.color),
                shadows = light.shadows.ToString()
            };
        }

        private static object DescribeReflectionProbe(ReflectionProbe probe)
        {
            return new
            {
                name = probe.name,
                gameObjectPath = GetTransformPath(probe.transform),
                mode = probe.mode.ToString(),
                importance = probe.importance,
                boxProjection = probe.boxProjection,
                bounds = new
                {
                    center = ToVector3Object(probe.center),
                    size = ToVector3Object(probe.size)
                }
            };
        }

        private static IEnumerable<Component> GetVolumeComponents(GameObject root)
        {
            return root.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && component.GetType().FullName == "UnityEngine.Rendering.Volume");
        }

        private static object DescribeVolume(Component volume)
        {
            var type = volume.GetType();
            return new
            {
                name = volume.name,
                gameObjectPath = GetTransformPath(volume.transform),
                type = type.FullName,
                isGlobal = ReadOptionalMember(volume, "isGlobal"),
                priority = ReadOptionalMember(volume, "priority"),
                blendDistance = ReadOptionalMember(volume, "blendDistance"),
                sharedProfile = DescribeUnityObject(ReadOptionalMember(volume, "sharedProfile") as UnityEngine.Object)
            };
        }

        private static AssetReferenceDto DescribeUnityObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(value);
            return new AssetReferenceDto
            {
                name = value.name,
                path = path,
                guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path),
                type = value.GetType().FullName
            };
        }

        private static string ResolveAssetPath(string path, string guid)
        {
            if (!string.IsNullOrWhiteSpace(guid))
            {
                path = AssetDatabase.GUIDToAssetPath(guid.Trim());
            }

            path = NormalizeAssetPath(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Asset path or GUID is required.");
            }

            var resolvedGuid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrWhiteSpace(resolvedGuid))
            {
                throw new FileNotFoundException($"Asset was not found at '{path}'.");
            }

            return path;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/');
        }

        internal static string ToAbsoluteProjectPath(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                return assetPath;
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }

        private static string GetTransformPath(Transform transform)
        {
            var parts = new Stack<string>();
            var current = transform;

            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", parts);
        }

        internal static object ToColorObject(Color color)
        {
            return new
            {
                r = color.r,
                g = color.g,
                b = color.b,
                a = color.a
            };
        }

        internal static object ToVector4Object(Vector4 value)
        {
            return new
            {
                x = value.x,
                y = value.y,
                z = value.z,
                w = value.w
            };
        }

        internal static object ToVector2Object(Vector2 value)
        {
            return new
            {
                x = value.x,
                y = value.y
            };
        }

        internal static object ToVector3Object(Vector3 value)
        {
            return new
            {
                x = value.x,
                y = value.y,
                z = value.z
            };
        }

        private static object ReadOptionalMember(object instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            var type = instance.GetType();
            var property = type.GetProperty(memberName);
            if (property != null)
            {
                return property.GetValue(instance, null);
            }

            var field = type.GetField(memberName);
            return field?.GetValue(instance);
        }

        private sealed class GraphExportRecord
        {
            public string assetPath;
            public string exportFile;
            public string guid;
            public string name;
            public JObject payload;
        }

    }
}
