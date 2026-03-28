using UnityEngine;

namespace TA.ReadOnlyUnityMcp.Compat
{
    internal sealed class UnityVersionContext
    {
        public string unityVersion;
        public string shaderGraphPackageVersion;
        public string renderPipelinePackageVersion;
        public bool hasShaderGetPropertyApi;
        public bool hasShaderKeywordSpace;
        public bool hasVolumeType;
        public bool hasShaderGraphAssembly;

        public static UnityVersionContext Create()
        {
            return new UnityVersionContext
            {
                unityVersion = Application.unityVersion,
                shaderGraphPackageVersion = CapabilityProbe.GetPackageVersion("Packages/com.unity.shadergraph"),
                renderPipelinePackageVersion = CapabilityProbe.GetPackageVersion("Packages/com.unity.render-pipelines.universal"),
                hasShaderGetPropertyApi = CapabilityProbe.HasInstanceMethod(typeof(UnityEngine.Shader), "GetPropertyCount"),
                hasShaderKeywordSpace = CapabilityProbe.HasReadableProperty(typeof(UnityEngine.Shader), "keywordSpace"),
                hasVolumeType = CapabilityProbe.HasType("UnityEngine.Rendering.Volume"),
                hasShaderGraphAssembly = CapabilityProbe.HasLoadedAssembly("ShaderGraph")
            };
        }
    }
}
