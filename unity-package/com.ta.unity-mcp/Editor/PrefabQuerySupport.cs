using System.Linq;
using TA.UnityMcp.Contracts;
using UnityEngine;

namespace TA.UnityMcp
{
    internal static class PrefabQuerySupport
    {
        internal static object DescribePrefabNode(Transform transform, string rootPath)
        {
            var renderer = transform.GetComponent<Renderer>();
            return new
            {
                name = transform.name,
                path = rootPath,
                localPosition = UnityMcpQueries.ToVector3Object(transform.localPosition),
                localRotationEuler = UnityMcpQueries.ToVector3Object(transform.localEulerAngles),
                localScale = UnityMcpQueries.ToVector3Object(transform.localScale),
                components = transform.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => new
                    {
                        type = component.GetType().FullName,
                        name = component.GetType().Name
                    })
                    .ToList(),
                materials = renderer != null
                    ? renderer.sharedMaterials.Where(material => material != null).Select(UnityMcpQueries.DescribeMaterialReference).ToList()
                    : new System.Collections.Generic.List<MaterialReferenceDto>(),
                children = transform.Cast<Transform>()
                    .Select(child => DescribePrefabNode(child, $"{rootPath}/{child.name}"))
                    .ToList()
            };
        }
    }
}
