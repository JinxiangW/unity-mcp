using System;
using System.Collections.Generic;
using TA.UnityMcp.Contracts;
using UnityEngine;

namespace TA.UnityMcp.Compat.Shader
{
    internal sealed class LegacyShaderIntrospector : IShaderIntrospector
    {
        public List<MaterialPropertyDto> ReadMaterialProperties(Material material)
        {
            var shader = material.shader;
            if (shader == null)
            {
                return new List<MaterialPropertyDto>();
            }

            var propertyCount = ShaderReflectionCompat.GetLegacyPropertyCount(shader);
            var properties = new List<MaterialPropertyDto>(propertyCount);

            for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                var propertyName = ShaderReflectionCompat.GetLegacyPropertyName(shader, propertyIndex);
                var propertyType = ShaderReflectionCompat.NormalizePropertyTypeName(ShaderReflectionCompat.GetLegacyPropertyType(shader, propertyIndex));

                properties.Add(new MaterialPropertyDto
                {
                    name = propertyName,
                    description = ShaderReflectionCompat.GetLegacyPropertyDescription(shader, propertyIndex),
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
            var propertyCount = ShaderReflectionCompat.GetLegacyPropertyCount(shader);
            var properties = new List<ShaderPropertyDto>(propertyCount);

            for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                var propertyType = ShaderReflectionCompat.NormalizePropertyTypeName(ShaderReflectionCompat.GetLegacyPropertyType(shader, propertyIndex));
                float? rangeMin = null;
                float? rangeMax = null;

                if (string.Equals(propertyType, "Range", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        (rangeMin, rangeMax) = ShaderReflectionCompat.GetLegacyRangeLimits(shader, propertyIndex);
                    }
                    catch
                    {
                    }
                }

                properties.Add(new ShaderPropertyDto
                {
                    name = ShaderReflectionCompat.GetLegacyPropertyName(shader, propertyIndex),
                    description = ShaderReflectionCompat.GetLegacyPropertyDescription(shader, propertyIndex),
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
                    return UnityMcpQueries.ToColorObject(material.GetColor(propertyName));

                case "Vector":
                    return UnityMcpQueries.ToVector4Object(material.GetVector(propertyName));

                case "Float":
                case "Range":
                    return material.GetFloat(propertyName);

                case "Texture":
                    var texture = material.GetTexture(propertyName);
                    return new
                    {
                        texture = texture != null ? UnityMcpQueries.DescribeTextureReference(texture) : null,
                        scale = UnityMcpQueries.ToVector2Object(material.GetTextureScale(propertyName)),
                        offset = UnityMcpQueries.ToVector2Object(material.GetTextureOffset(propertyName))
                    };

                default:
                    return null;
            }
        }
    }
}
