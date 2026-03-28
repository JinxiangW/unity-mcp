using TA.ReadOnlyUnityMcp.Contracts;

namespace TA.ReadOnlyUnityMcp.Compat.ShaderGraph
{
    internal interface IShaderGraphReader
    {
        ShaderGraphReadResultDto Read(string assetPath);
    }
}
