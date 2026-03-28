using System.Collections.Generic;
using TA.ReadOnlyUnityMcp.Contracts;
using UnityEngine;

namespace TA.ReadOnlyUnityMcp.Compat.Shader
{
    internal interface IShaderIntrospector
    {
        List<MaterialPropertyDto> ReadMaterialProperties(Material material);

        List<ShaderPassDto> ReadPasses(UnityEngine.Shader shader);

        List<ShaderKeywordDto> ReadKeywords(UnityEngine.Shader shader);

        List<ShaderPropertyDto> ReadProperties(UnityEngine.Shader shader);
    }
}
