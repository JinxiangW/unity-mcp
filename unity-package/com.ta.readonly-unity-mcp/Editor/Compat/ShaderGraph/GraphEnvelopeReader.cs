using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TA.ReadOnlyUnityMcp.Compat.ShaderGraph
{
    internal static class GraphEnvelopeReader
    {
        internal sealed class ParseResult
        {
            public List<JObject> objects = new List<JObject>();
            public List<string> warnings = new List<string>();
            public string parseError;
        }

        public static ParseResult ParseObjects(string text)
        {
            var result = new ParseResult();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.parseError = "Shader Graph source text was empty.";
                return result;
            }

            var depth = 0;
            var startIndex = -1;
            var inString = false;
            var escaping = false;
            var objectIndex = 0;

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
                        try
                        {
                            result.objects.Add(JObject.Parse(slice));
                        }
                        catch (JsonException exception)
                        {
                            result.warnings.Add($"Failed to parse Shader Graph JSON object {objectIndex}: {exception.Message}");
                        }

                        objectIndex++;
                        startIndex = -1;
                    }
                }
            }

            if (depth != 0 || inString)
            {
                result.warnings.Add("Shader Graph text ended with an incomplete JSON object.");
            }

            if (result.objects.Count == 0)
            {
                result.parseError = result.warnings.Count > 0
                    ? "Shader Graph text could not be parsed into JSON objects."
                    : "No JSON objects were found in the Shader Graph file.";
            }

            return result;
        }
    }
}
