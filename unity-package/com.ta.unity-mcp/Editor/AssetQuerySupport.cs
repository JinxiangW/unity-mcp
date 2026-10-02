using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace TA.UnityMcp
{
    internal static class AssetQuerySupport
    {
        internal static List<object> GetDependencyReferences(string assetPath, bool recursive)
        {
            return AssetDatabase.GetDependencies(assetPath, recursive)
                .Where(candidate => candidate != assetPath)
                .Select(UnityMcpQueries.DescribeAssetReference)
                .Cast<object>()
                .ToList();
        }

        internal static List<object> FindAssetsReferencing(string assetPath)
        {
            var results = new List<object>();
            foreach (var candidatePath in AssetDatabase.GetAllAssetPaths())
            {
                if (candidatePath == assetPath || !IsSearchableAssetPath(candidatePath))
                {
                    continue;
                }

                var dependencies = AssetDatabase.GetDependencies(candidatePath, false);
                if (dependencies.Contains(assetPath))
                {
                    results.Add(UnityMcpQueries.DescribeAssetReference(candidatePath));
                }
            }

            return results;
        }

        private static bool IsSearchableAssetPath(string assetPath)
        {
            return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
