using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TA.UnityMcp.Compat;
using TA.UnityMcp.Compat.Shader;
using TA.UnityMcp.Compat.ShaderGraph;
using TA.UnityMcp.Contracts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TA.UnityMcp
{
    internal static class UnityMcpQueries
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
                importSettings = MaterialExportSpecBuilder.GetImportSettings(assetPath)
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

            return MaterialExportSpecBuilder.Build(material, assetPath, exportProfile, includeShaderGraph, recursiveShaderGraphs, includeRawProperties);
        }

        public static object CreateMaterialTransferPackage(JObject request)
        {
            return MaterialTransferPackageWriter.Create(request ?? new JObject());
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
            var usageMaterials = includeUsage ? FindMaterialsForShader(assetPath, shader.name) : null;

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

            var normalizedShaderPath = NormalizeAssetPath(assetPath);
            var materials = FindMaterialsForShader(normalizedShaderPath, shaderName);
            var renderers = includeRenderers ? FindLoadedRenderersUsingShader(normalizedShaderPath, shaderName) : new List<object>();

            return new
            {
                shader = shader != null ? DescribeShaderReference(shader) : new ShaderReferenceDto
                {
                    name = shaderName,
                    path = assetPath,
                    guid = guid
                },
                matchMode = !string.IsNullOrWhiteSpace(normalizedShaderPath) ? "shaderAsset" : "shaderName",
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

        public static object GetRenderPipelineInfo()
        {
            var activePipeline = GraphicsSettings.currentRenderPipeline ?? QualitySettings.renderPipeline;
            var defaultPipeline = GraphicsSettings.defaultRenderPipeline;
            var activeQualityLevel = QualitySettings.GetQualityLevel();
            var qualityNames = QualitySettings.names ?? Array.Empty<string>();

            return new
            {
                activePipeline = DescribeRenderPipelineAsset(activePipeline),
                defaultPipeline = DescribeRenderPipelineAsset(defaultPipeline),
                activeQualityLevel = activeQualityLevel >= 0 && activeQualityLevel < qualityNames.Length ? qualityNames[activeQualityLevel] : null,
                qualityLevels = qualityNames.Select((qualityName, index) => new
                {
                    name = qualityName,
                    index,
                    isActive = index == activeQualityLevel,
                    renderPipeline = DescribeRenderPipelineAsset(ReadQualityRenderPipeline(index))
                }).ToList(),
                activeQualitySettings = new
                {
                    antiAliasing = QualitySettings.antiAliasing,
                    anisotropicFiltering = QualitySettings.anisotropicFiltering.ToString(),
                    lodBias = QualitySettings.lodBias,
                    masterTextureLimit = QualitySettings.masterTextureLimit,
                    maximumLODLevel = QualitySettings.maximumLODLevel,
                    pixelLightCount = QualitySettings.pixelLightCount,
                    realtimeReflectionProbes = QualitySettings.realtimeReflectionProbes,
                    softParticles = QualitySettings.softParticles,
                    softVegetation = QualitySettings.softVegetation,
                    shadowCascades = QualitySettings.shadowCascades,
                    shadowDistance = QualitySettings.shadowDistance,
                    shadowProjection = QualitySettings.shadowProjection.ToString(),
                    shadowResolution = QualitySettings.shadowResolution.ToString(),
                    shadows = QualitySettings.shadows.ToString(),
                    vSyncCount = QualitySettings.vSyncCount
                }
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

        public static object GetSceneLights(string scenePath, string layers, string tag)
        {
            var layerFilters = ParseCsvValues(layers);
            var scenes = GetFilteredScenes(scenePath);

            return new
            {
                sceneCount = scenes.Count,
                filters = new
                {
                    scenePath = NormalizeAssetPath(scenePath),
                    layers = layerFilters,
                    tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim()
                },
                scenes = scenes.Select(scene => new
                {
                    name = scene.name,
                    path = scene.path,
                    lightCount = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                        .Count(light => MatchesSceneObjectFilter(light.gameObject, layerFilters, tag)),
                    lights = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                        .Where(light => MatchesSceneObjectFilter(light.gameObject, layerFilters, tag))
                        .Select(DescribeLight)
                        .ToList()
                }).ToList()
            };
        }

        public static object GetSceneVolumes(string scenePath, string layers, string tag)
        {
            var layerFilters = ParseCsvValues(layers);
            var scenes = GetFilteredScenes(scenePath);

            return new
            {
                sceneCount = scenes.Count,
                filters = new
                {
                    scenePath = NormalizeAssetPath(scenePath),
                    layers = layerFilters,
                    tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim()
                },
                scenes = scenes.Select(scene => new
                {
                    name = scene.name,
                    path = scene.path,
                    volumeCount = scene.GetRootGameObjects()
                        .SelectMany(GetVolumeComponents)
                        .Count(volume => MatchesSceneObjectFilter(volume.gameObject, layerFilters, tag)),
                    volumes = scene.GetRootGameObjects()
                        .SelectMany(GetVolumeComponents)
                        .Where(volume => MatchesSceneObjectFilter(volume.gameObject, layerFilters, tag))
                        .Select(DescribeVolume)
                        .ToList()
                }).ToList()
            };
        }

        public static object GetSceneRenderers(string scenePath)
        {
            var scenes = GetFilteredScenes(scenePath);

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

        public static object GetPrefabInfo(string path, string guid)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Asset at '{assetPath}' is not a Prefab.");
            }

            return new
            {
                asset = DescribeAssetReference(assetPath),
                prefabAssetType = PrefabUtility.GetPrefabAssetType(prefab).ToString(),
                prefabInstanceStatus = PrefabUtility.GetPrefabInstanceStatus(prefab).ToString(),
                hierarchy = DescribePrefabNode(prefab.transform, prefab.name),
                materialReferences = prefab.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials.Where(material => material != null))
                    .Distinct()
                    .Select(DescribeMaterialReference)
                    .ToList()
            };
        }

        public static object GetTextureInfo(string path, string guid)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture == null)
            {
                throw new InvalidOperationException($"Asset at '{assetPath}' is not a Texture.");
            }

            return new
            {
                asset = DescribeTextureReference(texture),
                width = texture.width,
                height = texture.height,
                dimension = texture.dimension.ToString(),
                mipmapCount = texture.mipmapCount,
                graphicsFormat = texture.graphicsFormat.ToString(),
                importSettings = MaterialExportSpecBuilder.GetImportSettings(assetPath),
                referencedBy = FindAssetsReferencing(assetPath)
            };
        }

        public static object GetAnimationInfo(string path, string guid)
        {
            var assetPath = ResolveAssetPath(path, guid);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller != null)
            {
                return new
                {
                    asset = DescribeAssetReference(assetPath),
                    kind = "AnimatorController",
                    layers = controller.layers.Select(layer => new
                    {
                        name = layer.name,
                        defaultWeight = layer.defaultWeight,
                        blendingMode = layer.blendingMode.ToString(),
                        stateMachine = new
                        {
                            states = layer.stateMachine.states.Select(state => new
                            {
                                name = state.state.name,
                                speed = state.state.speed,
                                tag = state.state.tag,
                                motion = state.state.motion != null ? state.state.motion.name : null,
                                writeDefaultValues = state.state.writeDefaultValues
                            }).ToList()
                        }
                    }).ToList(),
                    parameters = controller.parameters.Select(parameter => new
                    {
                        name = parameter.name,
                        type = parameter.type.ToString(),
                        defaultBool = parameter.defaultBool,
                        defaultFloat = parameter.defaultFloat,
                        defaultInt = parameter.defaultInt
                    }).ToList()
                };
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip != null)
            {
                var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
                return new
                {
                    asset = DescribeAssetReference(assetPath),
                    kind = "AnimationClip",
                    length = clip.length,
                    frameRate = clip.frameRate,
                    legacy = clip.legacy,
                    loopTime = clipSettings.loopTime,
                    curveBindings = AnimationUtility.GetCurveBindings(clip).Select(binding => new
                    {
                        path = binding.path,
                        propertyName = binding.propertyName,
                        type = binding.type.FullName
                    }).ToList(),
                    objectReferenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(binding => new
                    {
                        path = binding.path,
                        propertyName = binding.propertyName,
                        type = binding.type.FullName
                    }).ToList(),
                    events = AnimationUtility.GetAnimationEvents(clip).Select(animationEvent => new
                    {
                        functionName = animationEvent.functionName,
                        time = animationEvent.time,
                        floatParameter = animationEvent.floatParameter,
                        intParameter = animationEvent.intParameter,
                        stringParameter = animationEvent.stringParameter
                    }).ToList()
                };
            }

            throw new InvalidOperationException($"Asset at '{assetPath}' is not an AnimatorController or AnimationClip.");
        }

        public static object GetProjectSettings()
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

            return new
            {
                playerSettings = new
                {
                    companyName = PlayerSettings.companyName,
                    productName = PlayerSettings.productName,
                    colorSpace = PlayerSettings.colorSpace.ToString(),
                    activeInputHandling = ReadStaticPropertyValue(typeof(PlayerSettings), "activeInputHandler"),
                    scriptingBackend = PlayerSettings.GetScriptingBackend(buildTargetGroup).ToString(),
                    graphicsJobs = PlayerSettings.graphicsJobs
                },
                editorBuild = new
                {
                    activeBuildTarget = buildTarget.ToString(),
                    activeBuildTargetGroup = buildTargetGroup.ToString(),
                    development = EditorUserBuildSettings.development,
                    connectProfiler = EditorUserBuildSettings.connectProfiler
                },
                graphicsSettings = new
                {
                    defaultRenderPipeline = DescribeRenderPipelineAsset(GraphicsSettings.defaultRenderPipeline),
                    currentRenderPipeline = DescribeRenderPipelineAsset(GraphicsSettings.currentRenderPipeline ?? QualitySettings.renderPipeline),
                    lightsUseLinearIntensity = GraphicsSettings.lightsUseLinearIntensity,
                    lightsUseColorTemperature = GraphicsSettings.lightsUseColorTemperature,
                    transparencySortMode = GraphicsSettings.transparencySortMode.ToString(),
                    transparencySortAxis = ToVector3Object(GraphicsSettings.transparencySortAxis)
                }
            };
        }

        public static object GetProjectPackages()
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException($"Unity manifest was not found at '{manifestPath}'.");
            }

            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            var dependencies = manifest["dependencies"] is JObject dependencyObject
                ? dependencyObject.Properties().Select(property => new
                {
                    name = property.Name,
                    version = property.Value.ToString()
                }).OrderBy(entry => entry.name, StringComparer.OrdinalIgnoreCase).Cast<object>().ToList()
                : new List<object>();

            return new
            {
                manifestPath = manifestPath.Replace('\\', '/'),
                scopedRegistries = manifest["scopedRegistries"] ?? new JArray(),
                dependencies
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

        private static List<Scene> GetFilteredScenes(string scenePath)
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

            return scenes;
        }

        private static List<string> ParseCsvValues(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(',')
                    .Select(entry => entry.Trim())
                    .Where(entry => !string.IsNullOrWhiteSpace(entry))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        private static bool MatchesSceneObjectFilter(GameObject gameObject, IReadOnlyCollection<string> layerFilters, string tag)
        {
            if (gameObject == null)
            {
                return false;
            }

            if (layerFilters != null && layerFilters.Count > 0)
            {
                var layerName = LayerMask.LayerToName(gameObject.layer);
                var layerIndex = gameObject.layer.ToString();
                if (!layerFilters.Contains(layerName, StringComparer.OrdinalIgnoreCase)
                    && !layerFilters.Contains(layerIndex, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(tag) && !string.Equals(gameObject.tag, tag.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            return true;
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

        private static List<object> FindLoadedRenderersUsingShader(string shaderAssetPath, string shaderName)
        {
            return GetLoadedScenes()
                .SelectMany(scene => scene.GetRootGameObjects())
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer.sharedMaterials.Any(material => ShaderMatches(material?.shader, shaderAssetPath, shaderName)))
                .Select(renderer => new
                {
                    scene = renderer.gameObject.scene.path,
                    renderer = renderer.name,
                    gameObjectPath = GetTransformPath(renderer.transform),
                    rendererType = renderer.GetType().FullName,
                    materials = renderer.sharedMaterials
                        .Where(material => ShaderMatches(material?.shader, shaderAssetPath, shaderName))
                        .Select(DescribeMaterialReference)
                        .ToList()
                })
                .Cast<object>()
                .ToList();
        }

        private static List<MaterialReferenceDto> FindMaterialsForShader(string shaderAssetPath, string shaderName)
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
                .Where(entry => ShaderMatches(entry.material.shader, shaderAssetPath, shaderName))
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

        private static bool ShaderMatches(Shader candidate, string shaderAssetPath, string shaderName)
        {
            if (candidate == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(shaderAssetPath))
            {
                return string.Equals(
                    NormalizeAssetPath(AssetDatabase.GetAssetPath(candidate)),
                    shaderAssetPath,
                    StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(shaderName)
                && string.Equals(candidate.name, shaderName, StringComparison.Ordinal);
        }

        private static object GetImportSettings(string assetPath) => MaterialExportSpecBuilder.GetImportSettings(assetPath);

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
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
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
            var parseResult = GraphEnvelopeReader.ParseObjects(text);
            var envelope = adapter.BuildEnvelope(parseResult);
            var root = envelope.root;
            var objectMap = envelope.objectMap;

            var properties = root != null ? adapter.ResolveObjectList(root["m_Properties"], objectMap) : new List<JObject>();
            var keywords = root != null ? adapter.ResolveObjectList(root["m_Keywords"], objectMap) : new List<JObject>();
            var categories = root != null ? adapter.ResolveObjectList(root["m_CategoryData"], objectMap) : new List<JObject>();
            var groups = root != null ? adapter.ResolveObjectList(root["m_GroupDatas"], objectMap) : new List<JObject>();
            var stickyNotes = root != null ? adapter.ResolveObjectList(root["m_StickyNoteDatas"], objectMap) : new List<JObject>();
            var nodes = root != null ? adapter.ResolveObjectList(root["m_Nodes"], objectMap) : new List<JObject>();
            var edges = root != null ? adapter.ResolveObjectList(root["m_Edges"], objectMap) : new List<JObject>();
            var propertyIds = properties.Select(property => property.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var keywordIds = keywords.Select(keyword => keyword.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var nodeIds = nodes.Select(node => node.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var nodesById = nodes
                .Where(node => !string.IsNullOrWhiteSpace(node.Value<string>("m_ObjectId")))
                .GroupBy(node => node.Value<string>("m_ObjectId"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
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

            var nodeExports = new JArray(nodes.Select(node =>
            {
                var export = new JObject
                {
                    ["objectId"] = node.Value<string>("m_ObjectId"),
                    ["type"] = node.Value<string>("m_Type"),
                    ["displayName"] = ToJToken(adapter.FirstString(node, "m_Name", "m_DisplayName")),
                    ["position"] = BuildPositionObject(node["m_DrawState"]?["m_Position"] as JObject),
                    ["groupId"] = ToJToken(node["m_Group"]?["m_Id"]?.Value<string>()),
                    ["categoryIds"] = categoryMembership.TryGetValue(node.Value<string>("m_ObjectId"), out var memberships) ? new JArray(memberships) : new JArray(),
                    ["subGraphGuid"] = ToJToken(adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"])),
                    ["subGraphPath"] = ToJToken(ResolveSubGraphPath(adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"]), guidMap)),
                    ["slots"] = adapter.ResolveNodeSlots(node, objectMap).Count,
                    ["inputSlots"] = BuildNodeSlotExports(node, objectMap, adapter, "input"),
                    ["outputSlots"] = BuildNodeSlotExports(node, objectMap, adapter, "output")
                };

                var customFunction = BuildCustomFunctionExport(node, guidMap);
                if (customFunction != null)
                {
                    export["customFunction"] = customFunction;
                }

                return export;
            }));

            var edgeExports = new JArray(edges.Select(edge =>
            {
                var outputNodeId = edge["m_OutputSlot"]?["m_Node"]?["m_Id"]?.Value<string>()
                                   ?? edge["m_OutputSlot"]?.Value<string>("m_NodeGUIDSerialized");
                var inputNodeId = edge["m_InputSlot"]?["m_Node"]?["m_Id"]?.Value<string>()
                                  ?? edge["m_InputSlot"]?.Value<string>("m_NodeGUIDSerialized");
                var outputNode = ResolveNode(nodesById, outputNodeId);
                var inputNode = ResolveNode(nodesById, inputNodeId);
                var outputSlotId = edge["m_OutputSlot"]?.Value<int?>("m_SlotId");
                var inputSlotId = edge["m_InputSlot"]?.Value<int?>("m_SlotId");

                return new JObject
                {
                    ["objectId"] = ToJToken(edge.Value<string>("m_ObjectId")),
                    ["outputNodeId"] = ToJToken(outputNodeId),
                    ["outputSlotId"] = ToJToken(outputSlotId),
                    ["outputSlotName"] = ToJToken(ResolveNodeSlotDisplayName(outputNode, outputSlotId, objectMap, adapter)),
                    ["inputNodeId"] = ToJToken(inputNodeId),
                    ["inputSlotId"] = ToJToken(inputSlotId),
                    ["inputSlotName"] = ToJToken(ResolveNodeSlotDisplayName(inputNode, inputSlotId, objectMap, adapter))
                };
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

            var targetExports = new JArray((root != null ? adapter.ResolveObjectList(root["m_ActiveTargets"], objectMap) : new List<JObject>()).Select(target => new JObject
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
                    ["rootType"] = ToJToken(root?.Value<string>("m_Type")),
                    ["parseError"] = ToJToken(envelope.parseError),
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
                        ["outputNode"] = BuildOutputNode(root?["m_OutputNode"] as JObject, objectMap, adapter),
                        ["vertexBlocks"] = BuildGraphBlocks(root?["m_VertexContext"]?["m_Blocks"], objectMap, adapter),
                        ["fragmentBlocks"] = BuildGraphBlocks(root?["m_FragmentContext"]?["m_Blocks"], objectMap, adapter)
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
                ["uv"] = ToJToken(uv)
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
                ["textureId"] = ToJToken(GetTextureReference(textureProperty) != null ? GetTextureExportId(semantic) : null),
                ["rawPropertyNames"] = new JArray(rawPropertyNames),
                ["parameters"] = ToJToken(parameters)
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

        private static float? GetColorAlphaValue(MaterialPropertyDto property)
        {
            return ToNullableFloat(ReadOptionalMember(property?.value, "a"));
        }

        private static bool HasKeyword(IEnumerable<string> shaderKeywords, string keyword)
        {
            return shaderKeywords != null && shaderKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase);
        }

        private static string InferPipeline(Shader shader, string shaderPath = null)
        {
            if (shader == null)
            {
                return null;
            }

            var normalizedShaderPath = NormalizeAssetPath(shaderPath ?? AssetDatabase.GetAssetPath(shader));
            var shaderName = shader.name ?? string.Empty;

            if (shaderName.IndexOf("Universal Render Pipeline", StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("Hidden/Universal Render Pipeline", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "URP";
            }

            if (shaderName.IndexOf("High Definition Render Pipeline", StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("Hidden/HDRP", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "HDRP";
            }

            if (IsShaderGraphAssetPath(normalizedShaderPath))
            {
                var shaderGraphPipeline = InferShaderGraphPipeline(normalizedShaderPath);
                if (!string.IsNullOrWhiteSpace(shaderGraphPipeline))
                {
                    return shaderGraphPipeline;
                }

                return "ShaderGraphOrUnknown";
            }

            var absoluteShaderPath = !string.IsNullOrWhiteSpace(normalizedShaderPath)
                ? ToAbsoluteProjectPath(normalizedShaderPath)
                : null;
            if (!string.IsNullOrWhiteSpace(absoluteShaderPath) && File.Exists(absoluteShaderPath))
            {
                var sourceText = File.ReadAllText(absoluteShaderPath);
                if (sourceText.IndexOf("com.unity.render-pipelines.universal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "URP";
                }

                if (sourceText.IndexOf("com.unity.render-pipelines.high-definition", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "HDRP";
                }
            }

            return "BuiltInOrUnknown";
        }

        private static string InferShaderGraphPipeline(string shaderGraphPath)
        {
            try
            {
                var readResult = CompatServices.ShaderGraph.Read(shaderGraphPath);
                foreach (var target in readResult.graph?.targets ?? new List<ShaderGraphTargetDto>())
                {
                    if (ContainsPipelineMarker(target.type, target.displayName, "Universal", "URP"))
                    {
                        return "URP";
                    }

                    if (ContainsPipelineMarker(target.type, target.displayName, "High Definition", "HDRP", "HDTarget"))
                    {
                        return "HDRP";
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ContainsPipelineMarker(string primary, string secondary, params string[] markers)
        {
            foreach (var marker in markers)
            {
                if ((!string.IsNullOrWhiteSpace(primary) && primary.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrWhiteSpace(secondary) && secondary.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }

            return false;
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
                ["r"] = ToJToken(r),
                ["g"] = ToJToken(g),
                ["b"] = ToJToken(b),
                ["a"] = ToJToken(a)
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

        private static JObject BuildCustomFunctionExport(JObject node, IReadOnlyDictionary<string, string> guidMap)
        {
            if (node == null)
            {
                return null;
            }

            var typeName = node.Value<string>("m_Type");
            if (string.IsNullOrWhiteSpace(typeName)
                || typeName.IndexOf("CustomFunctionNode", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var sourceType = node.Value<int?>("m_SourceType");
            var sourceGuid = node.Value<string>("m_FunctionSource");
            return new JObject
            {
                ["sourceType"] = ToJToken(sourceType),
                ["sourceTypeName"] = ToJToken(ResolveCustomFunctionSourceTypeName(sourceType)),
                ["functionName"] = ToJToken(node.Value<string>("m_FunctionName")),
                ["functionSourceGuid"] = ToJToken(sourceGuid),
                ["functionSourcePath"] = ToJToken(ResolveSubGraphPath(sourceGuid, guidMap)),
                ["functionBody"] = ToJToken(node.Value<string>("m_FunctionBody"))
            };
        }

        private static string ResolveCustomFunctionSourceTypeName(int? sourceType)
        {
            switch (sourceType)
            {
                case 0:
                    return "file";
                case 1:
                    return "string";
                default:
                    return sourceType.HasValue ? $"unknown_{sourceType.Value}" : null;
            }
        }

        private static JObject BuildWorldDissolveRuntimeExport(Material material, string materialAssetPath)
        {
            var worldDissolveType = ResolveTypeByName("INab.WorldDissolve.WorldDissolve");
            if (worldDissolveType == null)
            {
                return new JObject
                {
                    ["available"] = false,
                    ["reason"] = "world_dissolve_type_not_loaded",
                    ["instances"] = new JArray()
                };
            }

            var instances = new JArray();
            foreach (var obj in Resources.FindObjectsOfTypeAll(worldDissolveType))
            {
                if (!(obj is Component component) || !component.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (!ComponentUsesMaterial(component, material, materialAssetPath))
                {
                    continue;
                }

                instances.Add(BuildWorldDissolveInstanceExport(component, materialAssetPath));
            }

            return new JObject
            {
                ["available"] = true,
                ["instanceCount"] = instances.Count,
                ["instances"] = instances
            };
        }

        private static JObject BuildWorldDissolveInstanceExport(Component component, string materialAssetPath)
        {
            var componentType = component.GetType();
            var baseType = componentType.BaseType;
            var gameObject = component.gameObject;

            return new JObject
            {
                ["componentType"] = componentType.FullName,
                ["gameObjectName"] = gameObject.name,
                ["scenePath"] = BuildGameObjectScenePath(gameObject.transform),
                ["sceneName"] = gameObject.scene.name,
                ["enabled"] = component is Behaviour behaviour ? behaviour.enabled : true,
                ["activeInHierarchy"] = gameObject.activeInHierarchy,
                ["shaderType"] = ToJToken(ReadFieldOrPropertyAsString(component, componentType, "propertiesType")),
                ["globalPropertiesId"] = ToJToken(ReadFieldOrPropertyAsString(component, componentType, "globalPropertiesId")),
                ["activeMasks"] = ToJToken(ReadFieldValue<int?>(component, baseType, "activeMasks")),
                ["maskType"] = ToJToken(ReadFieldOrPropertyAsString(component, baseType, "type")),
                ["useDisplacement"] = ToJToken(ReadFieldValue<bool?>(component, baseType, "useDisplacement")),
                ["materialMatches"] = BuildReferencedMaterialsExport(component, baseType, materialAssetPath),
                ["sdfVectors"] = new JObject
                {
                    ["positions"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "positions")),
                    ["scales"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "scales")),
                    ["ups"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "upVectors")),
                    ["rights"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "rightVectors")),
                    ["forwards"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "forwardVectors")),
                    ["positions2"] = SerializeVector4Array(ReadFieldValue<Vector4[]>(component, componentType, "positions_2"))
                },
                ["masks"] = BuildMasksExport(ReadFieldValue<System.Collections.IEnumerable>(component, componentType, "masksList"))
            };
        }

        private static JArray BuildReferencedMaterialsExport(Component component, Type baseType, string materialAssetPath)
        {
            var materials = ReadFieldValue<System.Collections.IEnumerable>(component, baseType, "materialsList");
            var exports = new JArray();
            if (materials == null)
            {
                return exports;
            }

            foreach (var entry in materials)
            {
                if (!(entry is Material material))
                {
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(material);
                exports.Add(new JObject
                {
                    ["name"] = material.name,
                    ["path"] = assetPath,
                    ["matchesExportedMaterial"] = string.Equals(assetPath, materialAssetPath, StringComparison.OrdinalIgnoreCase)
                });
            }

            return exports;
        }

        private static JArray BuildMasksExport(System.Collections.IEnumerable masks)
        {
            var exports = new JArray();
            if (masks == null)
            {
                return exports;
            }

            foreach (var entry in masks)
            {
                if (!(entry is Component maskComponent))
                {
                    continue;
                }

                var transform = maskComponent.transform;
                exports.Add(new JObject
                {
                    ["componentType"] = maskComponent.GetType().FullName,
                    ["name"] = maskComponent.gameObject.name,
                    ["scenePath"] = BuildGameObjectScenePath(transform),
                    ["position"] = SerializeVector3(transform.position),
                    ["rotationEuler"] = SerializeVector3(transform.rotation.eulerAngles),
                    ["lossyScale"] = SerializeVector3(transform.lossyScale),
                    ["type"] = ToJToken(ReadFieldOrPropertyAsString(maskComponent, maskComponent.GetType(), "type")),
                    ["colliderScale"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "colliderScale")),
                    ["angle"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "angle")),
                    ["angleAdjust"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "angleAdjust")),
                    ["radiusAdjust"] = ToJToken(ReadFieldValue<float?>(maskComponent, maskComponent.GetType(), "radiusAdjust"))
                });
            }

            return exports;
        }

        private static bool ComponentUsesMaterial(Component component, Material targetMaterial, string targetMaterialAssetPath)
        {
            if (component == null || targetMaterial == null)
            {
                return false;
            }

            var materials = ReadFieldValue<System.Collections.IEnumerable>(component, component.GetType().BaseType, "materialsList");
            if (materials == null)
            {
                return false;
            }

            foreach (var entry in materials)
            {
                if (!(entry is Material material))
                {
                    continue;
                }

                if (material == targetMaterial)
                {
                    return true;
                }

                var materialAssetPath = AssetDatabase.GetAssetPath(material);
                if (!string.IsNullOrWhiteSpace(materialAssetPath)
                    && string.Equals(materialAssetPath, targetMaterialAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Type ResolveTypeByName(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static T ReadFieldValue<T>(object source, Type declaredType, string memberName)
        {
            if (source == null || declaredType == null || string.IsNullOrWhiteSpace(memberName))
            {
                return default(T);
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var current = declaredType; current != null; current = current.BaseType)
            {
                var field = current.GetField(memberName, Flags);
                if (field == null)
                {
                    continue;
                }

                var value = field.GetValue(source);
                if (value is T typed)
                {
                    return typed;
                }

                if (value == null)
                {
                    return default(T);
                }

                try
                {
                    return (T)value;
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }

        private static string ReadFieldOrPropertyAsString(object source, Type declaredType, string memberName)
        {
            if (source == null || declaredType == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var current = declaredType; current != null; current = current.BaseType)
            {
                var field = current.GetField(memberName, Flags);
                if (field != null)
                {
                    return field.GetValue(source)?.ToString();
                }

                var property = current.GetProperty(memberName, Flags);
                if (property != null && property.CanRead)
                {
                    try
                    {
                        return property.GetValue(source, null)?.ToString();
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static string BuildGameObjectScenePath(Transform transform)
        {
            if (transform == null)
            {
                return null;
            }

            var segments = new List<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                segments.Add(current.name);
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static JArray SerializeVector4Array(Vector4[] values)
        {
            return values == null
                ? new JArray()
                : new JArray(values.Select(value => new JObject
                {
                    ["x"] = value.x,
                    ["y"] = value.y,
                    ["z"] = value.z,
                    ["w"] = value.w
                }));
        }

        private static JObject SerializeVector3(Vector3 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z
            };
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

        private static JArray BuildNodeSlotExports(JObject node, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter, string directionFilter)
        {
            return new JArray(adapter.ResolveNodeSlots(node, objectMap)
                .Select(slot => BuildSlotExport(slot, adapter))
                .Where(slot => string.Equals(slot.Value<string>("direction"), directionFilter, StringComparison.OrdinalIgnoreCase)));
        }

        private static JObject BuildSlotExport(JObject slot, GraphSchemaAdapter adapter)
        {
            return new JObject
            {
                ["objectId"] = ToJToken(slot.Value<string>("m_ObjectId")),
                ["slotId"] = ToJToken(slot.Value<int?>("m_Id")),
                ["displayName"] = ToJToken(adapter.FirstString(slot, "m_DisplayName", "m_Name", "m_ShaderOutputName")),
                ["direction"] = ToJToken(adapter.ResolveSlotDirection(slot)),
                ["valueType"] = ToJToken(slot.Value<string>("m_Type")),
                ["slotType"] = ToJToken(slot.Value<int?>("m_SlotType")),
                ["shaderOutputName"] = ToJToken(slot.Value<string>("m_ShaderOutputName")),
                ["stageCapability"] = ToJToken(slot.Value<int?>("m_StageCapability")),
                ["hidden"] = ToJToken(slot.Value<bool?>("m_Hidden"))
            };
        }

        private static JObject ResolveNode(IReadOnlyDictionary<string, JObject> nodesById, string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return null;
            }

            return nodesById != null && nodesById.TryGetValue(nodeId, out var node) ? node : null;
        }

        private static string ResolveNodeSlotDisplayName(JObject node, int? slotId, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter)
        {
            var slot = adapter.FindNodeSlotById(node, slotId, objectMap);
            return adapter.FirstString(slot, "m_DisplayName", "m_Name", "m_ShaderOutputName");
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
                layer = LayerMask.LayerToName(light.gameObject.layer),
                tag = light.gameObject.tag,
                type = light.type.ToString(),
                intensity = light.intensity,
                intensityUnit = ReadOptionalMember(light, "lightUnit")?.ToString(),
                range = light.range,
                spotAngle = light.spotAngle,
                innerSpotAngle = ReadOptionalMember(light, "innerSpotAngle"),
                color = ToColorObject(light.color),
                colorTemperature = light.colorTemperature,
                useColorTemperature = light.useColorTemperature,
                bounceIntensity = light.bounceIntensity,
                shadowStrength = light.shadowStrength,
                cookieSize = light.cookieSize,
                cullingMask = light.cullingMask,
                shadows = light.shadows.ToString()
            };
        }

        private static object DescribeReflectionProbe(ReflectionProbe probe)
        {
            return new
            {
                name = probe.name,
                gameObjectPath = GetTransformPath(probe.transform),
                layer = LayerMask.LayerToName(probe.gameObject.layer),
                tag = probe.gameObject.tag,
                mode = probe.mode.ToString(),
                importance = probe.importance,
                boxProjection = probe.boxProjection,
                size = ToVector3Object(probe.size),
                center = ToVector3Object(probe.center),
                nearClipPlane = probe.nearClipPlane,
                farClipPlane = probe.farClipPlane,
                resolution = probe.resolution,
                intensity = probe.intensity,
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
                layer = LayerMask.LayerToName(volume.gameObject.layer),
                tag = volume.gameObject.tag,
                type = type.FullName,
                enabled = volume is Behaviour behaviour ? behaviour.enabled : true,
                isGlobal = ReadOptionalMember(volume, "isGlobal"),
                priority = ReadOptionalMember(volume, "priority"),
                weight = ReadOptionalMember(volume, "weight"),
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

        private static object DescribeRenderPipelineAsset(RenderPipelineAsset pipelineAsset)
        {
            if (pipelineAsset == null)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(pipelineAsset);
            return new
            {
                name = pipelineAsset.name,
                path,
                guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path),
                type = pipelineAsset.GetType().FullName
            };
        }

        private static RenderPipelineAsset ReadQualityRenderPipeline(int qualityLevel)
        {
            var method = typeof(QualitySettings).GetMethod("GetRenderPipelineAssetAt", BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                return null;
            }

            try
            {
                return method.Invoke(null, new object[] { qualityLevel }) as RenderPipelineAsset;
            }
            catch
            {
                return null;
            }
        }

        private static object DescribePrefabNode(Transform transform, string rootPath)
        {
            var renderer = transform.GetComponent<Renderer>();
            return new
            {
                name = transform.name,
                path = rootPath,
                localPosition = ToVector3Object(transform.localPosition),
                localRotationEuler = ToVector3Object(transform.localEulerAngles),
                localScale = ToVector3Object(transform.localScale),
                components = transform.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => new
                    {
                        type = component.GetType().FullName,
                        name = component.GetType().Name
                    })
                    .ToList(),
                materials = renderer != null
                    ? renderer.sharedMaterials.Where(material => material != null).Select(DescribeMaterialReference).ToList()
                    : new List<MaterialReferenceDto>(),
                children = transform.Cast<Transform>()
                    .Select(child => DescribePrefabNode(child, $"{rootPath}/{child.name}"))
                    .ToList()
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

        private static string ReadStaticPropertyValue(Type type, string memberName)
        {
            if (type == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            var property = type.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public);
            if (property == null || !property.CanRead)
            {
                return null;
            }

            try
            {
                return property.GetValue(null, null)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static float? ToNullableFloat(object value)
        {
            switch (value)
            {
                case float floatValue:
                    return floatValue;
                case double doubleValue:
                    return (float)doubleValue;
                case int intValue:
                    return intValue;
                case long longValue:
                    return longValue;
                default:
                    return null;
            }
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
