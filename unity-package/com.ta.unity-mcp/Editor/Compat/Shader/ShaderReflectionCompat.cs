using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TA.UnityMcp.Contracts;
using UnityEditor;
using UnityEngine;

namespace TA.UnityMcp.Compat.Shader
{
    internal static class ShaderReflectionCompat
    {
        public static readonly MethodInfo GetPropertyCountMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyCount");
        public static readonly MethodInfo GetPropertyNameMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyName", typeof(int));
        public static readonly MethodInfo GetPropertyTypeMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyType", typeof(int));
        public static readonly MethodInfo GetPropertyDescriptionMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyDescription", typeof(int));
        public static readonly MethodInfo GetPropertyFlagsMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyFlags", typeof(int));
        public static readonly MethodInfo GetPropertyRangeLimitsMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyRangeLimits", typeof(int));
        public static readonly MethodInfo GetPropertyAttributesMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyAttributes", typeof(int));
        public static readonly MethodInfo GetPassNameMethod = CapabilityProbe.GetInstanceMethod(typeof(UnityEngine.Shader), "GetPassName", typeof(int));
        public static readonly MethodInfo ShaderUtilGetPropertyCountMethod = typeof(ShaderUtil).GetMethod("GetPropertyCount", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(UnityEngine.Shader) }, null);
        public static readonly MethodInfo ShaderUtilGetPropertyNameMethod = typeof(ShaderUtil).GetMethod("GetPropertyName", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(UnityEngine.Shader), typeof(int) }, null);
        public static readonly MethodInfo ShaderUtilGetPropertyTypeMethod = typeof(ShaderUtil).GetMethod("GetPropertyType", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(UnityEngine.Shader), typeof(int) }, null);
        public static readonly MethodInfo ShaderUtilGetPropertyDescriptionMethod = typeof(ShaderUtil).GetMethod("GetPropertyDescription", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(UnityEngine.Shader), typeof(int) }, null);
        public static readonly MethodInfo ShaderUtilGetRangeLimitsMethod = typeof(ShaderUtil).GetMethod("GetRangeLimits", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(UnityEngine.Shader), typeof(int), typeof(int) }, null);
        public static readonly MethodInfo ShaderUtilGetPropertyFlagsMethod = typeof(ShaderUtil).GetMethod("GetShaderPropertyFlags", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(UnityEngine.Shader), typeof(string) }, null);
        public static readonly MethodInfo ShaderUtilGetPropertyAttributesMethod = typeof(ShaderUtil).GetMethod("GetShaderPropertyAttributes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(UnityEngine.Shader), typeof(string) }, null);

        private static readonly PropertyInfo PassCountProperty = typeof(UnityEngine.Shader).GetProperty("passCount", BindingFlags.Instance | BindingFlags.Public);
        private static readonly PropertyInfo KeywordSpaceProperty = typeof(UnityEngine.Shader).GetProperty("keywordSpace", BindingFlags.Instance | BindingFlags.Public);

        public static int GetPassCount(UnityEngine.Shader shader)
        {
            if (PassCountProperty == null)
            {
                return 0;
            }

            var raw = PassCountProperty.GetValue(shader, null);
            return raw is int value ? value : 0;
        }

        public static string GetPassName(UnityEngine.Shader shader, int passIndex)
        {
            if (GetPassNameMethod == null)
            {
                return null;
            }

            return GetPassNameMethod.Invoke(shader, new object[] { passIndex }) as string;
        }

        public static List<ShaderPassDto> ReadPasses(UnityEngine.Shader shader)
        {
            var passCount = GetPassCount(shader);
            var passes = new List<ShaderPassDto>(passCount);

            for (var passIndex = 0; passIndex < passCount; passIndex++)
            {
                passes.Add(new ShaderPassDto
                {
                    index = passIndex,
                    name = GetPassName(shader, passIndex)
                });
            }

            return passes;
        }

        public static List<ShaderKeywordDto> ReadKeywords(UnityEngine.Shader shader)
        {
            var results = new List<ShaderKeywordDto>();
            if (KeywordSpaceProperty == null)
            {
                return results;
            }

            var keywordSpace = KeywordSpaceProperty.GetValue(shader, null);
            if (keywordSpace == null)
            {
                return results;
            }

            var keywordsProperty = keywordSpace.GetType().GetProperty("keywords", BindingFlags.Instance | BindingFlags.Public);
            var rawKeywords = keywordsProperty?.GetValue(keywordSpace, null) as IEnumerable;
            if (rawKeywords == null)
            {
                return results;
            }

            foreach (var keyword in rawKeywords)
            {
                results.Add(new ShaderKeywordDto
                {
                    name = ReadOptionalMember(keyword, "name") as string,
                    isOverridable = ToBool(ReadOptionalMember(keyword, "isOverridable")),
                    isDynamic = ReadOptionalMember(keyword, "isDynamic")
                });
            }

            return results;
        }

        public static int GetLegacyPropertyCount(UnityEngine.Shader shader)
        {
            if (ShaderUtilGetPropertyCountMethod == null)
            {
                return 0;
            }

            var raw = ShaderUtilGetPropertyCountMethod.Invoke(null, new object[] { shader });
            return raw is int value ? value : 0;
        }

        public static string GetLegacyPropertyName(UnityEngine.Shader shader, int propertyIndex)
        {
            return ShaderUtilGetPropertyNameMethod?.Invoke(null, new object[] { shader, propertyIndex }) as string;
        }

        public static object GetLegacyPropertyType(UnityEngine.Shader shader, int propertyIndex)
        {
            return ShaderUtilGetPropertyTypeMethod?.Invoke(null, new object[] { shader, propertyIndex });
        }

        public static string GetLegacyPropertyDescription(UnityEngine.Shader shader, int propertyIndex)
        {
            return ShaderUtilGetPropertyDescriptionMethod?.Invoke(null, new object[] { shader, propertyIndex }) as string;
        }

        public static object GetLegacyPropertyFlags(UnityEngine.Shader shader, string propertyName)
        {
            return ShaderUtilGetPropertyFlagsMethod?.Invoke(null, new object[] { shader, propertyName });
        }

        public static object GetLegacyPropertyAttributes(UnityEngine.Shader shader, string propertyName)
        {
            return ShaderUtilGetPropertyAttributesMethod?.Invoke(null, new object[] { shader, propertyName });
        }

        public static (float? rangeMin, float? rangeMax) GetLegacyRangeLimits(UnityEngine.Shader shader, int propertyIndex)
        {
            if (ShaderUtilGetRangeLimitsMethod == null)
            {
                return (null, null);
            }

            var min = ShaderUtilGetRangeLimitsMethod.Invoke(null, new object[] { shader, propertyIndex, 1 });
            var max = ShaderUtilGetRangeLimitsMethod.Invoke(null, new object[] { shader, propertyIndex, 2 });
            return (ToNullableFloat(min), ToNullableFloat(max));
        }

        public static object ReadOptionalMember(object instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            var type = instance.GetType();
            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
            {
                return property.GetValue(instance, null);
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            return field?.GetValue(instance);
        }

        public static string NormalizePropertyTypeName(object rawPropertyType)
        {
            var typeName = rawPropertyType?.ToString();
            if (string.Equals(typeName, "TexEnv", StringComparison.OrdinalIgnoreCase))
            {
                return "Texture";
            }

            return typeName;
        }

        public static (float? rangeMin, float? rangeMax) TryReadRangeLimitsFromResult(object limits)
        {
            if (limits == null)
            {
                return (null, null);
            }

            if (limits is Vector2 vector2)
            {
                return (vector2.x, vector2.y);
            }

            var x = ReadOptionalMember(limits, "x");
            var y = ReadOptionalMember(limits, "y");
            return (ToNullableFloat(x), ToNullableFloat(y));
        }

        public static float? ToNullableFloat(object value)
        {
            switch (value)
            {
                case float floatValue:
                    return floatValue;
                case double doubleValue:
                    return (float)doubleValue;
                case int intValue:
                    return intValue;
                default:
                    return null;
            }
        }

        public static bool ToBool(object value)
        {
            switch (value)
            {
                case bool boolValue:
                    return boolValue;
                case int intValue:
                    return intValue != 0;
                case string stringValue when bool.TryParse(stringValue, out var parsed):
                    return parsed;
                default:
                    return false;
            }
        }
    }
}
