using TA.UnityMcp.Contracts;

namespace TA.UnityMcp.Compat.ShaderGraph
{
    internal interface IShaderGraphReader
    {
        ShaderGraphReadResultDto Read(string assetPath);
    }
}
