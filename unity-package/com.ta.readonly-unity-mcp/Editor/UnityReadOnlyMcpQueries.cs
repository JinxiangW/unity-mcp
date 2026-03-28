using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
                properties = GetMaterialProperties(material)
            };
        }

        public static object GetShaderInfo(string path, string guid, bool includeUsage)
        {
            var assetPath = ResolveAssetPath(path, guid);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            if (shader == null)
            {
                throw new InvalidOperationException($"Asset at '{assetPath}' is not a Shader.");
            }

            var passes = new List<object>();
            for (var passIndex = 0; passIndex < shader.passCount; passIndex++)
            {
                passes.Add(new
                {
                    index = passIndex,
                    name = shader.GetPassName(passIndex)
                });
            }

            var sourceInfo = GetShaderSourceInfo(assetPath);
            var usageMaterials = includeUsage ? FindMaterialsForShader(shader.name) : null;

            return new
            {
                asset = DescribeAssetReference(assetPath),
                name = shader.name,
                isSupported = shader.isSupported,
                maximumLOD = shader.maximumLOD,
                passCount = shader.passCount,
                passes,
                keywords = GetShaderKeywords(shader),
                properties = GetShaderPropertyDescriptions(shader),
                fallback = sourceInfo.fallback,
                customEditor = sourceInfo.customEditor,
                usage = usageMaterials != null ? new
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
                shader = shader != null ? DescribeShaderReference(shader) : new
                {
                    name = shaderName,
                    path = assetPath,
                    guid
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

            var absolutePath = ToAbsoluteProjectPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException("Shader Graph source file was not found.", absolutePath);
            }

            var text = File.ReadAllText(absolutePath);
            var parsed = UnityShaderGraphTextParser.Parse(assetPath, text);
            var dependencyPaths = AssetDatabase.GetDependencies(assetPath, false)
                .Where(candidate => candidate != assetPath)
                .Where(candidate => string.Equals(Path.GetExtension(candidate), ".shadersubgraph", StringComparison.OrdinalIgnoreCase))
                .Select(DescribeAssetReference)
                .ToList();

            return new
            {
                asset = DescribeAssetReference(assetPath),
                sourceLength = text.Length,
                subGraphDependencies = dependencyPaths,
                graph = parsed
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

        private static List<object> FindMaterialsForShader(string shaderName)
        {
            var materials = AssetDatabase.FindAssets("t:Material")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<Material>(path))
                .Where(material => material != null && material.shader != null && material.shader.name == shaderName)
                .Select(DescribeMaterialReference)
                .Cast<object>()
                .ToList();

            return materials;
        }

        private static List<object> GetMaterialProperties(Material material)
        {
            if (material.shader == null)
            {
                return new List<object>();
            }

            var properties = new List<object>();
            var propertyCount = ShaderUtil.GetPropertyCount(material.shader);

            for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                var propertyName = ShaderUtil.GetPropertyName(material.shader, propertyIndex);
                var propertyType = ShaderUtil.GetPropertyType(material.shader, propertyIndex);
                object value = null;

                switch (propertyType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        value = ToColorObject(material.GetColor(propertyName));
                        break;

                    case ShaderUtil.ShaderPropertyType.Vector:
                        value = ToVectorObject(material.GetVector(propertyName));
                        break;

                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        value = material.GetFloat(propertyName);
                        break;

                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        var texture = material.GetTexture(propertyName);
                        value = new
                        {
                            texture = texture != null ? DescribeTextureReference(texture) : null,
                            scale = ToVector2Object(material.GetTextureScale(propertyName)),
                            offset = ToVector2Object(material.GetTextureOffset(propertyName))
                        };
                        break;
                }

                properties.Add(new
                {
                    name = propertyName,
                    description = ShaderUtil.GetPropertyDescription(material.shader, propertyIndex),
                    type = propertyType.ToString(),
                    value
                });
            }

            return properties;
        }

        private static List<object> GetShaderPropertyDescriptions(Shader shader)
        {
            var properties = new List<object>();
            var propertyCount = ShaderUtil.GetPropertyCount(shader);

            for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                var propertyType = ShaderUtil.GetPropertyType(shader, propertyIndex);
                float? rangeMin = null;
                float? rangeMax = null;

                if (propertyType == ShaderUtil.ShaderPropertyType.Range)
                {
                    try
                    {
                        rangeMin = ShaderUtil.GetRangeLimits(shader, propertyIndex, 1);
                        rangeMax = ShaderUtil.GetRangeLimits(shader, propertyIndex, 2);
                    }
                    catch
                    {
                    }
                }

                properties.Add(new
                {
                    name = ShaderUtil.GetPropertyName(shader, propertyIndex),
                    description = ShaderUtil.GetPropertyDescription(shader, propertyIndex),
                    type = propertyType.ToString(),
                    flags = ShaderUtil.GetPropertyFlags(shader, propertyIndex).ToString(),
                    rangeMin,
                    rangeMax,
                    attributes = ShaderUtil.GetPropertyAttributes(shader, propertyIndex)
                });
            }

            return properties;
        }

        private static List<object> GetShaderKeywords(Shader shader)
        {
            var keywords = new List<object>();
            foreach (var keyword in shader.keywordSpace.keywords)
            {
                keywords.Add(new
                {
                    name = keyword.name,
                    isOverridable = keyword.isOverridable,
                    isDynamic = ReadOptionalMember(keyword, "isDynamic")
                });
            }

            return keywords;
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

        private static object DescribeAssetReference(string assetPath)
        {
            var normalizedPath = NormalizeAssetPath(assetPath);
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(normalizedPath);
            var assetType = AssetDatabase.GetMainAssetTypeAtPath(normalizedPath);

            return new
            {
                name = mainAsset != null ? mainAsset.name : Path.GetFileNameWithoutExtension(normalizedPath),
                path = normalizedPath,
                guid = AssetDatabase.AssetPathToGUID(normalizedPath),
                type = assetType != null ? assetType.FullName : null
            };
        }

        private static object DescribeShaderReference(Shader shader)
        {
            var path = AssetDatabase.GetAssetPath(shader);
            return new
            {
                name = shader.name,
                path,
                guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path)
            };
        }

        private static object DescribeMaterialReference(Material material)
        {
            var path = AssetDatabase.GetAssetPath(material);
            return new
            {
                name = material.name,
                path,
                guid = string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.AssetPathToGUID(path),
                shader = material.shader != null ? DescribeShaderReference(material.shader) : null
            };
        }

        private static object DescribeTextureReference(Texture texture)
        {
            var path = AssetDatabase.GetAssetPath(texture);
            return new
            {
                name = texture.name,
                path,
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

        private static object DescribeUnityObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(value);
            return new
            {
                name = value.name,
                path,
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

        private static string ToAbsoluteProjectPath(string assetPath)
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

        private static object ToColorObject(Color color)
        {
            return new
            {
                r = color.r,
                g = color.g,
                b = color.b,
                a = color.a
            };
        }

        private static object ToVectorObject(Vector4 value)
        {
            return new
            {
                x = value.x,
                y = value.y,
                z = value.z,
                w = value.w
            };
        }

        private static object ToVector2Object(Vector2 value)
        {
            return new
            {
                x = value.x,
                y = value.y
            };
        }

        private static object ToVector3Object(Vector3 value)
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

        private static (string fallback, string customEditor) GetShaderSourceInfo(string assetPath)
        {
            var absolutePath = ToAbsoluteProjectPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                return (null, null);
            }

            var source = File.ReadAllText(absolutePath);
            var fallbackMatch = Regex.Match(source, "\\bFallback\\s+\"?(?<value>[^\"\\r\\n]+)\"?", RegexOptions.IgnoreCase);
            var customEditorMatch = Regex.Match(source, "\\bCustomEditor\\s+\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);

            var fallback = fallbackMatch.Success ? fallbackMatch.Groups["value"].Value.Trim() : null;
            var customEditor = customEditorMatch.Success ? customEditorMatch.Groups["value"].Value.Trim() : null;
            return (fallback, customEditor);
        }
    }
}
