using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TA.ReadOnlyUnityMcp.Compat
{
    internal static class CapabilityProbe
    {
        public static bool HasType(string typeName)
        {
            return FindType(typeName) != null;
        }

        public static Type FindType(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName, false))
                .FirstOrDefault(type => type != null);
        }

        public static bool HasInstanceMethod(Type type, string methodName, params Type[] parameterTypes)
        {
            return GetInstanceMethod(type, methodName, parameterTypes) != null;
        }

        public static MethodInfo GetInstanceMethod(Type type, string methodName, params Type[] parameterTypes)
        {
            return type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, parameterTypes, null);
        }

        public static bool HasReadableProperty(Type type, string propertyName)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.CanRead;
        }

        public static string GetPackageVersion(string packageAssetPath)
        {
            var packageInfoType = FindType("UnityEditor.PackageManager.PackageInfo");
            if (packageInfoType == null)
            {
                return null;
            }

            var findForAssetPath = packageInfoType.GetMethod(
                "FindForAssetPath",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (findForAssetPath == null)
            {
                return null;
            }

            var packageInfo = findForAssetPath.Invoke(null, new object[] { packageAssetPath });
            if (packageInfo == null)
            {
                return null;
            }

            var versionProperty = packageInfoType.GetProperty("version", BindingFlags.Instance | BindingFlags.Public);
            return versionProperty?.GetValue(packageInfo, null) as string;
        }

        public static bool HasLoadedAssembly(string assemblyNameFragment)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.GetName().Name.IndexOf(assemblyNameFragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
