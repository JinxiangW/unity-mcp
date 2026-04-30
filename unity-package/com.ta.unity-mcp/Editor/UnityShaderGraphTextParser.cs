using TA.UnityMcp.Compat.ShaderGraph;
using TA.UnityMcp.Contracts;

namespace TA.UnityMcp
{
    internal static class UnityShaderGraphTextParser
    {
        public static ShaderGraphInfoDto Parse(string assetPath, string text)
        {
            return new TextShaderGraphReader().Parse(assetPath, text);
        }
    }
}
