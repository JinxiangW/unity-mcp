using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TA.UnityMcp.Compat;
using TA.UnityMcp.Compat.Shader;
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
                dependencies = includeDependencies ? AssetQuerySupport.GetDependencyReferences(assetPath, false) : null,
                referencedBy = includeReferencedBy ? AssetQuerySupport.FindAssetsReferencing(assetPath) : null,
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
                dependencies = AssetQuerySupport.GetDependencyReferences(assetPath, recursive),
                referencedBy = includeReferencedBy ? AssetQuerySupport.FindAssetsReferencing(assetPath) : null
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
            var usageMaterials = includeUsage ? ShaderUsageQuerySupport.FindMaterialsForShader(assetPath, shader.name) : null;

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
            var materials = ShaderUsageQuerySupport.FindMaterialsForShader(normalizedShaderPath, shaderName);
            var renderers = includeRenderers ? ShaderUsageQuerySupport.FindLoadedRenderersUsingShader(normalizedShaderPath, shaderName) : new List<object>();

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
            var scenes = SceneQuerySupport.GetLoadedScenes();
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
                scenes = scenes.Select(SceneQuerySupport.DescribeSceneSummary).ToList()
            };
        }

        public static object GetSceneLights(string scenePath, string layers, string tag)
        {
            var layerFilters = SceneQuerySupport.ParseCsvValues(layers);
            var scenes = SceneQuerySupport.GetFilteredScenes(scenePath);

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
                        .Count(light => SceneQuerySupport.MatchesSceneObjectFilter(light.gameObject, layerFilters, tag)),
                    lights = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                        .Where(light => SceneQuerySupport.MatchesSceneObjectFilter(light.gameObject, layerFilters, tag))
                        .Select(SceneQuerySupport.DescribeLight)
                        .ToList()
                }).ToList()
            };
        }

        public static object GetSceneVolumes(string scenePath, string layers, string tag)
        {
            var layerFilters = SceneQuerySupport.ParseCsvValues(layers);
            var scenes = SceneQuerySupport.GetFilteredScenes(scenePath);

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
                        .SelectMany(SceneQuerySupport.GetVolumeComponents)
                        .Count(volume => SceneQuerySupport.MatchesSceneObjectFilter(volume.gameObject, layerFilters, tag)),
                    volumes = scene.GetRootGameObjects()
                        .SelectMany(SceneQuerySupport.GetVolumeComponents)
                        .Where(volume => SceneQuerySupport.MatchesSceneObjectFilter(volume.gameObject, layerFilters, tag))
                        .Select(SceneQuerySupport.DescribeVolume)
                        .ToList()
                }).ToList()
            };
        }

        public static object GetSceneRenderers(string scenePath)
        {
            var scenes = SceneQuerySupport.GetFilteredScenes(scenePath);

            return new
            {
                sceneCount = scenes.Count,
                scenes = scenes.Select(scene => new
                {
                    name = scene.name,
                    path = scene.path,
                    renderers = SceneQuerySupport.GetSceneRendererEntries(scene)
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
                hierarchy = PrefabQuerySupport.DescribePrefabNode(prefab.transform, prefab.name),
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
                referencedBy = AssetQuerySupport.FindAssetsReferencing(assetPath)
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

    }
}
