using System;
using System.Collections.Generic;
using TA.ReadOnlyUnityMcp.Contracts;
using UnityEngine;

namespace TA.ReadOnlyUnityMcp.Compat.Shader
{
    internal sealed class ModernShaderIntrospector : IShaderIntrospector
    {
        public List<MaterialPropertyDto> ReadMaterialProperties(Material material)
        {
            var shader = material.shader;
            if (shader == null || ShaderReflectionCompat.GetPropertyCountMethod == null)
            {
                return new List<MaterialPropertyDto>();
            }

            var propertyCount = (int)ShaderReflectionCompat.GetPropertyCountMethod.Invoke(shader, null);
            var properties = new List<MaterialPropertyDto>(propertyCount);

            for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                var propertyName = ShaderReflectionCompat.GetPropertyNameMethod?.Invoke(shader, new object[] { propertyIndex }) as string;
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                var rawPropertyType = ShaderReflectionCompat.GetPropertyTypeMethod?.Invoke(shader, new object[] { propertyIndex });
                var propertyType = ShaderReflectionCompat.NormalizePropertyTypeName(rawPropertyType);
                var description = ShaderReflectionCompat.GetPropertyDescriptionMethod?.Invoke(shader, new object[] { propertyIndex }) as string;

                properties.Add(new MaterialPropertyDto
                {
                    name = propertyName,
                    description = description,
                    type = propertyType,
                    value = ReadMaterialValue(material, propertyName, propertyType)
                });
            }

            return properties;
        }

        public List<ShaderPassDto> ReadPasses(UnityEngine.Shader shader)
        {
            return ShaderReflectionCompat.ReadPasses(shader);
        }

        public List<ShaderKeywordDto> ReadKeywords(UnityEngine.Shader shader)
        {
            return ShaderReflectionCompat.ReadKeywords(shader);
        }

        public List<ShaderPropertyDto> ReadProperties(UnityEngine.Shader shader)
        {
            if (ShaderReflectionCompat.GetPropertyCountMethod == null)
            {
                return new List<ShaderPropertyDto>();
            }

            var propertyCount = (int)ShaderReflectionCompat.GetPropertyCountMethod.Invoke(shader, null);
            var properties = new List<ShaderPropertyDto>(propertyCount);

            for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                var propertyName = ShaderReflectionCompat.GetPropertyNameMethod?.Invoke(shader, new object[] { propertyIndex }) as string;
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                var rawPropertyType = ShaderReflectionCompat.GetPropertyTypeMethod?.Invoke(shader, new object[] { propertyIndex });
                var propertyType = ShaderReflectionCompat.NormalizePropertyTypeName(rawPropertyType);
                float? rangeMin = null;
                float? rangeMax = null;

                if (string.Equals(propertyType, "Range", StringComparison.OrdinalIgnoreCase)
                    && ShaderReflectionCompat.GetPropertyRangeLimitsMethod != null)
                {
                    try
                    {
                        var limits = ShaderReflectionCompat.GetPropertyRangeLimitsMethod.Invoke(shader, new object[] { propertyIndex });
                        (rangeMin, rangeMax) = ShaderReflectionCompat.TryReadRangeLimitsFromResult(limits);
                    }
                    catch
                    {
                    }
                }

                properties.Add(new ShaderPropertyDto
                {
                    name = propertyName,
                    description = ShaderReflectionCompat.GetPropertyDescriptionMethod?.Invoke(shader, new object[] { propertyIndex }) as string,
                    type = propertyType,
                    flags = ShaderReflectionCompat.GetPropertyFlagsMethod?.Invoke(shader, new object[] { propertyIndex })?.ToString(),
                    rangeMin = rangeMin,
                    rangeMax = rangeMax,
                    attributes = ShaderReflectionCompat.GetPropertyAttributesMethod?.Invoke(shader, new object[] { propertyIndex })
                });
            }

            return properties;
        }

        private static object ReadMaterialValue(Material material, string propertyName, string propertyType)
        {
            switch (propertyType)
            {
                case "Color":
                    return UnityReadOnlyMcpQueries.ToColorObject(material.GetColor(propertyName));

                case "Vector":
                    return UnityReadOnlyMcpQueries.ToVector4Object(material.GetVector(propertyName));

                case "Float":
                case "Range":
                    return material.GetFloat(propertyName);

                case "Texture":
                    var texture = material.GetTexture(propertyName);
                    return new
                    {
                        texture = texture != null ? UnityReadOnlyMcpQueries.DescribeTextureReference(texture) : null,
                        scale = UnityReadOnlyMcpQueries.ToVector2Object(material.GetTextureScale(propertyName)),
                        offset = UnityReadOnlyMcpQueries.ToVector2Object(material.GetTextureOffset(propertyName))
                    };

                default:
                    return null;
            }
        }
    }
}
