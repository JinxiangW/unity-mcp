using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TA.ReadOnlyUnityMcp.Contracts;

namespace TA.ReadOnlyUnityMcp.Compat.ShaderGraph
{
    internal sealed class GraphNormalizer
    {
        private readonly GraphSchemaAdapter adapter;

        public GraphNormalizer(GraphSchemaAdapter adapter)
        {
            this.adapter = adapter;
        }

        public ShaderGraphInfoDto Normalize(GraphSchemaAdapter.Envelope envelope)
        {
            if (envelope.root == null)
            {
                return new ShaderGraphInfoDto
                {
                    format = envelope.format,
                    parseError = "No JSON objects were found in the Shader Graph file.",
                    warnings = envelope.warnings,
                    properties = new List<ShaderGraphPropertyDto>(),
                    keywords = new List<ShaderGraphKeywordDto>(),
                    categories = new List<ShaderGraphCategoryDto>(),
                    nodes = new List<ShaderGraphNodeDto>(),
                    edges = new List<ShaderGraphEdgeDto>(),
                    subGraphs = new List<ShaderGraphSubGraphDto>(),
                    targets = new List<ShaderGraphTargetDto>(),
                    output = new ShaderGraphOutputDto
                    {
                        vertexBlocks = new List<ShaderGraphBlockDto>(),
                        fragmentBlocks = new List<ShaderGraphBlockDto>(),
                        outputNode = null
                    }
                };
            }

            var properties = adapter.ResolveObjectList(envelope.root["m_Properties"], envelope.objectMap).Select(SimplifyProperty).ToList();
            var keywords = adapter.ResolveObjectList(envelope.root["m_Keywords"], envelope.objectMap).Select(SimplifyKeyword).ToList();
            var categories = adapter.ResolveObjectList(envelope.root["m_CategoryData"], envelope.objectMap).Select(category => new ShaderGraphCategoryDto
            {
                objectId = category.Value<string>("m_ObjectId"),
                name = adapter.FirstString(category, "m_Name", "m_DisplayName"),
                childCount = adapter.ResolveObjectList(category["m_ChildObjectList"], envelope.objectMap).Count
            }).ToList();

            var nodeObjects = adapter.ResolveObjectList(envelope.root["m_Nodes"], envelope.objectMap);
            var nodes = nodeObjects.Select(SimplifyNode).ToList();
            var edges = adapter.ResolveObjectList(envelope.root["m_Edges"], envelope.objectMap).Select(SimplifyEdge).ToList();
            var subGraphs = nodeObjects
                .Select(node => new ShaderGraphSubGraphDto
                {
                    objectId = node.Value<string>("m_ObjectId"),
                    displayName = adapter.FirstString(node, "m_Name", "m_DisplayName"),
                    subGraphGuid = adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"])
                })
                .Where(node => !string.IsNullOrWhiteSpace(node.subGraphGuid))
                .ToList();

            var targets = adapter.ResolveObjectList(envelope.root["m_ActiveTargets"], envelope.objectMap).Select(SimplifyTarget).ToList();

            return new ShaderGraphInfoDto
            {
                format = envelope.format,
                rootType = envelope.root.Value<string>("m_Type"),
                nodeCount = nodes.Count,
                edgeCount = edges.Count,
                propertyCount = properties.Count,
                keywordCount = keywords.Count,
                warnings = envelope.warnings,
                properties = properties,
                keywords = keywords,
                categories = categories,
                nodes = nodes,
                edges = edges,
                subGraphs = subGraphs,
                targets = targets,
                output = new ShaderGraphOutputDto
                {
                    vertexBlocks = ResolveBlocks(envelope.root["m_VertexContext"], envelope.objectMap),
                    fragmentBlocks = ResolveBlocks(envelope.root["m_FragmentContext"], envelope.objectMap),
                    outputNode = SimplifyObjectReference(envelope.root["m_OutputNode"], envelope.objectMap)
                }
            };
        }

        private ShaderGraphPropertyDto SimplifyProperty(JObject property)
        {
            var overrideReferenceName = property.Value<string>("m_OverrideReferenceName");
            return new ShaderGraphPropertyDto
            {
                objectId = property.Value<string>("m_ObjectId"),
                type = property.Value<string>("m_Type"),
                displayName = adapter.FirstString(property, "m_DisplayName", "m_Name"),
                referenceName = !string.IsNullOrWhiteSpace(overrideReferenceName)
                    ? overrideReferenceName
                    : adapter.FirstString(property, "m_RefNameGeneratedByDisplayName", "m_ReferenceName", "m_DefaultReferenceName", "m_Name"),
                valueType = adapter.FirstString(property, "m_ValueType", "m_Type")
            };
        }

        private ShaderGraphKeywordDto SimplifyKeyword(JObject keyword)
        {
            return new ShaderGraphKeywordDto
            {
                objectId = keyword.Value<string>("m_ObjectId"),
                type = keyword.Value<string>("m_Type"),
                displayName = adapter.FirstString(keyword, "m_DisplayName", "m_Name"),
                referenceName = adapter.FirstString(keyword, "m_ReferenceName", "m_RefNameGeneratedByDisplayName", "m_Name"),
                definition = adapter.FirstString(keyword, "m_KeywordDefinition"),
                scope = adapter.FirstString(keyword, "m_Scope")
            };
        }

        private ShaderGraphNodeDto SimplifyNode(JObject node)
        {
            return new ShaderGraphNodeDto
            {
                objectId = node.Value<string>("m_ObjectId"),
                type = node.Value<string>("m_Type"),
                displayName = adapter.FirstString(node, "m_Name", "m_DisplayName"),
                position = node["m_DrawState"]?["m_Position"] != null ? new ShaderGraphPositionDto
                {
                    x = node["m_DrawState"]["m_Position"].Value<float?>("x"),
                    y = node["m_DrawState"]["m_Position"].Value<float?>("y"),
                    width = node["m_DrawState"]["m_Position"].Value<float?>("width"),
                    height = node["m_DrawState"]["m_Position"].Value<float?>("height")
                } : null,
                subGraphGuid = adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"]),
                slots = node["m_Slots"] is JArray slots ? slots.Count : node["m_SerializableSlots"] is JArray legacySlots ? legacySlots.Count : 0
            };
        }

        private static ShaderGraphEdgeDto SimplifyEdge(JObject edge)
        {
            return new ShaderGraphEdgeDto
            {
                objectId = edge.Value<string>("m_ObjectId"),
                outputNodeId = edge["m_OutputSlot"]?["m_Node"]?.Value<string>("m_Id")
                               ?? edge["m_OutputSlot"]?.Value<string>("m_NodeGUIDSerialized"),
                outputSlotId = edge["m_OutputSlot"]?.Value<int?>("m_SlotId"),
                inputNodeId = edge["m_InputSlot"]?["m_Node"]?.Value<string>("m_Id")
                              ?? edge["m_InputSlot"]?.Value<string>("m_NodeGUIDSerialized"),
                inputSlotId = edge["m_InputSlot"]?.Value<int?>("m_SlotId")
            };
        }

        private ShaderGraphTargetDto SimplifyTarget(JObject target)
        {
            return new ShaderGraphTargetDto
            {
                objectId = target.Value<string>("m_ObjectId"),
                type = target.Value<string>("m_Type"),
                displayName = adapter.FirstString(target, "m_DisplayName", "m_Name", "m_TargetId"),
                activeSubTarget = target["m_ActiveSubTarget"]?.ToString(Newtonsoft.Json.Formatting.None)
            };
        }

        private List<ShaderGraphBlockDto> ResolveBlocks(JToken contextToken, IReadOnlyDictionary<string, JObject> objectMap)
        {
            if (!(contextToken is JObject contextObject))
            {
                return new List<ShaderGraphBlockDto>();
            }

            return adapter.ResolveObjectList(contextObject["m_Blocks"], objectMap)
                .Select(block => new ShaderGraphBlockDto
                {
                    objectId = block.Value<string>("m_ObjectId"),
                    type = block.Value<string>("m_Type"),
                    descriptor = adapter.FirstString(block, "m_SerializedDescriptor", "m_Name")
                })
                .ToList();
        }

        private ShaderGraphObjectReferenceDto SimplifyObjectReference(JToken token, IReadOnlyDictionary<string, JObject> objectMap)
        {
            if (!(token is JObject objectReference))
            {
                return null;
            }

            var objectId = objectReference.Value<string>("m_Id");
            if (string.IsNullOrWhiteSpace(objectId) || !objectMap.TryGetValue(objectId, out var resolved))
            {
                return null;
            }

            return new ShaderGraphObjectReferenceDto
            {
                objectId = objectId,
                type = resolved.Value<string>("m_Type"),
                displayName = adapter.FirstString(resolved, "m_Name", "m_DisplayName")
            };
        }
    }
}
