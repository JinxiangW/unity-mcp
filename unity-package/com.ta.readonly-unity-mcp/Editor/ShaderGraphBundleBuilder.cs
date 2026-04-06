using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TA.ReadOnlyUnityMcp.Compat.ShaderGraph;
using UnityEditor;

namespace TA.ReadOnlyUnityMcp
{
    internal static class ShaderGraphBundleBuilder
    {
        public static JObject BuildSection(string assetPath, bool recursive)
        {
            var bundle = BuildBundle(assetPath, recursive);
            var mainGraph = bundle["mainGraph"] as JObject;
            var summary = mainGraph?["graph"]?["summary"] != null ? (JObject)mainGraph["graph"]["summary"].DeepClone() : new JObject();

            return new JObject
            {
                ["summary"] = summary,
                ["bundle"] = bundle
            };
        }

        private static JObject BuildBundle(string assetPath, bool recursive)
        {
            var guidMap = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(path => AssetDatabase.AssetPathToGUID(path), NormalizeAssetPath, StringComparer.OrdinalIgnoreCase);

            var exportedGraphs = new Dictionary<string, GraphExportRecord>(StringComparer.OrdinalIgnoreCase);
            var mainGraph = ExportShaderGraph(assetPath, true, recursive, guidMap, exportedGraphs);
            var subgraphs = new JArray(exportedGraphs.Values
                .Where(record => !string.Equals(record.assetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.exportFile, StringComparer.OrdinalIgnoreCase)
                .Select(record => record.payload));
            var index = new JArray(exportedGraphs.Values
                .Where(record => !string.Equals(record.assetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.exportFile, StringComparer.OrdinalIgnoreCase)
                .Select(record => new JObject
                {
                    ["name"] = record.name,
                    ["path"] = record.assetPath,
                    ["guid"] = record.guid,
                    ["exportFile"] = record.exportFile
                }));

            return new JObject
            {
                ["mainGraphFile"] = mainGraph.exportFile,
                ["subgraphsDirectory"] = "subgraphs",
                ["indexFile"] = "subgraphs/index.json",
                ["recursive"] = recursive,
                ["mainGraph"] = mainGraph.payload,
                ["subgraphs"] = subgraphs,
                ["index"] = index
            };
        }

        private static GraphExportRecord ExportShaderGraph(string assetPath, bool isMainGraph, bool recursive, IReadOnlyDictionary<string, string> guidMap, IDictionary<string, GraphExportRecord> exportedGraphs)
        {
            assetPath = NormalizeAssetPath(assetPath);
            if (exportedGraphs.TryGetValue(assetPath, out var existing))
            {
                return existing;
            }

            var adapter = new GraphSchemaAdapter();
            var text = File.ReadAllText(UnityReadOnlyMcpQueries.ToAbsoluteProjectPath(assetPath));
            var parseResult = GraphEnvelopeReader.ParseObjects(text);
            var envelope = adapter.BuildEnvelope(parseResult);
            var root = envelope.root;
            var objectMap = envelope.objectMap;

            var properties = root != null ? adapter.ResolveObjectList(root["m_Properties"], objectMap) : new List<JObject>();
            var keywords = root != null ? adapter.ResolveObjectList(root["m_Keywords"], objectMap) : new List<JObject>();
            var categories = root != null ? adapter.ResolveObjectList(root["m_CategoryData"], objectMap) : new List<JObject>();
            var groups = root != null ? adapter.ResolveObjectList(root["m_GroupDatas"], objectMap) : new List<JObject>();
            var stickyNotes = root != null ? adapter.ResolveObjectList(root["m_StickyNoteDatas"], objectMap) : new List<JObject>();
            var nodes = root != null ? adapter.ResolveObjectList(root["m_Nodes"], objectMap) : new List<JObject>();
            var edges = root != null ? adapter.ResolveObjectList(root["m_Edges"], objectMap) : new List<JObject>();
            var propertyIds = properties.Select(property => property.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var keywordIds = keywords.Select(keyword => keyword.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var nodeIds = nodes.Select(node => node.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet();
            var nodesById = nodes
                .Where(node => !string.IsNullOrWhiteSpace(node.Value<string>("m_ObjectId")))
                .GroupBy(node => node.Value<string>("m_ObjectId"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            var categoryMembership = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var categoryExports = new JArray();

            foreach (var category in categories)
            {
                var childObjects = adapter.ResolveObjectList(category["m_ChildObjectList"], objectMap);
                var childObjectIds = new JArray(childObjects.Select(child => child.Value<string>("m_ObjectId")).Where(id => !string.IsNullOrWhiteSpace(id)));
                foreach (var childObjectId in childObjectIds.Values<string>())
                {
                    if (!categoryMembership.TryGetValue(childObjectId, out var memberships))
                    {
                        memberships = new List<string>();
                        categoryMembership[childObjectId] = memberships;
                    }

                    memberships.Add(category.Value<string>("m_ObjectId"));
                }

                categoryExports.Add(new JObject
                {
                    ["objectId"] = category.Value<string>("m_ObjectId"),
                    ["name"] = ToJToken(adapter.FirstString(category, "m_Name", "m_DisplayName")),
                    ["childCount"] = childObjects.Count,
                    ["childObjectIds"] = childObjectIds,
                    ["childObjects"] = new JArray(childObjects.Select(child => SimplifyGraphObjectReference(child, propertyIds, keywordIds, nodeIds, adapter)))
                });
            }

            var nodeExports = new JArray(nodes.Select(node =>
            {
                var export = new JObject
                {
                    ["objectId"] = node.Value<string>("m_ObjectId"),
                    ["type"] = node.Value<string>("m_Type"),
                    ["displayName"] = ToJToken(adapter.FirstString(node, "m_Name", "m_DisplayName")),
                    ["position"] = BuildPositionObject(node["m_DrawState"]?["m_Position"] as JObject),
                    ["groupId"] = ToJToken(node["m_Group"]?["m_Id"]?.Value<string>()),
                    ["categoryIds"] = categoryMembership.TryGetValue(node.Value<string>("m_ObjectId"), out var memberships) ? new JArray(memberships) : new JArray(),
                    ["subGraphGuid"] = ToJToken(adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"])),
                    ["subGraphPath"] = ToJToken(ResolveSubGraphPath(adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"]), guidMap)),
                    ["slots"] = adapter.ResolveNodeSlots(node, objectMap).Count,
                    ["inputSlots"] = BuildNodeSlotExports(node, objectMap, adapter, "input"),
                    ["outputSlots"] = BuildNodeSlotExports(node, objectMap, adapter, "output")
                };

                var customFunction = BuildCustomFunctionExport(node, guidMap);
                if (customFunction != null)
                {
                    export["customFunction"] = customFunction;
                }

                return export;
            }));

            var edgeExports = new JArray(edges.Select(edge =>
            {
                var outputNodeId = edge["m_OutputSlot"]?["m_Node"]?["m_Id"]?.Value<string>()
                                   ?? edge["m_OutputSlot"]?.Value<string>("m_NodeGUIDSerialized");
                var inputNodeId = edge["m_InputSlot"]?["m_Node"]?["m_Id"]?.Value<string>()
                                  ?? edge["m_InputSlot"]?.Value<string>("m_NodeGUIDSerialized");
                var outputNode = ResolveNode(nodesById, outputNodeId);
                var inputNode = ResolveNode(nodesById, inputNodeId);
                var outputSlotId = edge["m_OutputSlot"]?.Value<int?>("m_SlotId");
                var inputSlotId = edge["m_InputSlot"]?.Value<int?>("m_SlotId");

                return new JObject
                {
                    ["objectId"] = ToJToken(edge.Value<string>("m_ObjectId")),
                    ["outputNodeId"] = ToJToken(outputNodeId),
                    ["outputSlotId"] = ToJToken(outputSlotId),
                    ["outputSlotName"] = ToJToken(ResolveNodeSlotDisplayName(outputNode, outputSlotId, objectMap, adapter)),
                    ["inputNodeId"] = ToJToken(inputNodeId),
                    ["inputSlotId"] = ToJToken(inputSlotId),
                    ["inputSlotName"] = ToJToken(ResolveNodeSlotDisplayName(inputNode, inputSlotId, objectMap, adapter))
                };
            }));

            var groupExports = new JArray(groups.Select(group => new JObject
            {
                ["objectId"] = group.Value<string>("m_ObjectId"),
                ["title"] = ToJToken(adapter.FirstString(group, "m_Title", "m_Name", "m_DisplayName")),
                ["position"] = BuildPositionObject(group["m_DrawState"]?["m_Position"] as JObject ?? group["m_Position"] as JObject),
                ["containedObjectIds"] = new JArray(adapter.ResolveObjectList(group["m_ContainedNodes"], objectMap)
                    .Concat(adapter.ResolveObjectList(group["m_Items"], objectMap))
                    .Concat(adapter.ResolveObjectList(group["m_GroupItems"], objectMap))
                    .Select(item => item.Value<string>("m_ObjectId"))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
            }));

            var stickyExports = new JArray(stickyNotes.Select(note => new JObject
            {
                ["objectId"] = note.Value<string>("m_ObjectId"),
                ["title"] = ToJToken(note.Value<string>("m_Title")),
                ["content"] = ToJToken(note.Value<string>("m_Content")),
                ["theme"] = ToJToken(note.Value<string>("m_Theme") ?? note.Value<int?>("m_Theme")?.ToString()),
                ["textSize"] = ToJToken(note.Value<int?>("m_TextSize")),
                ["position"] = BuildPositionObject(note["m_Position"] as JObject)
            }));

            var targetExports = new JArray((root != null ? adapter.ResolveObjectList(root["m_ActiveTargets"], objectMap) : new List<JObject>()).Select(target => new JObject
            {
                ["objectId"] = target.Value<string>("m_ObjectId"),
                ["type"] = target.Value<string>("m_Type"),
                ["displayName"] = ToJToken(adapter.FirstString(target, "m_DisplayName", "m_Name", "m_TargetId")),
                ["activeSubTarget"] = ToJToken(target["m_ActiveSubTarget"]?.ToString(Newtonsoft.Json.Formatting.None))
            }));

            var subGraphReferences = new JArray();
            var record = new GraphExportRecord
            {
                assetPath = assetPath,
                guid = AssetDatabase.AssetPathToGUID(assetPath),
                name = Path.GetFileNameWithoutExtension(assetPath),
                exportFile = BuildShaderGraphExportPath(assetPath, isMainGraph),
                payload = new JObject()
            };
            exportedGraphs[assetPath] = record;

            foreach (var node in nodes)
            {
                var subGraphGuid = adapter.ExtractSubGraphGuid(node["m_SerializedSubGraph"]);
                if (string.IsNullOrWhiteSpace(subGraphGuid))
                {
                    continue;
                }

                var subGraphPath = ResolveSubGraphPath(subGraphGuid, guidMap);
                var subgraphExportFile = !string.IsNullOrWhiteSpace(subGraphPath) ? BuildShaderGraphExportPath(subGraphPath, false) : null;

                if (recursive && !string.IsNullOrWhiteSpace(subGraphPath))
                {
                    subgraphExportFile = ExportShaderGraph(subGraphPath, false, true, guidMap, exportedGraphs).exportFile;
                }

                subGraphReferences.Add(new JObject
                {
                    ["objectId"] = node.Value<string>("m_ObjectId"),
                    ["displayName"] = ToJToken(adapter.FirstString(node, "m_Name", "m_DisplayName")),
                    ["subGraphGuid"] = subGraphGuid,
                    ["assetPath"] = ToJToken(subGraphPath),
                    ["exportFile"] = ToJToken(subgraphExportFile)
                });
            }

            record.payload = new JObject
            {
                ["schemaVersion"] = "unity-shadergraph-export/1.0",
                ["exportFile"] = record.exportFile,
                ["asset"] = JObject.FromObject(new
                {
                    name = record.name,
                    path = assetPath,
                    guid = record.guid,
                    type = IsSubGraphAssetPath(assetPath) ? "UnityEditor.ShaderGraph.SubGraphAsset" : "UnityEngine.Shader"
                }),
                ["graph"] = new JObject
                {
                    ["format"] = envelope.format,
                    ["rootType"] = ToJToken(root?.Value<string>("m_Type")),
                    ["parseError"] = ToJToken(envelope.parseError),
                    ["summary"] = new JObject
                    {
                        ["propertyCount"] = properties.Count,
                        ["keywordCount"] = keywords.Count,
                        ["categoryCount"] = categories.Count,
                        ["groupCount"] = groups.Count,
                        ["stickyNoteCount"] = stickyNotes.Count,
                        ["nodeCount"] = nodes.Count,
                        ["edgeCount"] = edges.Count,
                        ["subGraphReferenceCount"] = subGraphReferences.Count,
                        ["targetCount"] = targetExports.Count
                    },
                    ["properties"] = new JArray(properties.Select(property => new JObject
                    {
                        ["objectId"] = property.Value<string>("m_ObjectId"),
                        ["type"] = property.Value<string>("m_Type"),
                        ["displayName"] = ToJToken(adapter.FirstString(property, "m_DisplayName", "m_Name")),
                        ["referenceName"] = ToJToken(property.Value<string>("m_OverrideReferenceName")
                            ?? adapter.FirstString(property, "m_RefNameGeneratedByDisplayName", "m_ReferenceName", "m_DefaultReferenceName", "m_Name")),
                        ["valueType"] = ToJToken(adapter.FirstString(property, "m_ValueType", "m_Type")),
                        ["groupId"] = ToJToken(property["m_Group"]?["m_Id"]?.Value<string>())
                    })),
                    ["keywords"] = new JArray(keywords.Select(keyword => new JObject
                    {
                        ["objectId"] = keyword.Value<string>("m_ObjectId"),
                        ["type"] = keyword.Value<string>("m_Type"),
                        ["displayName"] = ToJToken(adapter.FirstString(keyword, "m_DisplayName", "m_Name")),
                        ["referenceName"] = ToJToken(adapter.FirstString(keyword, "m_ReferenceName", "m_RefNameGeneratedByDisplayName", "m_Name")),
                        ["definition"] = ToJToken(adapter.FirstString(keyword, "m_KeywordDefinition")),
                        ["scope"] = ToJToken(adapter.FirstString(keyword, "m_Scope")),
                        ["groupId"] = ToJToken(keyword["m_Group"]?["m_Id"]?.Value<string>())
                    })),
                    ["categories"] = categoryExports,
                    ["groups"] = groupExports,
                    ["stickyNotes"] = stickyExports,
                    ["nodes"] = nodeExports,
                    ["edges"] = edgeExports,
                    ["targets"] = targetExports,
                    ["output"] = new JObject
                    {
                        ["outputNode"] = BuildOutputNode(root?["m_OutputNode"] as JObject, objectMap, adapter),
                        ["vertexBlocks"] = BuildGraphBlocks(root?["m_VertexContext"]?["m_Blocks"], objectMap, adapter),
                        ["fragmentBlocks"] = BuildGraphBlocks(root?["m_FragmentContext"]?["m_Blocks"], objectMap, adapter)
                    },
                    ["subGraphReferences"] = subGraphReferences,
                    ["warnings"] = new JArray(envelope.warnings ?? new List<string>())
                }
            };

            return record;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/');
        }

        private static bool IsSubGraphAssetPath(string assetPath)
        {
            return string.Equals(Path.GetExtension(assetPath), ".shadersubgraph", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject BuildPositionObject(JObject position)
        {
            if (position == null)
            {
                return null;
            }

            return new JObject
            {
                ["x"] = ToJToken(position.Value<float?>("x")),
                ["y"] = ToJToken(position.Value<float?>("y")),
                ["width"] = ToJToken(position.Value<float?>("width")),
                ["height"] = ToJToken(position.Value<float?>("height"))
            };
        }

        private static JArray BuildNodeSlotExports(JObject node, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter, string directionFilter)
        {
            return new JArray(adapter.ResolveNodeSlots(node, objectMap)
                .Select(slot => BuildSlotExport(slot, adapter))
                .Where(slot => string.Equals(slot.Value<string>("direction"), directionFilter, StringComparison.OrdinalIgnoreCase)));
        }

        private static JObject BuildSlotExport(JObject slot, GraphSchemaAdapter adapter)
        {
            return new JObject
            {
                ["objectId"] = ToJToken(slot.Value<string>("m_ObjectId")),
                ["slotId"] = ToJToken(slot.Value<int?>("m_Id")),
                ["displayName"] = ToJToken(adapter.FirstString(slot, "m_DisplayName", "m_Name", "m_ShaderOutputName")),
                ["direction"] = ToJToken(adapter.ResolveSlotDirection(slot)),
                ["valueType"] = ToJToken(slot.Value<string>("m_Type")),
                ["slotType"] = ToJToken(slot.Value<int?>("m_SlotType")),
                ["shaderOutputName"] = ToJToken(slot.Value<string>("m_ShaderOutputName")),
                ["stageCapability"] = ToJToken(slot.Value<int?>("m_StageCapability")),
                ["hidden"] = ToJToken(slot.Value<bool?>("m_Hidden"))
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

        private static string ResolveNodeSlotDisplayName(JObject node, int? slotId, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter)
        {
            var slot = adapter.FindNodeSlotById(node, slotId, objectMap);
            return adapter.FirstString(slot, "m_DisplayName", "m_Name", "m_ShaderOutputName");
        }

        private static JObject SimplifyGraphObjectReference(JObject source, ISet<string> propertyIds, ISet<string> keywordIds, ISet<string> nodeIds, GraphSchemaAdapter adapter)
        {
            var objectId = source.Value<string>("m_ObjectId");
            return new JObject
            {
                ["objectId"] = objectId,
                ["type"] = source.Value<string>("m_Type"),
                ["displayName"] = ToJToken(adapter.FirstString(source, "m_Name", "m_DisplayName")),
                ["kind"] = propertyIds.Contains(objectId) ? "property" : keywordIds.Contains(objectId) ? "keyword" : nodeIds.Contains(objectId) ? "node" : "object"
            };
        }

        private static JObject BuildOutputNode(JObject token, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter)
        {
            var objectId = token?["m_Id"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(objectId) || !objectMap.TryGetValue(objectId, out var resolved))
            {
                return null;
            }

            return new JObject
            {
                ["objectId"] = objectId,
                ["type"] = resolved.Value<string>("m_Type"),
                ["displayName"] = ToJToken(adapter.FirstString(resolved, "m_Name", "m_DisplayName"))
            };
        }

        private static JArray BuildGraphBlocks(JToken blocksToken, IReadOnlyDictionary<string, JObject> objectMap, GraphSchemaAdapter adapter)
        {
            return new JArray(adapter.ResolveObjectList(blocksToken, objectMap).Select(block => new JObject
            {
                ["objectId"] = block.Value<string>("m_ObjectId"),
                ["type"] = block.Value<string>("m_Type"),
                ["descriptor"] = ToJToken(adapter.FirstString(block, "m_SerializedDescriptor", "m_Name"))
            }));
        }

        private static string BuildShaderGraphExportPath(string assetPath, bool isMainGraph)
        {
            if (isMainGraph)
            {
                return "shadergraph.json";
            }

            var normalizedPath = NormalizeAssetPath(assetPath);
            const string ShadersRoot = "Assets/Shaders/";
            if (normalizedPath.StartsWith(ShadersRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "subgraphs/" + normalizedPath.Substring(ShadersRoot.Length)
                    .Replace(".shadergraph", ".json")
                    .Replace(".shadersubgraph", ".json");
            }

            return "subgraphs/" + Path.GetFileNameWithoutExtension(normalizedPath) + ".json";
        }

        private static string ResolveSubGraphPath(string guid, IReadOnlyDictionary<string, string> guidMap)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            return guidMap.TryGetValue(guid, out var assetPath) ? assetPath : null;
        }

        private static JObject BuildCustomFunctionExport(JObject node, IReadOnlyDictionary<string, string> guidMap)
        {
            if (node == null)
            {
                return null;
            }

            var typeName = node.Value<string>("m_Type");
            if (string.IsNullOrWhiteSpace(typeName)
                || typeName.IndexOf("CustomFunctionNode", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var sourceType = node.Value<int?>("m_SourceType");
            var sourceGuid = node.Value<string>("m_FunctionSource");
            return new JObject
            {
                ["sourceType"] = ToJToken(sourceType),
                ["sourceTypeName"] = ToJToken(ResolveCustomFunctionSourceTypeName(sourceType)),
                ["functionName"] = ToJToken(node.Value<string>("m_FunctionName")),
                ["functionSourceGuid"] = ToJToken(sourceGuid),
                ["functionSourcePath"] = ToJToken(ResolveSubGraphPath(sourceGuid, guidMap)),
                ["functionBody"] = ToJToken(node.Value<string>("m_FunctionBody"))
            };
        }

        private static string ResolveCustomFunctionSourceTypeName(int? sourceType)
        {
            switch (sourceType)
            {
                case 0:
                    return "file";
                case 1:
                    return "string";
                default:
                    return sourceType.HasValue ? $"unknown_{sourceType.Value}" : null;
            }
        }

        private static JToken ToJToken(object value)
        {
            return value != null ? JToken.FromObject(value) : JValue.CreateNull();
        }

        private sealed class GraphExportRecord
        {
            public string assetPath;
            public string exportFile;
            public string guid;
            public string name;
            public JObject payload;
        }
    }
}
