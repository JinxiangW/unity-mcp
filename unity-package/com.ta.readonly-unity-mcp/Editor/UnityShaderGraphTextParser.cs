using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TA.ReadOnlyUnityMcp
{
    internal static class UnityShaderGraphTextParser
    {
        public static object Parse(string assetPath, string text)
        {
            var objects = ParseObjects(text);
            if (objects.Count == 0)
            {
                return new
                {
                    assetPath,
                    format = "unknown",
                    parseError = "No JSON objects were found in the Shader Graph file."
                };
            }

            var root = FindRootObject(objects);
            var objectMap = objects
                .Where(obj => obj["m_ObjectId"] != null)
                .GroupBy(obj => obj.Value<string>("m_ObjectId"))
                .ToDictionary(group => group.Key, group => group.Last());

            var format = root["m_Type"] != null ? "multi-json" : "single-json";

            var nodes = ResolveObjectList(root["m_Nodes"], objectMap).Select(SimplifyNode).ToList();

            return new
            {
                format,
                rootType = root.Value<string>("m_Type"),
                nodeCount = nodes.Count,
                edgeCount = ResolveObjectList(root["m_Edges"], objectMap).Count,
                propertyCount = ResolveObjectList(root["m_Properties"], objectMap).Count,
                keywordCount = ResolveObjectList(root["m_Keywords"], objectMap).Count,
                properties = ResolveObjectList(root["m_Properties"], objectMap).Select(SimplifyProperty).ToList(),
                keywords = ResolveObjectList(root["m_Keywords"], objectMap).Select(SimplifyKeyword).ToList(),
                categories = ResolveObjectList(root["m_CategoryData"], objectMap).Select(category => new
                {
                    objectId = category.Value<string>("m_ObjectId"),
                    name = FirstString(category, "m_Name", "m_DisplayName"),
                    childCount = ResolveObjectList(category["m_ChildObjectList"], objectMap).Count
                }).ToList(),
                nodes,
                edges = ResolveObjectList(root["m_Edges"], objectMap).Select(SimplifyEdge).ToList(),
                subGraphs = nodes.Where(node => node.subGraphGuid != null).Select(node => new
                {
                    node.objectId,
                    node.displayName,
                    node.subGraphGuid
                }).ToList(),
                targets = ResolveObjectList(root["m_ActiveTargets"], objectMap).Select(SimplifyTarget).ToList(),
                output = new
                {
                    vertexBlocks = ResolveBlocks(root["m_VertexContext"], objectMap),
                    fragmentBlocks = ResolveBlocks(root["m_FragmentContext"], objectMap),
                    outputNode = SimplifyObjectReference(root["m_OutputNode"], objectMap)
                }
            };
        }

        private static List<JObject> ParseObjects(string text)
        {
            var results = new List<JObject>();
            var depth = 0;
            var startIndex = -1;
            var inString = false;
            var escaping = false;

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];

                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (inString && character == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (character == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (character == '{')
                {
                    if (depth == 0)
                    {
                        startIndex = index;
                    }

                    depth++;
                }
                else if (character == '}')
                {
                    depth--;

                    if (depth == 0 && startIndex >= 0)
                    {
                        var slice = text.Substring(startIndex, index - startIndex + 1);
                        results.Add(JObject.Parse(slice));
                        startIndex = -1;
                    }
                }
            }

            return results;
        }

        private static JObject FindRootObject(List<JObject> objects)
        {
            return objects.FirstOrDefault(LooksLikeRootGraphObject) ?? objects[0];
        }

        private static bool LooksLikeRootGraphObject(JObject obj)
        {
            var typeName = obj.Value<string>("m_Type");
            return obj["m_Nodes"] != null
                   || obj["m_Properties"] != null
                   || obj["m_Edges"] != null
                   || (!string.IsNullOrWhiteSpace(typeName) && typeName.IndexOf("GraphData", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<JObject> ResolveObjectList(JToken token, IReadOnlyDictionary<string, JObject> objectMap)
        {
            var results = new List<JObject>();
            if (!(token is JArray array))
            {
                return results;
            }

            foreach (var entry in array)
            {
                if (entry is JObject objectEntry && objectEntry["m_Id"] != null)
                {
                    var objectId = objectEntry.Value<string>("m_Id");
                    if (!string.IsNullOrWhiteSpace(objectId) && objectMap.TryGetValue(objectId, out var resolved))
                    {
                        results.Add(resolved);
                    }
                }
                else if (entry is JObject inlineObject)
                {
                    results.Add(inlineObject);
                }
            }

            return results;
        }

        private static object SimplifyProperty(JObject property)
        {
            return new
            {
                objectId = property.Value<string>("m_ObjectId"),
                type = property.Value<string>("m_Type"),
                displayName = FirstString(property, "m_DisplayName", "m_Name"),
                referenceName = FirstString(property, "m_RefNameGeneratedByDisplayName", "m_ReferenceName", "m_Name"),
                valueType = FirstString(property, "m_ValueType", "m_Type")
            };
        }

        private static object SimplifyKeyword(JObject keyword)
        {
            return new
            {
                objectId = keyword.Value<string>("m_ObjectId"),
                type = keyword.Value<string>("m_Type"),
                displayName = FirstString(keyword, "m_DisplayName", "m_Name"),
                referenceName = FirstString(keyword, "m_ReferenceName", "m_RefNameGeneratedByDisplayName", "m_Name"),
                definition = FirstString(keyword, "m_KeywordDefinition"),
                scope = FirstString(keyword, "m_Scope")
            };
        }

        private static object SimplifyNode(JObject node)
        {
            return new
            {
                objectId = node.Value<string>("m_ObjectId"),
                type = node.Value<string>("m_Type"),
                displayName = FirstString(node, "m_Name", "m_DisplayName"),
                position = node["m_DrawState"]?["m_Position"] != null ? new
                {
                    x = node["m_DrawState"]["m_Position"].Value<float?>("x"),
                    y = node["m_DrawState"]["m_Position"].Value<float?>("y"),
                    width = node["m_DrawState"]["m_Position"].Value<float?>("width"),
                    height = node["m_DrawState"]["m_Position"].Value<float?>("height")
                } : null,
                subGraphGuid = ExtractSubGraphGuid(node["m_SerializedSubGraph"]),
                slots = node["m_Slots"] is JArray slots ? slots.Count : 0
            };
        }

        private static object SimplifyEdge(JObject edge)
        {
            return new
            {
                objectId = edge.Value<string>("m_ObjectId"),
                outputNodeId = edge["m_OutputSlot"]?["m_Node"]?.Value<string>("m_Id"),
                outputSlotId = edge["m_OutputSlot"]?.Value<int?>("m_SlotId"),
                inputNodeId = edge["m_InputSlot"]?["m_Node"]?.Value<string>("m_Id"),
                inputSlotId = edge["m_InputSlot"]?.Value<int?>("m_SlotId")
            };
        }

        private static object SimplifyTarget(JObject target)
        {
            return new
            {
                objectId = target.Value<string>("m_ObjectId"),
                type = target.Value<string>("m_Type"),
                displayName = FirstString(target, "m_DisplayName", "m_Name", "m_TargetId"),
                activeSubTarget = target["m_ActiveSubTarget"]?.ToString(Newtonsoft.Json.Formatting.None)
            };
        }

        private static List<object> ResolveBlocks(JToken contextToken, IReadOnlyDictionary<string, JObject> objectMap)
        {
            if (!(contextToken is JObject contextObject))
            {
                return new List<object>();
            }

            return ResolveObjectList(contextObject["m_Blocks"], objectMap)
                .Select(block => new
                {
                    objectId = block.Value<string>("m_ObjectId"),
                    type = block.Value<string>("m_Type"),
                    descriptor = FirstString(block, "m_SerializedDescriptor", "m_Name")
                })
                .Cast<object>()
                .ToList();
        }

        private static object SimplifyObjectReference(JToken token, IReadOnlyDictionary<string, JObject> objectMap)
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

            return new
            {
                objectId,
                type = resolved.Value<string>("m_Type"),
                displayName = FirstString(resolved, "m_Name", "m_DisplayName")
            };
        }

        private static string FirstString(JObject source, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = source[key]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string ExtractSubGraphGuid(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                var text = token.Value<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                try
                {
                    var parsed = JObject.Parse(text);
                    return parsed.Value<string>("m_Guid") ?? parsed.Value<string>("guid");
                }
                catch
                {
                    return text;
                }
            }

            return token["m_Guid"]?.Value<string>() ?? token["guid"]?.Value<string>();
        }
    }
}
