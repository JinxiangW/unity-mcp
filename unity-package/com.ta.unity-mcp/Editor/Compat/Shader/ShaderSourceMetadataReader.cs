using System.IO;
using System.Text.RegularExpressions;

namespace TA.UnityMcp.Compat.Shader
{
    internal static class ShaderSourceMetadataReader
    {
        internal struct Result
        {
            public string fallback;
            public string customEditor;
        }

        public static Result Read(string absolutePath)
        {
            if (!File.Exists(absolutePath))
            {
                return default;
            }

            var source = File.ReadAllText(absolutePath);
            var fallbackMatch = Regex.Match(source, "\\bFallback\\s+\"?(?<value>[^\"\\r\\n]+)\"?", RegexOptions.IgnoreCase);
            var customEditorMatch = Regex.Match(source, "\\bCustomEditor\\s+\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);

            return new Result
            {
                fallback = fallbackMatch.Success ? fallbackMatch.Groups["value"].Value.Trim() : null,
                customEditor = customEditorMatch.Success ? customEditorMatch.Groups["value"].Value.Trim() : null
            };
        }
    }
}
