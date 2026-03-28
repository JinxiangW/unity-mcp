using TA.ReadOnlyUnityMcp.Compat.ShaderGraph;
using TA.ReadOnlyUnityMcp.Contracts;

namespace TA.ReadOnlyUnityMcp
{
    internal static class UnityShaderGraphTextParser
    {
        public static ShaderGraphInfoDto Parse(string assetPath, string text)
        {
            return new TextShaderGraphReader().Parse(assetPath, text);
        }
    }
}
