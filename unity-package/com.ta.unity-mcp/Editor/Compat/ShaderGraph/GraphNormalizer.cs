using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TA.UnityMcp.Contracts;

namespace TA.UnityMcp.Compat.ShaderGraph
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
                    parseError = string.IsNullOrWhiteSpace(envelope.parseError)
                        ? "No JSON objects were found in the Shader Graph file."
                        : envelope.parseError,
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
            var nodesById = nodeObjects
                .Where(node => !string.IsNullOrWhiteSpace(node.Value<string>("m_ObjectId")))
                .GroupBy(node => node.Value<string>("m_ObjectId"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            var nodes = nodeObjects.Select(node => SimplifyNode(node, envelope.objectMap)).ToList();
            var edges = adapter.ResolveObjectList(envelope.root["m_Edges"], envelope.objectMap)
                .Select(edge => SimplifyEdge(edge, nodesById, envelope.objectMap))
                .ToList();
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
                parseError = envelope.parseError,
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

        private ShaderGraphNodeDto SimplifyNode(JObject node, IReadOnlyDictionary<string, JObject> objectMap)
        {
            var slots = adapter.ResolveNodeSlots(node, objectMap).Select(SimplifySlot).ToList();
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
                slots = slots.Count,
                inputSlots = slots.Where(slot => string.Equals(slot.direction, "input", StringComparison.OrdinalIgnoreCase)).ToList(),
                outputSlots = slots.Where(slot => string.Equals(slot.direction, "output", StringComparison.OrdinalIgnoreCase)).ToList()
            };
        }

        private ShaderGraphEdgeDto SimplifyEdge(JObject edge, IReadOnlyDictionary<string, JObject> nodesById, IReadOnlyDictionary<string, JObject> objectMap)
        {
            var outputNodeId = edge["m_OutputSlot"]?["m_Node"]?.Value<string>("m_Id")
                               ?? edge["m_OutputSlot"]?.Value<string>("m_NodeGUIDSerialized");
            var inputNodeId = edge["m_InputSlot"]?["m_Node"]?.Value<string>("m_Id")
                              ?? edge["m_InputSlot"]?.Value<string>("m_NodeGUIDSerialized");
            var outputNode = ResolveNode(nodesById, outputNodeId);
            var inputNode = ResolveNode(nodesById, inputNodeId);
            var outputSlotId = edge["m_OutputSlot"]?.Value<int?>("m_SlotId");
            var inputSlotId = edge["m_InputSlot"]?.Value<int?>("m_SlotId");
            var outputSlot = adapter.FindNodeSlotById(outputNode, outputSlotId, objectMap);
            var inputSlot = adapter.FindNodeSlotById(inputNode, inputSlotId, objectMap);

            return new ShaderGraphEdgeDto
            {
                objectId = edge.Value<string>("m_ObjectId"),
                outputNodeId = outputNodeId,
                outputSlotId = outputSlotId,
                outputSlotName = adapter.FirstString(outputSlot, "m_DisplayName", "m_Name", "m_ShaderOutputName"),
                inputNodeId = inputNodeId,
                inputSlotId = inputSlotId,
                inputSlotName = adapter.FirstString(inputSlot, "m_DisplayName", "m_Name", "m_ShaderOutputName")
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

        private ShaderGraphSlotDto SimplifySlot(JObject slot)
        {
            return new ShaderGraphSlotDto
            {
                objectId = slot.Value<string>("m_ObjectId"),
                slotId = slot.Value<int?>("m_Id"),
                displayName = adapter.FirstString(slot, "m_DisplayName", "m_Name", "m_ShaderOutputName"),
                direction = adapter.ResolveSlotDirection(slot),
                valueType = slot.Value<string>("m_Type"),
                slotType = slot.Value<int?>("m_SlotType"),
                shaderOutputName = slot.Value<string>("m_ShaderOutputName"),
                stageCapability = slot.Value<int?>("m_StageCapability"),
                hidden = slot.Value<bool?>("m_Hidden")
            };
        }

        private static JObject ResolveNode(IReadOnlyDictionary<string, JObject> nodesById, string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return null;
            }

            return nodesById != null && nodesById.TryGetValue(nodeId, out var node) ? node : null;
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
