using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TA.ReadOnlyUnityMcp.Compat.ShaderGraph
{
    internal sealed class GraphSchemaAdapter
    {
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

            var objectMap = objects
                .Where(obj => obj["m_ObjectId"] != null)
                .GroupBy(obj => obj.Value<string>("m_ObjectId"))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.Last());

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
    }
}
