using System.Collections.Generic;
using TA.UnityMcp.Contracts;
using UnityEngine;

namespace TA.UnityMcp.Compat.Shader
{
    internal interface IShaderIntrospector
    {
        List<MaterialPropertyDto> ReadMaterialProperties(Material material);

        List<ShaderPassDto> ReadPasses(UnityEngine.Shader shader);

        List<ShaderKeywordDto> ReadKeywords(UnityEngine.Shader shader);

        List<ShaderPropertyDto> ReadProperties(UnityEngine.Shader shader);
    }
}
