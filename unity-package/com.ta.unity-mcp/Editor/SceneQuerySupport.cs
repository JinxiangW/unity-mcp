using System;
using System.Collections.Generic;
using System.Linq;
using TA.UnityMcp.Contracts;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TA.UnityMcp
{
    internal static class SceneQuerySupport
    {
        internal static List<Scene> GetLoadedScenes()
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

        internal static List<Scene> GetFilteredScenes(string scenePath)
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

        internal static List<string> ParseCsvValues(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(',')
                    .Select(entry => entry.Trim())
                    .Where(entry => !string.IsNullOrWhiteSpace(entry))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        internal static bool MatchesSceneObjectFilter(GameObject gameObject, IReadOnlyCollection<string> layerFilters, string tag)
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

        internal static object DescribeSceneSummary(Scene scene)
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

        internal static List<object> GetSceneRendererEntries(Scene scene)
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
                            material = material != null ? UnityMcpQueries.DescribeMaterialReference(material) : null
                        })
                        .ToList()
                })
                .Cast<object>()
                .ToList();
        }

        internal static IEnumerable<Component> GetVolumeComponents(GameObject root)
        {
            return root.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && component.GetType().FullName == "UnityEngine.Rendering.Volume");
        }

        internal static object DescribeLight(Light light)
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
                color = UnityMcpQueries.ToColorObject(light.color),
                colorTemperature = light.colorTemperature,
                useColorTemperature = light.useColorTemperature,
                bounceIntensity = light.bounceIntensity,
                shadowStrength = light.shadowStrength,
                cookieSize = light.cookieSize,
                cullingMask = light.cullingMask,
                shadows = light.shadows.ToString()
            };
        }

        internal static object DescribeReflectionProbe(ReflectionProbe probe)
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
                size = UnityMcpQueries.ToVector3Object(probe.size),
                center = UnityMcpQueries.ToVector3Object(probe.center),
                nearClipPlane = probe.nearClipPlane,
                farClipPlane = probe.farClipPlane,
                resolution = probe.resolution,
                intensity = probe.intensity,
                bounds = new
                {
                    center = UnityMcpQueries.ToVector3Object(probe.center),
                    size = UnityMcpQueries.ToVector3Object(probe.size)
                }
            };
        }

        internal static object DescribeVolume(Component volume)
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

        internal static string GetTransformPath(Transform transform)
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

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/');
        }
    }
}
