using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace TA.ReadOnlyUnityMcp.Compat.ShaderGraph
{
    internal static class GraphEnvelopeReader
    {
        public static List<JObject> ParseObjects(string text)
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
    }
}
