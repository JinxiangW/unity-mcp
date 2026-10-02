using System;
using System.Collections.Generic;

namespace TA.UnityMcp.Contracts
{
    [Serializable]
    internal sealed class ShaderGraphPropertyDto
    {
        public string objectId;
        public string type;
        public string displayName;
        public string referenceName;
        public string valueType;
    }

    [Serializable]
    internal sealed class ShaderGraphKeywordDto
    {
        public string objectId;
        public string type;
        public string displayName;
        public string referenceName;
        public string definition;
        public string scope;
    }

    [Serializable]
    internal sealed class ShaderGraphCategoryDto
    {
        public string objectId;
        public string name;
        public int childCount;
    }

    [Serializable]
    internal sealed class ShaderGraphPositionDto
    {
        public float? x;
        public float? y;
        public float? width;
        public float? height;
    }

    [Serializable]
    internal sealed class ShaderGraphSlotDto
    {
        public string objectId;
        public int? slotId;
        public string displayName;
        public string direction;
        public string valueType;
        public int? slotType;
        public string shaderOutputName;
        public int? stageCapability;
        public bool? hidden;
    }

    [Serializable]
    internal sealed class ShaderGraphNodeDto
    {
        public string objectId;
        public string type;
        public string displayName;
        public ShaderGraphPositionDto position;
        public string subGraphGuid;
        public int slots;
        public List<ShaderGraphSlotDto> inputSlots;
        public List<ShaderGraphSlotDto> outputSlots;
    }

    [Serializable]
    internal sealed class ShaderGraphEdgeDto
    {
        public string objectId;
        public string outputNodeId;
        public int? outputSlotId;
        public string outputSlotName;
        public string inputNodeId;
        public int? inputSlotId;
        public string inputSlotName;
    }

    [Serializable]
    internal sealed class ShaderGraphSubGraphDto
    {
        public string objectId;
        public string displayName;
        public string subGraphGuid;
    }

    [Serializable]
    internal sealed class ShaderGraphTargetDto
    {
        public string objectId;
        public string type;
        public string displayName;
        public string activeSubTarget;
    }

    [Serializable]
    internal sealed class ShaderGraphBlockDto
    {
        public string objectId;
        public string type;
        public string descriptor;
    }

    [Serializable]
    internal sealed class ShaderGraphObjectReferenceDto
    {
        public string objectId;
        public string type;
        public string displayName;
    }

    [Serializable]
    internal sealed class ShaderGraphOutputDto
    {
        public List<ShaderGraphBlockDto> vertexBlocks;
        public List<ShaderGraphBlockDto> fragmentBlocks;
        public ShaderGraphObjectReferenceDto outputNode;
    }

    [Serializable]
    internal sealed class ShaderGraphInfoDto
    {
        public string format;
        public string rootType;
        public int nodeCount;
        public int edgeCount;
        public int propertyCount;
        public int keywordCount;
        public string parseError;
        public List<string> warnings;
        public List<ShaderGraphPropertyDto> properties;
        public List<ShaderGraphKeywordDto> keywords;
        public List<ShaderGraphCategoryDto> categories;
        public List<ShaderGraphNodeDto> nodes;
        public List<ShaderGraphEdgeDto> edges;
        public List<ShaderGraphSubGraphDto> subGraphs;
        public List<ShaderGraphTargetDto> targets;
        public ShaderGraphOutputDto output;
    }

    [Serializable]
    internal sealed class ShaderGraphReadResultDto
    {
        public int sourceLength;
        public ShaderGraphInfoDto graph;
    }
}
