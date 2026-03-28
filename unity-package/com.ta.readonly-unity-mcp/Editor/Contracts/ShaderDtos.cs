using System.Collections.Generic;

namespace TA.ReadOnlyUnityMcp.Contracts
{
    internal sealed class ShaderPassDto
    {
        public int index;
        public string name;
    }

    internal sealed class ShaderKeywordDto
    {
        public string name;
        public bool isOverridable;
        public object isDynamic;
    }

    internal sealed class ShaderPropertyDto
    {
        public string name;
        public string description;
        public string type;
        public string flags;
        public float? rangeMin;
        public float? rangeMax;
        public object attributes;
    }

    internal sealed class MaterialPropertyDto
    {
        public string name;
        public string description;
        public string type;
        public object value;
    }

    internal sealed class ShaderUsageDto
    {
        public int materialCount;
        public List<MaterialReferenceDto> materials;
    }

    internal sealed class ShaderInfoDto
    {
        public AssetReferenceDto asset;
        public string name;
        public bool isSupported;
        public int maximumLOD;
        public int passCount;
        public List<ShaderPassDto> passes;
        public List<ShaderKeywordDto> keywords;
        public List<ShaderPropertyDto> properties;
        public string fallback;
        public string customEditor;
        public ShaderUsageDto usage;
    }
}
