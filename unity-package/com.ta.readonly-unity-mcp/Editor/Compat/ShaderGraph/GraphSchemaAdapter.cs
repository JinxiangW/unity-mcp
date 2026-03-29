using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TA.ReadOnlyUnityMcp.Compat.ShaderGraph
{
    internal sealed class GraphSchemaAdapter
    {
        // Legacy Shader Graph files use a single JSON object with serialized arrays.
        internal sealed class Envelope
        {
            public string format;
            public JObject root;
            public IReadOnlyDictionary<string, JObject> objectMap;
            public List<string> warnings;
        }

        public Envelope BuildEnvelope(List<JObject> objects)
        {
            var warnings = new List<string>();

            if (objects == null || objects.Count == 0)
            {
                return new Envelope
                {
                    format = "unknown",
                    root = null,
                    objectMap = new Dictionary<string, JObject>(),
                    warnings = warnings
                };
            }

            var root = FindRootObject(objects);
            if (root == null)
            {
                root = objects[0];
                warnings.Add("Shader Graph root object was not clearly identified; using the first parsed object.");
            }

            if (LooksLikeLegacyRootGraphObject(root))
            {
                root = NormalizeLegacyRoot(root, warnings);
            }

            var objectMap = objects
                .Where(obj => obj["m_ObjectId"] != null)
                .GroupBy(obj => obj.Value<string>("m_ObjectId"))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.Last());

            foreach (var candidate in ResolveObjectList(root["m_Properties"], objectMap)
                         .Concat(ResolveObjectList(root["m_Nodes"], objectMap)))
            {
                var objectId = candidate.Value<string>("m_ObjectId");
                if (!string.IsNullOrWhiteSpace(objectId) && !objectMap.ContainsKey(objectId))
                {
                    objectMap[objectId] = candidate;
                }
            }

            return new Envelope
            {
                format = root["m_Type"] != null ? "multi-json" : "single-json",
                root = root,
                objectMap = objectMap,
                warnings = warnings
            };
        }

        public List<JObject> ResolveObjectList(JToken token, IReadOnlyDictionary<string, JObject> objectMap)
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

        public string FirstString(JObject source, params string[] keys)
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

        public string ExtractSubGraphGuid(JToken token)
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

        private static JObject FindRootObject(List<JObject> objects)
        {
            return objects.FirstOrDefault(LooksLikeRootGraphObject);
        }

        private static bool LooksLikeRootGraphObject(JObject obj)
        {
            var typeName = obj.Value<string>("m_Type");
            return obj["m_Nodes"] != null
                   || obj["m_Properties"] != null
                   || obj["m_Edges"] != null
                   || (!string.IsNullOrWhiteSpace(typeName) && typeName.IndexOf("GraphData", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool LooksLikeLegacyRootGraphObject(JObject obj)
        {
            return obj["m_SerializedProperties"] != null
                   || obj["m_SerializableNodes"] != null
                   || obj["m_SerializableEdges"] != null;
        }

        private JObject NormalizeLegacyRoot(JObject root, List<string> warnings)
        {
            var normalizedRoot = new JObject
            {
                ["m_Type"] = "LegacyShaderGraph",
                ["m_Properties"] = ParseLegacySerializedArray(root["m_SerializedProperties"], "property"),
                ["m_Nodes"] = ParseLegacySerializedArray(root["m_SerializableNodes"], "node"),
                ["m_Edges"] = ParseLegacySerializedArray(root["m_SerializableEdges"], "edge"),
                ["m_CategoryData"] = new JArray(),
                ["m_Keywords"] = ParseLegacySerializedArray(root["m_SerializedKeywords"], "keyword"),
                ["m_ActiveTargets"] = new JArray(),
                ["m_VertexContext"] = new JObject { ["m_Blocks"] = new JArray() },
                ["m_FragmentContext"] = new JObject { ["m_Blocks"] = new JArray() }
            };

            var masterNode = ((JArray)normalizedRoot["m_Nodes"])
                .OfType<JObject>()
                .FirstOrDefault(node => (node.Value<string>("m_Type") ?? string.Empty).IndexOf("MasterNode", StringComparison.OrdinalIgnoreCase) >= 0);

            if (masterNode != null)
            {
                normalizedRoot["m_OutputNode"] = new JObject
                {
                    ["m_Id"] = masterNode.Value<string>("m_ObjectId")
                };
            }

            warnings.Add("Parsed legacy Shader Graph schema from older Unity/Shader Graph format.");
            return normalizedRoot;
        }

        private JArray ParseLegacySerializedArray(JToken token, string kind)
        {
            var result = new JArray();
            if (!(token is JArray array))
            {
                return result;
            }

            var index = 0;
            foreach (var entry in array.OfType<JObject>())
            {
                var parsed = ParseLegacyEntry(entry, kind, index);
                if (parsed != null)
                {
                    result.Add(parsed);
                }

                index++;
            }

            return result;
        }

        private JObject ParseLegacyEntry(JObject entry, string kind, int index)
        {
            var jsonNodeData = entry.Value<string>("JSONnodeData");
            JObject parsed;
            if (string.IsNullOrWhiteSpace(jsonNodeData))
            {
                parsed = new JObject();
            }
            else
            {
                try
                {
                    parsed = JObject.Parse(jsonNodeData);
                }
                catch
                {
                    parsed = new JObject();
                }
            }

            var typeName = entry["typeInfo"]?["fullName"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                parsed["m_Type"] = typeName;
            }

            switch (kind)
            {
                case "property":
                    parsed["m_ObjectId"] = parsed["m_ObjectId"] ?? parsed["m_Guid"]?["m_GuidSerialized"] ?? $"legacy-property-{index}";
                    break;

                case "node":
                    parsed["m_ObjectId"] = parsed["m_ObjectId"] ?? parsed["m_GuidSerialized"] ?? $"legacy-node-{index}";
                    parsed["m_Slots"] = parsed["m_Slots"] ?? parsed["m_SerializableSlots"] ?? new JArray();
                    break;

                case "edge":
                    parsed["m_ObjectId"] = parsed["m_ObjectId"] ?? $"legacy-edge-{index}";
                    break;

                case "keyword":
                    parsed["m_ObjectId"] = parsed["m_ObjectId"] ?? $"legacy-keyword-{index}";
                    break;
            }

            return parsed;
        }
    }
}
