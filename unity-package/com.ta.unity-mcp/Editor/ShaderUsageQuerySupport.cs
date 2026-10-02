using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TA.UnityMcp.Contracts;
using UnityEditor;
using UnityEngine;

namespace TA.UnityMcp
{
    internal static class ShaderUsageQuerySupport
    {
        internal static List<object> FindLoadedRenderersUsingShader(string shaderAssetPath, string shaderName)
        {
            return SceneQuerySupport.GetLoadedScenes()
                .SelectMany(scene => scene.GetRootGameObjects())
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer.sharedMaterials.Any(material => ShaderMatches(material?.shader, shaderAssetPath, shaderName)))
                .Select(renderer => new
                {
                    scene = renderer.gameObject.scene.path,
                    renderer = renderer.name,
                    gameObjectPath = SceneQuerySupport.GetTransformPath(renderer.transform),
                    rendererType = renderer.GetType().FullName,
                    materials = renderer.sharedMaterials
                        .Where(material => ShaderMatches(material?.shader, shaderAssetPath, shaderName))
                        .Select(UnityMcpQueries.DescribeMaterialReference)
                        .ToList()
                })
                .Cast<object>()
                .ToList();
        }

        internal static List<MaterialReferenceDto> FindMaterialsForShader(string shaderAssetPath, string shaderName)
        {
            return AssetDatabase.GetAllAssetPaths()
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
                    shader = UnityMcpQueries.DescribeShaderReference(entry.material.shader)
                })
                .ToList();
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

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/');
        }
    }
}
